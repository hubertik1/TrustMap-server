using System.Net;
using System.Net.Http.Json;
using TrustMap.Api.Features.Friends;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class FriendshipFlowTests
{
    [Fact]
    public async Task SendAcceptAndListFriends_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        var request = await aliceClient.SendFriendRequestAsync(bob.User.Id);

        var incoming = await bobClient.HttpClient.GetFromJsonAsync<IReadOnlyList<FriendRequestDto>>("/friend-requests/incoming", JsonDefaults.Options);
        Assert.NotNull(incoming);
        Assert.Single(incoming);
        Assert.Equal(request.Id, incoming[0].Id);

        var acceptResponse = await bobClient.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        acceptResponse.EnsureSuccessStatusCode();

        var aliceFriendsResponse = await aliceClient.HttpClient.GetAsync("/friends");
        var bobFriendsResponse = await bobClient.HttpClient.GetAsync("/friends");

        Assert.True(aliceFriendsResponse.IsSuccessStatusCode, await aliceFriendsResponse.Content.ReadAsStringAsync());
        Assert.True(bobFriendsResponse.IsSuccessStatusCode, await bobFriendsResponse.Content.ReadAsStringAsync());

        var aliceFriends = await aliceFriendsResponse.Content.ReadFromJsonAsync<IReadOnlyList<FriendDto>>(JsonDefaults.Options);
        var bobFriends = await bobFriendsResponse.Content.ReadFromJsonAsync<IReadOnlyList<FriendDto>>(JsonDefaults.Options);

        Assert.NotNull(aliceFriends);
        Assert.NotNull(bobFriends);
        Assert.Single(aliceFriends);
        Assert.Single(bobFriends);
        Assert.Equal("bob", aliceFriends[0].User.Handle);
        Assert.Equal("alice", bobFriends[0].User.Handle);
    }

    [Fact]
    public async Task RejectAndCancel_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());
        var charlieClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");
        var charlie = await charlieClient.SignInAsync("charlie", "Charlie");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        charlieClient.Authenticate(charlie.AccessToken);

        var rejectRequest = await aliceClient.SendFriendRequestAsync(bob.User.Id);
        var rejectResponse = await bobClient.HttpClient.PostAsync($"/friend-requests/{rejectRequest.Id}/reject", content: null);
        rejectResponse.EnsureSuccessStatusCode();

        var cancelRequest = await aliceClient.SendFriendRequestAsync(charlie.User.Id);
        var cancelResponse = await aliceClient.HttpClient.PostAsync($"/friend-requests/{cancelRequest.Id}/cancel", content: null);
        cancelResponse.EnsureSuccessStatusCode();

        var bobIncoming = await bobClient.HttpClient.GetFromJsonAsync<IReadOnlyList<FriendRequestDto>>("/friend-requests/incoming", JsonDefaults.Options);
        var charlieIncoming = await charlieClient.HttpClient.GetFromJsonAsync<IReadOnlyList<FriendRequestDto>>("/friend-requests/incoming", JsonDefaults.Options);

        Assert.NotNull(bobIncoming);
        Assert.NotNull(charlieIncoming);
        Assert.Empty(bobIncoming);
        Assert.Empty(charlieIncoming);
    }
}
