using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class DeviceTokenFlowTests
{
    [Fact]
    public async Task PostDeviceTokenStoresTokenForCurrentUser()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("device-token-user", "Device Token User");
        client.Authenticate(session.AccessToken);

        var response = await RegisterTokenAsync(client, "00ABFF");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var token = await dbContext.DeviceTokens.AsNoTracking().SingleAsync();

        Assert.Equal(session.User.Id, token.UserId);
        Assert.Equal("00abff", token.Token);
        Assert.Equal("ios", token.Platform);
        Assert.Equal("development", token.Environment);
        Assert.Equal("1.2.3", token.AppVersion);
        Assert.Equal("456", token.BuildNumber);
        Assert.Null(token.RevokedAt);
        Assert.NotNull(token.LastSeenAt);
    }

    [Fact]
    public async Task PostSameDeviceTokenTwiceIsIdempotent()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("device-token-repeat", "Device Token Repeat");
        client.Authenticate(session.AccessToken);

        (await RegisterTokenAsync(client, "00abff")).EnsureSuccessStatusCode();
        (await RegisterTokenAsync(client, "00ABFF")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var token = await dbContext.DeviceTokens.AsNoTracking().SingleAsync();

        Assert.Equal(session.User.Id, token.UserId);
        Assert.Equal("00abff", token.Token);
    }

    [Fact]
    public async Task PostSameDeviceTokenUnderAnotherUserMovesOwnership()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("device-token-alice", "Device Token Alice");
        var bob = await bobClient.SignInAsync("device-token-bob", "Device Token Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        (await RegisterTokenAsync(aliceClient, "00abff")).EnsureSuccessStatusCode();
        (await RegisterTokenAsync(bobClient, "00abff")).EnsureSuccessStatusCode();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var token = await dbContext.DeviceTokens.AsNoTracking().SingleAsync();

        Assert.Equal(bob.User.Id, token.UserId);
        Assert.Equal("00abff", token.Token);
        Assert.Null(token.RevokedAt);
    }

    [Fact]
    public async Task DeleteDeviceTokenIsIdempotent()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("device-token-delete", "Device Token Delete");
        client.Authenticate(session.AccessToken);

        (await RegisterTokenAsync(client, "00abff")).EnsureSuccessStatusCode();

        var firstDelete = await client.HttpClient.DeleteAsync("/me/device-tokens/00ABFF");
        var secondDelete = await client.HttpClient.DeleteAsync("/me/device-tokens/00ABFF");

        Assert.Equal(HttpStatusCode.NoContent, firstDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondDelete.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var token = await dbContext.DeviceTokens.AsNoTracking().SingleAsync();

        Assert.NotNull(token.RevokedAt);
    }

    [Fact]
    public async Task SignedOutDeviceTokenRequestsAreRejected()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());

        var response = await RegisterTokenAsync(client, "00abff");
        var deleteResponse = await client.HttpClient.DeleteAsync("/me/device-tokens/00abff");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
    }

    private static Task<HttpResponseMessage> RegisterTokenAsync(TestApiClient client, string token) =>
        client.HttpClient.PostAsJsonAsync(
            "/me/device-tokens",
            new
            {
                token,
                platform = "ios",
                environment = "development",
                appVersion = "1.2.3",
                buildNumber = "456"
            });
}
