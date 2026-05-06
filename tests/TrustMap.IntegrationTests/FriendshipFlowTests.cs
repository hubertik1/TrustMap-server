using System.Net;
using System.Net.Http.Json;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Friends;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;
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
        Assert.Equal(bob.User.Handle, aliceFriends[0].User.Handle);
        Assert.Equal(alice.User.Handle, bobFriends[0].User.Handle);
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

    [Fact]
    public async Task Search_UsesHandle_AndRanksExactBeforePrefixAndContains()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var searchClient = new TestApiClient(factory.CreateClient());
        var prefixClient = new TestApiClient(factory.CreateClient());
        var containsClient = new TestApiClient(factory.CreateClient());

        var exact = await searchClient.SignInAsync("alice", "Exact Person");
        var prefix = await prefixClient.SignInAsync("alicefriend", "Prefix Person");
        var contains = await containsClient.SignInAsync("malice", "Contains Person");

        searchClient.Authenticate(exact.AccessToken);

        var exactBase = HandleBase(exact.User.Handle);
        var baseResults = await searchClient.SearchUsersAsync(exactBase, take: 10);
        var atBaseResults = await searchClient.SearchUsersAsync($"@{exactBase}", take: 10);
        var fullHandleResults = await searchClient.SearchUsersAsync(SpacedHandleQuery(exact.User.Handle), take: 10);

        Assert.Contains(baseResults, x => x.User.Id == exact.User.Id);
        Assert.Contains(baseResults, x => x.User.Id == prefix.User.Id);
        Assert.Contains(baseResults, x => x.User.Id == contains.User.Id);

        Assert.Equal(
            baseResults.Select(x => x.User.Id).ToArray(),
            atBaseResults.Select(x => x.User.Id).ToArray());

        Assert.Equal(exact.User.Id, fullHandleResults[0].User.Id);
        Assert.True(IndexOf(baseResults, exact.User.Id) < IndexOf(baseResults, contains.User.Id));
        Assert.True(IndexOf(baseResults, prefix.User.Id) < IndexOf(baseResults, contains.User.Id));
    }

    [Fact]
    public async Task Friendship_SurvivesHandleChange_AndPreservesSuffix_WhenAvailable()
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
        var acceptResponse = await bobClient.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        acceptResponse.EnsureSuccessStatusCode();

        var originalSuffix = HandleSuffix(bob.User.Handle);
        var updatedBob = await bobClient.UpdateMeAsync("bob renamed#9999", "Bobby");

        Assert.Equal($"bob-renamed#{originalSuffix}", updatedBob.Handle);

        var aliceFriends = await aliceClient.HttpClient.GetFromJsonAsync<IReadOnlyList<FriendDto>>("/friends", JsonDefaults.Options);
        var bobFriends = await bobClient.HttpClient.GetFromJsonAsync<IReadOnlyList<FriendDto>>("/friends", JsonDefaults.Options);

        Assert.NotNull(aliceFriends);
        Assert.NotNull(bobFriends);
        Assert.Single(aliceFriends);
        Assert.Single(bobFriends);
        Assert.Equal(updatedBob.Handle, aliceFriends[0].User.Handle);
        Assert.Equal("Bobby", aliceFriends[0].User.DisplayName);
        Assert.Equal(alice.User.Id, bobFriends[0].User.Id);
    }

    [Fact]
    public async Task FetchUserFriends_ReturnsAcceptedFriendsWithViewerRelationshipStatuses()
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

        await MakeFriendsAsync(aliceClient, bobClient, bob.User.Id);
        await MakeFriendsAsync(aliceClient, charlieClient, charlie.User.Id);
        _ = await charlieClient.SendFriendRequestAsync(bob.User.Id);

        var aliceFriends = await charlieClient.FetchUserFriendsAsync(alice.User.Id);

        Assert.Equal(2, aliceFriends.Count);
        Assert.Contains(aliceFriends, x => x.User.Id == bob.User.Id && x.RelationshipStatus == RelationshipStatus.OutgoingRequest);
        Assert.Contains(aliceFriends, x => x.User.Id == charlie.User.Id && x.RelationshipStatus == RelationshipStatus.Self);
    }

    [Fact]
    public async Task FetchUserFriends_PrivateListAsOtherUser_ReturnsForbidden()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        var updated = await aliceClient.UpdateMePrivacyAsync(friendListVisibility: VisibilityLevel.Private);
        Assert.Equal(VisibilityLevel.Private, updated.FriendListVisibility);
        Assert.True(updated.CanViewFriends);

        var response = await bobClient.HttpClient.GetAsync($"/users/{alice.User.Id}/friends");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FetchUserFriends_PrivateOwnList_StillWorks()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        await MakeFriendsAsync(aliceClient, bobClient, bob.User.Id);
        var updated = await aliceClient.UpdateMePrivacyAsync(friendListVisibility: VisibilityLevel.Private);

        var aliceFriends = await aliceClient.FetchUserFriendsAsync(alice.User.Id);

        Assert.Equal(VisibilityLevel.Private, updated.FriendListVisibility);
        Assert.True(updated.CanViewFriends);
        Assert.Single(aliceFriends);
        Assert.Equal(bob.User.Id, aliceFriends[0].User.Id);
        Assert.Equal(RelationshipStatus.Friends, aliceFriends[0].RelationshipStatus);
    }

    [Fact]
    public async Task PatchMePrivacy_UpdatesProfileAndViewerPermission()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        var updated = await aliceClient.UpdateMePrivacyAsync(friendListVisibility: VisibilityLevel.Private);
        var aliceViewedByBob = await bobClient.HttpClient.GetFromJsonAsync<UserProfileDto>(
            $"/users/{alice.User.Id}",
            JsonDefaults.Options);

        Assert.Equal(VisibilityLevel.Private, updated.FriendListVisibility);
        Assert.True(updated.CanViewFriends);
        Assert.NotNull(aliceViewedByBob);
        Assert.Equal(VisibilityLevel.Private, aliceViewedByBob!.FriendListVisibility);
        Assert.False(aliceViewedByBob.CanViewFriends);
    }

    [Fact]
    public async Task PendingRequest_SurvivesHandleChange()
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
        var updatedBob = await bobClient.UpdateMeAsync("bob renamed#0001", "Bobby Pending");

        var outgoing = await aliceClient.HttpClient.GetFromJsonAsync<IReadOnlyList<FriendRequestDto>>("/friend-requests/outgoing", JsonDefaults.Options);
        var incoming = await bobClient.HttpClient.GetFromJsonAsync<IReadOnlyList<FriendRequestDto>>("/friend-requests/incoming", JsonDefaults.Options);

        Assert.NotNull(outgoing);
        Assert.NotNull(incoming);
        Assert.Single(outgoing);
        Assert.Single(incoming);
        Assert.Equal(request.Id, outgoing[0].Id);
        Assert.Equal(request.Id, incoming[0].Id);
        Assert.Equal(updatedBob.Handle, outgoing[0].Receiver.Handle);
        Assert.Equal("Bobby Pending", outgoing[0].Receiver.DisplayName);
        Assert.Equal(updatedBob.Handle, incoming[0].Receiver.Handle);
    }

    private static string HandleBase(string handle) => handle.Split('#')[0];

    private static string HandleSuffix(string handle) => handle.Split('#')[1];

    private static string SpacedHandleQuery(string handle)
    {
        var parts = handle.Split('#');
        return $"@ {parts[0]} # {parts[1]}";
    }

    private static int IndexOf(IReadOnlyList<UserSearchResultDto> results, Guid userId) =>
        results
            .Select((item, index) => new { item.User.Id, Index = index })
            .Single(x => x.Id == userId)
            .Index;

    private static async Task MakeFriendsAsync(
        TestApiClient senderClient,
        TestApiClient receiverClient,
        Guid receiverUserId)
    {
        var request = await senderClient.SendFriendRequestAsync(receiverUserId);
        var response = await receiverClient.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        response.EnsureSuccessStatusCode();
    }
}
