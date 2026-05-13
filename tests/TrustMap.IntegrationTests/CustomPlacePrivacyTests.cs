using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Feed;
using TrustMap.Api.Features.Map;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Reviews;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class CustomPlacePrivacyTests
{
    [Fact]
    public async Task UserCreatedPlace_IsPrivateCustomByDefault_AndVisibleToOwner()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "custom-default-alice", "Alice");

        var place = await alice.Client.CreatePlaceSummaryAsync("custom-default", 50.3001, 19.3001);

        Assert.Equal(PlaceSourceType.CustomPin, place.SourceType);
        Assert.True(place.CanEditCustomDisplayName);
        Assert.Equal(alice.Session.User.Id, place.CreatedByUserId);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var stored = await dbContext.Places.AsNoTracking().SingleAsync(x => x.Id == place.Id);

        Assert.Equal(PlaceSourceType.CustomPin, stored.SourceType);
        Assert.Equal(alice.Session.User.Id, stored.CreatedByUserId);
        Assert.False(stored.IsPubliclyListed);

        var places = await FetchPlacesAsync(alice.Client);
        Assert.Contains(places, x => x.Id == place.Id);
    }

    [Fact]
    public async Task ProviderVenue_IsPubliclyListed_AndGloballySearchable()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "provider-public-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "provider-public-bob", "Bob");

        var place = await alice.Client.CreatePlaceSummaryAsync(
            "external-public-cafe",
            50.3002,
            19.3002,
            provider: "test-provider",
            providerPlaceId: "external-public-cafe");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var stored = await dbContext.Places.AsNoTracking().SingleAsync(x => x.Id == place.Id);

        Assert.Equal(PlaceSourceType.ProviderVenue, stored.SourceType);
        Assert.True(stored.IsPubliclyListed);
        Assert.Null(stored.CreatedByUserId);

        var search = await SearchPlacesAsync(bob.Client, "external-public-cafe");
        Assert.Contains(search, x => x.Id == place.Id);

        var detail = await bob.Client.HttpClient.GetFromJsonAsync<PlaceDetailDto>(
            $"/places/{place.Id}",
            JsonDefaults.Options);
        Assert.NotNull(detail);
        Assert.Equal(place.Id, detail!.Id);
    }

    [Fact]
    public async Task CustomPlaceWithoutVisibleReview_IsHiddenFromStrangerListSearchAndDetail()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "custom-hidden-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "custom-hidden-bob", "Bob");

        var place = await alice.Client.CreatePlaceSummaryAsync("secret-rooftop", 50.3003, 19.3003);

        Assert.Contains(await FetchPlacesAsync(alice.Client), x => x.Id == place.Id);
        Assert.Contains(await SearchPlacesAsync(alice.Client, "secret-rooftop"), x => x.Id == place.Id);
        Assert.Contains(await SearchPlacesAsync(alice.Client, "Address secret-rooftop"), x => x.Id == place.Id);

        var ownerDetail = await alice.Client.HttpClient.GetFromJsonAsync<PlaceDetailDto>(
            $"/places/{place.Id}",
            JsonDefaults.Options);
        Assert.NotNull(ownerDetail);

        Assert.DoesNotContain(await FetchPlacesAsync(bob.Client), x => x.Id == place.Id);
        Assert.DoesNotContain(await SearchPlacesAsync(bob.Client, "secret-rooftop"), x => x.Id == place.Id);
        Assert.DoesNotContain(await SearchPlacesAsync(bob.Client, "Address secret-rooftop"), x => x.Id == place.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.HttpClient.GetAsync($"/places/{place.Id}")).StatusCode);
    }

    [Fact]
    public async Task FriendsOnlyReview_MakesCustomPlaceVisibleToFriendOnly()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "custom-friend-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "custom-friend-bob", "Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "custom-friend-charlie", "Charlie");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var place = await alice.Client.CreatePlaceSummaryAsync("friends-only-hideout", 50.3004, 19.3004);
        await alice.Client.CreatePlaceReviewAsync(place.Id, VisibilityLevel.Friends, 5, "Friends can see this pin");

        Assert.Contains(await FetchPlacesAsync(bob.Client), x => x.Id == place.Id);
        Assert.Contains(await SearchPlacesAsync(bob.Client, "friends-only-hideout"), x => x.Id == place.Id);
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.HttpClient.GetAsync($"/places/{place.Id}")).StatusCode);

        Assert.DoesNotContain(await FetchPlacesAsync(charlie.Client), x => x.Id == place.Id);
        Assert.DoesNotContain(await SearchPlacesAsync(charlie.Client, "friends-only-hideout"), x => x.Id == place.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await charlie.Client.HttpClient.GetAsync($"/places/{place.Id}")).StatusCode);
    }

    [Fact]
    public async Task ReviewVisibilityControlsCustomPlaceVisibilityForStrangers()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "custom-review-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "custom-review-bob", "Bob");

        var publicPlace = await alice.Client.CreatePlaceSummaryAsync("public-review-pin", 50.3005, 19.3005);
        var privatePlace = await alice.Client.CreatePlaceSummaryAsync("private-review-pin", 50.3006, 19.3006);

        await alice.Client.CreatePlaceReviewAsync(publicPlace.Id, VisibilityLevel.Public, 5, "Public review opens the pin");
        await alice.Client.CreatePlaceReviewAsync(privatePlace.Id, VisibilityLevel.Private, 5, "Private review keeps the pin hidden");

        Assert.Contains(await SearchPlacesAsync(bob.Client, "public-review-pin"), x => x.Id == publicPlace.Id);
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.HttpClient.GetAsync($"/places/{publicPlace.Id}")).StatusCode);

        Assert.DoesNotContain(await SearchPlacesAsync(bob.Client, "private-review-pin"), x => x.Id == privatePlace.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.HttpClient.GetAsync($"/places/{privatePlace.Id}")).StatusCode);
    }

    [Fact]
    public async Task ReviewCreation_CannotAttachToInaccessibleCustomPlace()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "custom-review-create-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "custom-review-create-bob", "Bob");

        var place = await alice.Client.CreatePlaceSummaryAsync("review-create-private", 50.3007, 19.3007);

        var placeReviewResponse = await CreatePlaceReviewRawAsync(
            bob.Client,
            place.Id,
            VisibilityLevel.Public,
            "Bob should not attach");
        var dishReviewResponse = await CreateDishReviewRawAsync(
            bob.Client,
            place.Id,
            VisibilityLevel.Public,
            "Hidden dish");

        Assert.Equal(HttpStatusCode.NotFound, placeReviewResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, dishReviewResponse.StatusCode);

        await alice.Client.CreatePlaceReviewAsync(place.Id, VisibilityLevel.Public, 5, "Public anchor");
        var allowedResponse = await CreatePlaceReviewRawAsync(
            bob.Client,
            place.Id,
            VisibilityLevel.Public,
            "Bob can attach after visible review");

        Assert.Equal(HttpStatusCode.Created, allowedResponse.StatusCode);
    }

    [Fact]
    public async Task MapAndFeed_DoNotLeakInvisibleCustomPlaces()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "custom-map-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "custom-map-bob", "Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "custom-map-charlie", "Charlie");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var privatePlace = await alice.Client.CreatePlaceSummaryAsync("map-private-pin", 50.3008, 19.3008);
        var friendsPlace = await alice.Client.CreatePlaceSummaryAsync("map-friends-pin", 50.3009, 19.3009);
        await alice.Client.CreatePlaceReviewAsync(privatePlace.Id, VisibilityLevel.Private, 5, "Private map review");
        await alice.Client.CreatePlaceReviewAsync(friendsPlace.Id, VisibilityLevel.Friends, 5, "Friends map review");

        var bobMapPlaces = await FetchMapPlacesAsync(bob.Client);
        var bobFeed = await FetchFeedAsync(bob.Client);
        var charlieMapPlaces = await FetchMapPlacesAsync(charlie.Client);
        var charlieFeed = await FetchFeedAsync(charlie.Client);

        Assert.DoesNotContain(bobMapPlaces, x => x.PlaceId == privatePlace.Id);
        Assert.DoesNotContain(bobFeed.Items, x => x.Place.Id == privatePlace.Id);
        Assert.Contains(bobMapPlaces, x => x.PlaceId == friendsPlace.Id);
        Assert.Contains(bobFeed.Items, x => x.Place.Id == friendsPlace.Id);

        Assert.DoesNotContain(charlieMapPlaces, x => x.PlaceId == privatePlace.Id);
        Assert.DoesNotContain(charlieFeed.Items, x => x.Place.Id == privatePlace.Id);
        Assert.DoesNotContain(charlieMapPlaces, x => x.PlaceId == friendsPlace.Id);
        Assert.DoesNotContain(charlieFeed.Items, x => x.Place.Id == friendsPlace.Id);
    }

    [Fact]
    public async Task IdenticalCustomPinCanBeCreatedSeparatelyByDifferentUsers()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "custom-duplicate-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "custom-duplicate-bob", "Bob");

        var alicePlace = await alice.Client.CreatePlaceSummaryAsync("same-private-pin", 50.3010, 19.3010);
        var bobPlace = await bob.Client.CreatePlaceSummaryAsync("same-private-pin", 50.3010, 19.3010);

        Assert.NotEqual(alicePlace.Id, bobPlace.Id);
        Assert.Equal(alice.Session.User.Id, alicePlace.CreatedByUserId);
        Assert.Equal(bob.Session.User.Id, bobPlace.CreatedByUserId);
        Assert.DoesNotContain(await SearchPlacesAsync(bob.Client, "same-private-pin"), x => x.Id == alicePlace.Id);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var storedCount = await dbContext.Places.CountAsync(x => x.Name == "Place same-private-pin");
        Assert.Equal(2, storedCount);
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
        var request = await requester.SendFriendRequestAsync(receiverUserId);
        var response = await receiver.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<IReadOnlyList<PlaceSummaryDto>> FetchPlacesAsync(TestApiClient client)
    {
        var response = await client.HttpClient.GetAsync("/places");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var places = await response.Content.ReadFromJsonAsync<IReadOnlyList<PlaceSummaryDto>>(JsonDefaults.Options);
        Assert.NotNull(places);
        return places!;
    }

    private static async Task<IReadOnlyList<PlaceSummaryDto>> SearchPlacesAsync(TestApiClient client, string query)
    {
        var response = await client.HttpClient.GetAsync($"/places/search?q={Uri.EscapeDataString(query)}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var places = await response.Content.ReadFromJsonAsync<IReadOnlyList<PlaceSummaryDto>>(JsonDefaults.Options);
        Assert.NotNull(places);
        return places!;
    }

    private static async Task<IReadOnlyList<MapPlaceDto>> FetchMapPlacesAsync(TestApiClient client)
    {
        var response = await client.HttpClient.GetAsync("/map/places?north=51&south=49&east=20&west=18");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var mapPlaces = await response.Content.ReadFromJsonAsync<IReadOnlyList<MapPlaceDto>>(JsonDefaults.Options);
        Assert.NotNull(mapPlaces);
        return mapPlaces!;
    }

    private static async Task<FeedResponseDto> FetchFeedAsync(TestApiClient client)
    {
        var response = await client.HttpClient.GetAsync("/feed");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        var feed = await response.Content.ReadFromJsonAsync<FeedResponseDto>(JsonDefaults.Options);
        Assert.NotNull(feed);
        return feed!;
    }

    private static Task<HttpResponseMessage> CreatePlaceReviewRawAsync(
        TestApiClient client,
        Guid placeId,
        VisibilityLevel visibility,
        string body) =>
        client.HttpClient.PostAsJsonAsync(
            "/place-reviews",
            new CreatePlaceReviewRequest
            {
                PlaceId = placeId,
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = visibility,
                Rating = 5,
                Title = "Review",
                Body = body
            },
            JsonDefaults.Options);

    private static Task<HttpResponseMessage> CreateDishReviewRawAsync(
        TestApiClient client,
        Guid placeId,
        VisibilityLevel visibility,
        string dishName) =>
        client.HttpClient.PostAsJsonAsync(
            "/dish-reviews",
            new CreateDishReviewRequest
            {
                PlaceId = placeId,
                PlaceReviewId = null,
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = visibility,
                DishName = dishName,
                Rating = 5,
                Body = "Dish body"
            },
            JsonDefaults.Options);
}
