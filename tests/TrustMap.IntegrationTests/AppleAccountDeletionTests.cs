using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class AppleAccountDeletionTests
{
    [Fact]
    public async Task Login_ExchangesCodeAndEncryptsToken_DeletionRevokesAndRemovesToken()
    {
        var apple = new FakeAppleTokens();
        await using var factory = CreateFactory(apple);
        await factory.InitializeAsync();
        var client = new TestApiClient(factory.CreateClient());
        var session = await LoginAsync(client, "apple-auto");
        Assert.Equal("fresh-code", apple.Code);
        Assert.Equal("raw-nonce", apple.Nonce);
        Assert.Equal("com.hubert.TrustMap", apple.ClientId);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var identity = await db.AppleIdentities.SingleAsync();
            Assert.NotEqual("apple-refresh-secret", identity.ProtectedRefreshToken);
            Assert.DoesNotContain("apple-refresh-secret", identity.ProtectedRefreshToken!);
            Assert.Equal("apple-refresh-secret", scope.ServiceProvider.GetRequiredService<AppleRefreshTokenProtection>()
                .Unprotect(identity.ProtectedRefreshToken!));
        }
        client.Authenticate(session.AccessToken);
        var response = await client.HttpClient.DeleteAsync("/me?includeResult=true");
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AccountDeletionResult>(JsonDefaults.Options);
        Assert.Equal("apple-refresh-secret", apple.RevokedToken);
        Assert.Equal("com.hubert.TrustMap", apple.RevokedClientId);
        await using var deletedScope = factory.Services.CreateAsyncScope();
        var deletedDb = deletedScope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        Assert.Empty(await deletedDb.AppleIdentities.ToListAsync());
        Assert.Empty(await deletedDb.RefreshTokens.ToListAsync());
        Assert.Empty(await deletedDb.AccountDeletionJobs.ToListAsync());
        Assert.Equal(new AccountDeletionResult(false, false), result);
    }

    [Theory]
    [InlineData(true, HttpStatusCode.Unauthorized)]
    [InlineData(false, HttpStatusCode.ServiceUnavailable)]
    public async Task FailedCodeExchange_DoesNotCreateUserOrSession(bool invalidGrant, HttpStatusCode status)
    {
        var apple = new FakeAppleTokens { ExchangeFailure = new AppleTokenException(invalidGrant) };
        await using var factory = CreateFactory(apple);
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/auth/apple", LoginRequest("failed-exchange"), JsonDefaults.Options);
        Assert.Equal(status, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.AppleIdentities.ToListAsync());
        Assert.Empty(await db.RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task MissingCode_IsRejectedWhenTokenManagementIsEnabled()
    {
        await using var factory = CreateFactory(new FakeAppleTokens());
        await factory.InitializeAsync();
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/apple",
            new AppleAuthRequest { IdentityToken = "apple:missing-code" }, JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task TokenlessLegacyAccount_StillDeletesAndReportsManualRevocation()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();
        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("legacy-apple", "Legacy");
        client.Authenticate(session.AccessToken);
        var response = await client.HttpClient.DeleteAsync("/me?includeResult=true");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new AccountDeletionResult(true, false),
            await response.Content.ReadFromJsonAsync<AccountDeletionResult>(JsonDefaults.Options));
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(UserAccountStatus.Deleted, (await scope.ServiceProvider.GetRequiredService<TrustMapDbContext>()
            .Users.SingleAsync()).Status);
    }

    [Fact]
    public async Task AppleOutage_DoesNotBlockDeletion_EncryptedJobRetriesInNewScope()
    {
        var apple = new FakeAppleTokens { FailRevocation = true };
        await using var factory = CreateFactory(apple);
        await factory.InitializeAsync();
        var client = new TestApiClient(factory.CreateClient());
        var session = await LoginAsync(client, "apple-outage");
        client.Authenticate(session.AccessToken);
        var response = await client.HttpClient.DeleteAsync("/me?includeResult=true");
        Assert.Equal(new AccountDeletionResult(false, true),
            await response.Content.ReadFromJsonAsync<AccountDeletionResult>(JsonDefaults.Options));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            Assert.Empty(await db.AppleIdentities.ToListAsync());
            var job = await db.AccountDeletionJobs.SingleAsync();
            Assert.Equal(AccountDeletionJobKind.AppleRevocation, job.Kind);
            Assert.DoesNotContain("apple-refresh-secret", job.Payload);
            Assert.Equal(1, job.Attempts);
            await scope.ServiceProvider.GetRequiredService<AccountDeletionJobProcessor>().ProcessPendingAsync(default);
            Assert.Equal(1, apple.RevocationAttempts); // Scheduled backoff is respected.
            await db.AccountDeletionJobs.ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }
        apple.FailRevocation = false;
        await using var retryScope = factory.Services.CreateAsyncScope();
        await retryScope.ServiceProvider.GetRequiredService<AccountDeletionJobProcessor>().ProcessPendingAsync(default);
        Assert.Equal(2, apple.RevocationAttempts);
        Assert.Empty(await retryScope.ServiceProvider.GetRequiredService<TrustMapDbContext>().AccountDeletionJobs.ToListAsync());
    }

    [Fact]
    public async Task DeletionTransactionFailure_RollsBackJobsAndIdentityDeletion()
    {
        var apple = new FakeAppleTokens();
        await using var factory = CreateFactory(apple, services =>
            services.AddDbContext<TrustMapDbContext>(options => options.AddInterceptors(new RejectDeletionSave())));
        await factory.InitializeAsync();
        var client = new TestApiClient(factory.CreateClient());
        var session = await LoginAsync(client, "delete-rollback");
        client.Authenticate(session.AccessToken);
        var response = await client.HttpClient.DeleteAsync("/me");
        Assert.False(response.IsSuccessStatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        Assert.Equal(UserAccountStatus.Active, (await db.Users.SingleAsync()).Status);
        Assert.Single(await db.AppleIdentities.ToListAsync());
        Assert.Single(await db.RefreshTokens.ToListAsync());
        Assert.Empty(await db.AccountDeletionJobs.ToListAsync());
        Assert.Equal(0, apple.RevocationAttempts);
    }

    private static AppleAuthRequest LoginRequest(string subject) => new()
    {
        IdentityToken = $"apple:{subject}", AuthorizationCode = "fresh-code", RawNonce = "raw-nonce",
        DisplayName = "Apple User", PreferredHandle = subject
    };

    private static async Task<AuthSessionDto> LoginAsync(TestApiClient client, string subject)
    {
        var response = await client.HttpClient.PostAsJsonAsync("/auth/apple", LoginRequest(subject), JsonDefaults.Options);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonDefaults.Options))!;
    }

    private static TrustMapWebApplicationFactory CreateFactory(FakeAppleTokens apple, Action<IServiceCollection>? configure = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new TrustMapWebApplicationFactory(services =>
        {
            services.RemoveAll<IAppleTokenClient>();
            services.AddSingleton<IAppleTokenClient>(apple);
            configure?.Invoke(services);
        }, new Dictionary<string, string?>
        {
            ["AppleAuth:TokenManagementEnabled"] = "true",
            ["AppleAuth:TeamId"] = "TESTTEAM",
            ["AppleAuth:KeyId"] = "TESTKEY",
            ["AppleAuth:PrivateKeyPem"] = key.ExportPkcs8PrivateKeyPem()
        });
    }

    private sealed class FakeAppleTokens : IAppleTokenClient
    {
        public Exception? ExchangeFailure { get; init; }
        public bool FailRevocation { get; set; }
        public string? Code { get; private set; }
        public string? Nonce { get; private set; }
        public string? ClientId { get; private set; }
        public string? RevokedToken { get; private set; }
        public string? RevokedClientId { get; private set; }
        public int RevocationAttempts { get; private set; }
        public Task<string> ExchangeAsync(string code, string clientId, string subject, string? rawNonce, CancellationToken cancellationToken)
        {
            if (ExchangeFailure is not null) throw ExchangeFailure;
            Code = code; Nonce = rawNonce; ClientId = clientId;
            return Task.FromResult("apple-refresh-secret");
        }
        public Task RevokeAsync(string refreshToken, string clientId, CancellationToken cancellationToken)
        {
            RevocationAttempts++;
            if (FailRevocation) throw new AppleTokenException();
            RevokedToken = refreshToken; RevokedClientId = clientId;
            return Task.CompletedTask;
        }
    }

    private sealed class RejectDeletionSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<User>().Any(x => x.Entity.Status == UserAccountStatus.Deleted))
                throw new InvalidOperationException("Forced deletion transaction failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
