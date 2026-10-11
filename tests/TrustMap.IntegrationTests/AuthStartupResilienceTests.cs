using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Services;
using TrustMap.Infrastructure.Authentication;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class AuthStartupResilienceTests
{
    [Fact]
    public async Task SlowAppleWarmup_DoesNotBlockHealthOrExistingSessionRefresh()
    {
        var configuration = new PendingConfiguration();
        await using var factory = CreateFactory(configuration, useRealVerifier: false);
        await factory.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await configuration.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var http = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/ready")).StatusCode);
        var session = await new TestApiClient(http).SignInAsync("existing-user", "Existing User");
        var refresh = await http.PostAsJsonAsync("/auth/refresh",
            new RefreshTokenRequest { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.False(WarmupTask(factory).IsCompleted);
    }

    [Fact]
    public async Task AppleMetadataOutage_Returns503WithoutCreatingSession_AndRecoversWithoutRestart()
    {
        using var key = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(key) { KeyId = "test-key" };
        var configuration = new RecoverableConfiguration();
        await using var factory = CreateFactory(configuration);
        await factory.InitializeAsync();
        await WarmupTask(factory).WaitAsync(TimeSpan.FromSeconds(5));
        using var http = factory.CreateClient();
        var request = new AppleAuthRequest { IdentityToken = CreateToken(signingKey) };

        var unavailable = await http.PostAsJsonAsync("/auth/apple", request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        var problem = await unavailable.Content.ReadAsStringAsync();
        Assert.Contains("apple_identity_service_unavailable", problem);
        Assert.DoesNotContain(request.IdentityToken, problem);
        Assert.DoesNotContain("private-provider-detail", problem);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            Assert.Empty(await db.Users.ToListAsync());
            Assert.Empty(await db.AppleIdentities.ToListAsync());
            Assert.Empty(await db.RefreshTokens.ToListAsync());
        }

        configuration.Recover(SigningConfiguration(signingKey));
        var recovered = await http.PostAsJsonAsync("/auth/apple", request);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        var session = await recovered.Content.ReadFromJsonAsync<AuthSessionDto>(JsonDefaults.Options);
        Assert.False(string.IsNullOrWhiteSpace(session!.RefreshToken));
    }

    [Fact]
    public async Task WarmupAndSignIn_ShareCachedSigningKeys()
    {
        using var key = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(key) { KeyId = "test-key" };
        var retriever = new CountingConfigurationRetriever(SigningConfiguration(signingKey));
        var configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://appleid.apple.com/.well-known/openid-configuration", retriever);
        await using var factory = CreateFactory(configuration);
        await factory.InitializeAsync();
        await WarmupTask(factory).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, retriever.Calls);
        using var http = factory.CreateClient();

        for (var i = 0; i < 2; i++)
        {
            var response = await http.PostAsJsonAsync("/auth/apple",
                new AppleAuthRequest { IdentityToken = CreateToken(signingKey) });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        Assert.Equal(1, retriever.Calls);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("expired")]
    [InlineData("nonce")]
    [InlineData("malformed")]
    public async Task InvalidAppleIdentity_Returns401WithoutCreatingSession(string invalidPart)
    {
        using var key = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);
        var signingKey = new RsaSecurityKey(key) { KeyId = "test-key" };
        var token = invalidPart == "malformed" ? "not-a-jwt" : CreateToken(
            invalidPart == "signature" ? new RsaSecurityKey(otherKey) { KeyId = "test-key" } : signingKey,
            audience: invalidPart == "audience" ? "wrong.app" : "com.hubertik.TrustMap",
            issuer: invalidPart == "issuer" ? "https://wrong.example" : "https://appleid.apple.com",
            expired: invalidPart == "expired",
            nonce: invalidPart == "nonce" ? "wrong-nonce" : null);
        await using var factory = CreateFactory(
            new StaticConfigurationManager<OpenIdConnectConfiguration>(SigningConfiguration(signingKey)));
        await factory.InitializeAsync();
        using var http = factory.CreateClient();

        var response = await http.PostAsJsonAsync("/auth/apple", new AppleAuthRequest { IdentityToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("invalid_apple_identity_token", await response.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TrustMapDbContext>().RefreshTokens.ToListAsync());
    }

    [Fact]
    public async Task MetadataHttpTimeout_IsAvailabilityFailure_AndCallerCancellationIsPreserved()
    {
        using var key = RSA.Create(2048);
        var options = Microsoft.Extensions.Options.Options.Create(new AppleAuthOptions
        {
            AllowedAudiences = ["com.hubertik.TrustMap"]
        });
        var verifier = new AppleIdentityTokenVerifier(options, new TimedOutConfiguration());
        var token = CreateToken(new RsaSecurityKey(key));
        await Assert.ThrowsAsync<AppleIdentityProviderUnavailableException>(
            () => verifier.VerifyAsync(token, null, default));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => verifier.VerifyAsync(token, null, cancellation.Token));
    }

    private static TrustMapWebApplicationFactory CreateFactory(
        IConfigurationManager<OpenIdConnectConfiguration> configuration, bool useRealVerifier = true) => new(services =>
        {
            services.RemoveAll<IConfigurationManager<OpenIdConnectConfiguration>>();
            services.AddSingleton(configuration);
            if (useRealVerifier)
            {
                services.RemoveAll<IAppleIdentityTokenVerifier>();
                services.AddSingleton<IAppleIdentityTokenVerifier, AppleIdentityTokenVerifier>();
            }
        });

    private static Task WarmupTask(TrustMapWebApplicationFactory factory) => factory.Services
        .GetServices<IHostedService>().OfType<AppleSignInWarmupService>().Single().ExecuteTask!;

    private static OpenIdConnectConfiguration SigningConfiguration(SecurityKey signingKey)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = "https://appleid.apple.com" };
        configuration.SigningKeys.Add(signingKey);
        return configuration;
    }

    private static string CreateToken(SecurityKey key, string audience = "com.hubertik.TrustMap",
        string issuer = "https://appleid.apple.com", bool expired = false, string? nonce = null)
    {
        var claims = new List<Claim> { new("sub", "test-apple-user"), new("email", "apple-user@example.com") };
        if (nonce is not null) claims.Add(new Claim("nonce", nonce));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: issuer, audience: audience, claims: claims,
            notBefore: DateTime.UtcNow.AddHours(-1),
            expires: expired ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256)));
    }

    private sealed class PendingConfiguration : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<OpenIdConnectConfiguration> _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return _pending.Task.WaitAsync(cancellationToken);
        }
        public void RequestRefresh() { }
    }

    private sealed class RecoverableConfiguration : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private OpenIdConnectConfiguration? _configuration;
        public void Recover(OpenIdConnectConfiguration configuration) => Volatile.Write(ref _configuration, configuration);
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
            Volatile.Read(ref _configuration) is { } configuration
                ? Task.FromResult(configuration)
                : Task.FromException<OpenIdConnectConfiguration>(new HttpRequestException("private-provider-detail"));
        public void RequestRefresh() { }
    }

    private sealed class TimedOutConfiguration : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<OpenIdConnectConfiguration>(new TaskCanceledException("HTTP timeout"));
        }
        public void RequestRefresh() { }
    }

    private sealed class CountingConfigurationRetriever(OpenIdConnectConfiguration configuration) : IConfigurationRetriever<OpenIdConnectConfiguration>
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(string address, IDocumentRetriever retriever, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(configuration);
        }
    }
}
