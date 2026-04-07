using System.Net;
using System.Net.Http.Json;
using TrustMap.Api.Features.Auth;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class AuthFlowTests
{
    [Fact]
    public async Task AppleExchange_Refresh_Logout_Works_EndToEnd()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");

        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(session.RefreshToken));
        Assert.Equal("alice", session.User.Handle);

        var refreshResponse = await client.HttpClient.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshTokenRequest
            {
                RefreshToken = session.RefreshToken
            });

        refreshResponse.EnsureSuccessStatusCode();
        var refreshed = (await refreshResponse.Content.ReadFromJsonAsync<AuthSessionDto>(JsonDefaults.Options))!;

        Assert.NotEqual(session.RefreshToken, refreshed.RefreshToken);
        client.Authenticate(refreshed.AccessToken);

        var logoutResponse = await client.HttpClient.PostAsJsonAsync(
            "/auth/logout",
            new LogoutRequest
            {
                RefreshToken = refreshed.RefreshToken
            });

        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var secondRefreshResponse = await client.HttpClient.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshTokenRequest
            {
                RefreshToken = refreshed.RefreshToken
            });

        Assert.Equal(HttpStatusCode.Unauthorized, secondRefreshResponse.StatusCode);
    }
}
