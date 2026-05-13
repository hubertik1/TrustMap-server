using System.Net.Http.Json;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Map;
using TrustMap.Domain.Enums;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class MapPinsEndpointTests
{
    [Fact]
    public async Task Pins_ReturnOwnVisiblePlaces()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "pins-own-alice", "Alice");

        var placeId = await alice.Client.CreatePlaceAsync("pins-own", 50.0001, 19.0001);
        await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 4, "Alice private");

        var pins = await FetchPinsAsync(alice.Client);

        var pin = Assert.Single(pins, x => x.PlaceId == placeId);
        Assert.Equal("Address pins-own", pin.DisplayName);
        Assert.Equal(50.0001, pin.Latitude);
        Assert.Equal(19.0001, pin.Longitude);
        Assert.True(pin.IsReviewedByCurrentUser);
        Assert.Equal(1, pin.ReviewCount);
        Assert.Equal(1, pin.ContributorCount);
    }

    [Fact]
    public async Task Pins_ReturnFriendPlacesVisibleByPrivacy()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "pins-friend-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "pins-friend-bob", "Bob");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var friendsPlaceId = await bob.Client.CreatePlaceAsync("pins-friend-friends", 50.0002, 19.0002);
        var publicPlaceId = await bob.Client.CreatePlaceAsync("pins-friend-public", 50.0003, 19.0003);

        await bob.Client.CreatePlaceReviewAsync(friendsPlaceId, VisibilityLevel.Friends, 5, "Bob friends");
        await bob.Client.CreatePlaceReviewAsync(publicPlaceId, VisibilityLevel.Public, 3, "Bob public");

        var pins = await FetchPinsAsync(alice.Client);

        Assert.Contains(pins, x => x.PlaceId == friendsPlaceId);
        Assert.Contains(pins, x => x.PlaceId == publicPlaceId);
    }

    [Fact]
    public async Task Pins_DoNotReturnPrivatePlacesFromOtherUsers()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "pins-private-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "pins-private-bob", "Bob");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var privatePlaceId = await bob.Client.CreatePlaceAsync("pins-private-bob-place", 50.0004, 19.0004);
        await bob.Client.CreatePlaceReviewAsync(privatePlaceId, VisibilityLevel.Private, 5, "Bob private");

        var pins = await FetchPinsAsync(alice.Client);

        Assert.DoesNotContain(pins, x => x.PlaceId == privatePlaceId);
    }

    [Fact]
    public async Task Pins_FilterByBounds()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "pins-bounds-alice", "Alice");

        var insidePlaceId = await alice.Client.CreatePlaceAsync("pins-bounds-inside", 50.1001, 19.1001);
        var outsidePlaceId = await alice.Client.CreatePlaceAsync("pins-bounds-outside", 52.1001, 21.1001);

        await alice.Client.CreatePlaceReviewAsync(insidePlaceId, VisibilityLevel.Private, 4, "Inside");
        await alice.Client.CreatePlaceReviewAsync(outsidePlaceId, VisibilityLevel.Private, 5, "Outside");

        var pins = await FetchPinsAsync(alice.Client, north: 51, south: 49, east: 20, west: 18);

        Assert.Contains(pins, x => x.PlaceId == insidePlaceId);
        Assert.DoesNotContain(pins, x => x.PlaceId == outsidePlaceId);
    }

    [Fact]
    public async Task Pins_ReturnCategoryIds()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "pins-categories-alice", "Alice");

        var restaurantPlaceId = await alice.Client.CreatePlaceAsync("pins-category-restaurant", 50.0005, 19.0005);
        var barPlaceId = await alice.Client.CreatePlaceAsync("pins-category-bar", 50.0006, 19.0006);

        await alice.Client.CreatePlaceReviewAsync(restaurantPlaceId, VisibilityLevel.Private, 4, "Restaurant");
        await alice.Client.CreatePlaceReviewAsync(
            barPlaceId,
            VisibilityLevel.Private,
            5,
            "Bar",
            TestApiClient.AlternateDefaultCategoryId);

        var pins = await FetchPinsAsync(alice.Client);

        var restaurantPin = Assert.Single(pins, x => x.PlaceId == restaurantPlaceId);
        var barPin = Assert.Single(pins, x => x.PlaceId == barPlaceId);
        Assert.Contains(TestApiClient.DefaultCategoryId, restaurantPin.CategoryIds);
        Assert.Contains(TestApiClient.AlternateDefaultCategoryId, barPin.CategoryIds);
    }

    [Fact]
    public async Task PinByPlaceId_ReturnsNull_WhenPinIsNotVisible()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "pins-single-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "pins-single-bob", "Bob");

        var bobPlaceId = await bob.Client.CreatePlaceAsync("pins-single-private", 50.0007, 19.0007);
        await bob.Client.CreatePlaceReviewAsync(bobPlaceId, VisibilityLevel.Private, 4, "Bob private");

        var pin = await FetchPinAsync(alice.Client, bobPlaceId);

        Assert.Null(pin);
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

    private static async Task<IReadOnlyList<MapPinDto>> FetchPinsAsync(
        TestApiClient client,
        double north = 51,
        double south = 49,
        double east = 20,
        double west = 18)
    {
        var response = await client.HttpClient.GetAsync($"/map/pins?north={north}&south={south}&east={east}&west={west}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var pins = await response.Content.ReadFromJsonAsync<IReadOnlyList<MapPinDto>>(JsonDefaults.Options);
        Assert.NotNull(pins);
        return pins!;
    }

    private static async Task<MapPinDto?> FetchPinAsync(TestApiClient client, Guid placeId)
    {
        var response = await client.HttpClient.GetAsync($"/map/pins/{placeId}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        return await response.Content.ReadFromJsonAsync<MapPinDto?>(JsonDefaults.Options);
    }
}
