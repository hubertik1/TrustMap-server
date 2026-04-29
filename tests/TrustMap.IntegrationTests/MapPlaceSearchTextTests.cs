using System.Text.Json;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Map;
using TrustMap.Domain.Enums;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class MapPlaceSearchTextTests
{
    [Fact]
    public async Task Map_SearchText_IncludesVisiblePlaceReviewText()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "search-place-alice", "Alice");
        var placeId = await alice.Client.CreatePlaceAsync("quiet-patio-search", 50.1001, 19.1001);

        await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 5, "quiet patio");

        var mapPlace = await FetchSingleMapPlaceAsync(alice.Client, placeId);

        Assert.Contains("quiet patio", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Map_SearchText_IncludesVisibleDishNameAndReviewBody()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "search-dish-alice", "Alice");
        var placeId = await alice.Client.CreatePlaceAsync("hot-dog-search", 50.1002, 19.1002);
        var placeReviewId = await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 5, "visible place anchor");

        await alice.Client.CreateDishReviewAsync(
            placeId,
            placeReviewId,
            VisibilityLevel.Private,
            "Hot dog",
            5,
            body: "crispy and cheap");

        var mapPlace = await FetchSingleMapPlaceAsync(alice.Client, placeId);

        Assert.Contains("Hot dog", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("crispy and cheap", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Map_SearchText_DoesNotIncludePrivateReviewFromOtherUser()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "search-private-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "search-private-bob", "Bob");
        var placeId = await alice.Client.CreatePlaceAsync("private-search-leak", 50.1003, 19.1003);

        await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 5, "visible anchor");
        await bob.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 1, "secret ramen");

        var (content, mapPlaces) = await FetchMapPlacesWithContentAsync(alice.Client);
        var mapPlace = Assert.Single(mapPlaces, x => x.PlaceId == placeId);

        Assert.Contains("visible anchor", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret ramen", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret ramen", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Map_SearchText_RespectsFriendsVisibility()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "search-friends-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "search-friends-bob", "Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "search-friends-charlie", "Charlie");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var placeId = await alice.Client.CreatePlaceAsync("friends-search", 50.1004, 19.1004);

        await bob.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 5, "friend phrase pierogi");
        await charlie.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 1, "nonfriend hidden phrase");

        var (content, mapPlaces) = await FetchMapPlacesWithContentAsync(alice.Client);
        var mapPlace = Assert.Single(mapPlaces, x => x.PlaceId == placeId);

        Assert.Contains("friend phrase pierogi", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nonfriend hidden phrase", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nonfriend hidden phrase", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Map_SearchText_DoesNotIncludeDeletedDishReviewText()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "search-deleted-alice", "Alice");
        var placeId = await alice.Client.CreatePlaceAsync("deleted-search", 50.1005, 19.1005);
        var placeReviewId = await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 5, "visible after delete");
        var dishReviewId = await alice.Client.CreateDishReviewAsync(
            placeId,
            placeReviewId,
            VisibilityLevel.Private,
            "Temporary noodles",
            2,
            body: "temporary secret crunch");

        var deleteResponse = await alice.Client.HttpClient.DeleteAsync($"/dish-reviews/{dishReviewId}");
        Assert.True(deleteResponse.IsSuccessStatusCode, await deleteResponse.Content.ReadAsStringAsync());

        var (content, mapPlaces) = await FetchMapPlacesWithContentAsync(alice.Client);
        var mapPlace = Assert.Single(mapPlaces, x => x.PlaceId == placeId);

        Assert.Contains("visible after delete", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Temporary noodles", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("temporary secret crunch", mapPlace.SearchText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Temporary noodles", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("temporary secret crunch", content, StringComparison.OrdinalIgnoreCase);
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
        var mapPlaces = await FetchMapPlacesAsync(client);
        return Assert.Single(mapPlaces, x => x.PlaceId == placeId);
    }

    private static async Task<IReadOnlyList<MapPlaceDto>> FetchMapPlacesAsync(TestApiClient client)
    {
        var (_, mapPlaces) = await FetchMapPlacesWithContentAsync(client);
        return mapPlaces;
    }

    private static async Task<(string Content, IReadOnlyList<MapPlaceDto> MapPlaces)> FetchMapPlacesWithContentAsync(TestApiClient client)
    {
        var response = await client.HttpClient.GetAsync("/map/places?north=51&south=49&east=20&west=18");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var content = await response.Content.ReadAsStringAsync();
        var mapPlaces = JsonSerializer.Deserialize<IReadOnlyList<MapPlaceDto>>(content, JsonDefaults.Options);
        Assert.NotNull(mapPlaces);
        Assert.Contains("\"searchText\"", content, StringComparison.Ordinal);
        return (content, mapPlaces!);
    }
}
