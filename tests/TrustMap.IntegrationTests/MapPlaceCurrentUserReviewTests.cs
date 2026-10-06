using System.Text.Json;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Map;
using TrustMap.Domain.Enums;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class MapPlaceCurrentUserReviewTests
{
    [Fact]
    public async Task Map_MarksPlaceReviewedByCurrentUser_WhenPlaceWasCreatedBySomeoneElse()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "mine-created-by-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "mine-reviewed-by-bob", "Bob");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var placeId = await alice.Client.CreatePlaceAsync("mine-created-by-other", 50.2001, 19.2001);
        await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Alice created it");
        await bob.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 5, "Bob reviewed it too");

        var (content, mapPlaces) = await FetchMapPlacesWithContentAsync(bob.Client);
        var mapPlace = Assert.Single(mapPlaces, x => x.PlaceId == placeId);

        Assert.Equal(alice.Session.User.Id, mapPlace.CreatedByUserId);
        Assert.True(mapPlace.IsReviewedByCurrentUser);
        Assert.Contains("\"isReviewedByCurrentUser\":true", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Map_DoesNotMarkPlaceCreatedByCurrentUser_WhenCurrentUserDidNotWritePlaceReview()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "mine-created-no-review-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "mine-created-no-review-bob", "Bob");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var placeId = await alice.Client.CreatePlaceAsync("mine-created-no-review", 50.2002, 19.2002);
        var aliceAnchorReviewId = await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Alice temporary access anchor");
        await bob.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 5, "Bob visible review");
        var deleteAnchorResponse = await alice.Client.HttpClient.DeleteAsync($"/place-reviews/{aliceAnchorReviewId}");
        deleteAnchorResponse.EnsureSuccessStatusCode();

        var mapPlace = await FetchSingleMapPlaceAsync(alice.Client, placeId);

        Assert.Equal(alice.Session.User.Id, mapPlace.CreatedByUserId);
        Assert.False(mapPlace.IsReviewedByCurrentUser);
    }

    [Fact]
    public async Task Map_DoesNotMarkCurrentUserDishReview_WhenCurrentUserDidNotWritePlaceReview()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "mine-dish-only-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "mine-dish-only-bob", "Bob");

        var friendRequest = await bob.Client.SendFriendRequestAsync(alice.Session.User.Id);
        (await alice.Client.HttpClient.PostAsync($"/friend-requests/{friendRequest.Id}/accept", null)).EnsureSuccessStatusCode();
        var placeId = await bob.Client.CreatePlaceAsync("mine-dish-only", 50.2003, 19.2003);
        var bobAnchorReviewId = await bob.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 4, "Bob restaurant anchor");
        await alice.Client.CreateDishReviewAsync(placeId, placeReviewId: null, VisibilityLevel.Private, "Hot dog", 5);
        var deleteAnchorResponse = await bob.Client.HttpClient.DeleteAsync($"/place-reviews/{bobAnchorReviewId}");
        deleteAnchorResponse.EnsureSuccessStatusCode();

        var mapPlace = await FetchSingleMapPlaceAsync(alice.Client, placeId);

        Assert.Equal(bob.Session.User.Id, mapPlace.CreatedByUserId);
        Assert.False(mapPlace.IsReviewedByCurrentUser);
        Assert.Equal(0, mapPlace.VisiblePlaceReviewCount);
        Assert.Equal(1, mapPlace.VisibleDishReviewCount);
    }

    [Fact]
    public async Task Map_MarksCurrentUserPrivatePlaceReview()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "mine-private-alice", "Alice");

        var placeId = await alice.Client.CreatePlaceAsync("mine-private", 50.2004, 19.2004);
        await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 5, "Alice private review");

        var mapPlace = await FetchSingleMapPlaceAsync(alice.Client, placeId);

        Assert.True(mapPlace.IsReviewedByCurrentUser);
        Assert.Equal(1, mapPlace.VisiblePlaceReviewCount);
    }

    [Fact]
    public async Task Map_DoesNotMarkOtherUserReviewOnly()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "mine-other-only-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "mine-other-only-bob", "Bob");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var placeId = await bob.Client.CreatePlaceAsync("mine-other-only", 50.2005, 19.2005);
        await bob.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Bob visible review");

        var mapPlace = await FetchSingleMapPlaceAsync(alice.Client, placeId);

        Assert.Equal(bob.Session.User.Id, mapPlace.CreatedByUserId);
        Assert.False(mapPlace.IsReviewedByCurrentUser);
        Assert.Equal(1, mapPlace.VisiblePlaceReviewCount);
    }

    private static async Task<(TestApiClient Client, AuthSessionDto Session)> CreateAuthenticatedClientAsync(
        TrustMapWebApplicationFactory factory,
        string subject,
        string displayName)
    {
        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync(subject, displayName);
        client.Authenticate(session.AccessToken);
        return (client, session);
    }

    private static async Task MakeFriendsAsync(TestApiClient requester, TestApiClient receiver, Guid receiverUserId)
    {
        var friendRequest = await requester.SendFriendRequestAsync(receiverUserId);
        var response = await receiver.HttpClient.PostAsync($"/friend-requests/{friendRequest.Id}/accept", content: null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<MapPlaceDto> FetchSingleMapPlaceAsync(TestApiClient client, Guid placeId)
    {
        var (_, mapPlaces) = await FetchMapPlacesWithContentAsync(client);
        return Assert.Single(mapPlaces, x => x.PlaceId == placeId);
    }

    private static async Task<(string Content, IReadOnlyList<MapPlaceDto> MapPlaces)> FetchMapPlacesWithContentAsync(TestApiClient client)
    {
        var response = await client.HttpClient.GetAsync("/map/places?north=51&south=49&east=20&west=18");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var content = await response.Content.ReadAsStringAsync();
        var mapPlaces = JsonSerializer.Deserialize<IReadOnlyList<MapPlaceDto>>(content, JsonDefaults.Options);
        Assert.NotNull(mapPlaces);
        Assert.Contains("\"isReviewedByCurrentUser\"", content, StringComparison.Ordinal);
        return (content, mapPlaces!);
    }
}
