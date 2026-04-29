using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Map;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class MapRecentContributorsTests
{
    [Fact]
    public async Task Map_ReturnsRecentContributors_DedupedSortedCapped_AndPreservesExistingFields()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "recent-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "recent-bob", "Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "recent-charlie", "Charlie");
        var dana = await CreateAuthenticatedClientAsync(factory, "recent-dana", "Dana");
        var erin = await CreateAuthenticatedClientAsync(factory, "recent-erin", "Erin");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);
        await MakeFriendsAsync(alice.Client, charlie.Client, charlie.Session.User.Id);
        await MakeFriendsAsync(alice.Client, dana.Client, dana.Session.User.Id);
        await MakeFriendsAsync(alice.Client, erin.Client, erin.Session.User.Id);

        var placeId = await alice.Client.CreatePlaceAsync("recent-contributors", 50.0004, 19.0004);

        var aliceReviewId = await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 4, "Alice place");
        var bobReviewId = await bob.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 5, "Bob place");
        var charlieReviewId = await charlie.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 3, "Charlie place");
        var danaReviewId = await dana.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 2, "Dana place");
        var erinReviewId = await erin.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 1, "Erin place");
        var bobDishReviewId = await bob.Client.CreateDishReviewAsync(placeId, bobReviewId, VisibilityLevel.Friends, "Pierogi", 5);

        await SetPlaceReviewUpdatedAtAsync(factory, aliceReviewId, UtcDate(2026, 4, 1));
        await SetPlaceReviewUpdatedAtAsync(factory, bobReviewId, UtcDate(2026, 4, 2));
        await SetPlaceReviewUpdatedAtAsync(factory, charlieReviewId, UtcDate(2026, 4, 5));
        await SetPlaceReviewUpdatedAtAsync(factory, danaReviewId, UtcDate(2026, 4, 4));
        await SetPlaceReviewUpdatedAtAsync(factory, erinReviewId, UtcDate(2026, 4, 3));
        await SetDishReviewUpdatedAtAsync(factory, bobDishReviewId, UtcDate(2026, 4, 6));

        var mapPlaces = await FetchMapPlacesAsync(alice.Client);
        var mapPlace = Assert.Single(mapPlaces, x => x.PlaceId == placeId);

        Assert.Equal(placeId, mapPlace.PlaceId);
        Assert.Contains("recent-contributors", mapPlace.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recent-contributors", mapPlace.DisplayName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(50.0004, mapPlace.Latitude);
        Assert.Equal(19.0004, mapPlace.Longitude);
        Assert.Equal(5, mapPlace.VisiblePlaceReviewCount);
        Assert.Equal(1, mapPlace.VisibleDishReviewCount);
        Assert.Equal(3.0, mapPlace.AveragePlaceRating);
        Assert.Equal(5, mapPlace.ContributorCount);
        Assert.Equal(UtcDate(2026, 4, 6), mapPlace.LatestActivityAtUtc);
        Assert.Contains("Restaurants", mapPlace.CategoryNames);
        Assert.True(mapPlace.IsReviewedByCurrentUser);

        Assert.Equal(3, mapPlace.RecentContributors.Count);
        Assert.Equal(
            new[] { bob.Session.User.Id, charlie.Session.User.Id, dana.Session.User.Id },
            mapPlace.RecentContributors.Select(x => x.Id).ToArray());
        Assert.Equal(
            new[] { "Bob", "Charlie", "Dana" },
            mapPlace.RecentContributors.Select(x => x.DisplayName).ToArray());
    }

    [Fact]
    public async Task Map_RecentContributors_RespectsVisibilityAndDoesNotLeakHiddenAuthors()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "visibility-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "visibility-bob", "Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "visibility-charlie", "Charlie");

        await charlie.Client.UpdateMeAsync(
            "visibility-charlie",
            "Hidden Charlie",
            avatarUrl: "/uploads/avatars/hidden-charlie.jpg");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var placeId = await alice.Client.CreatePlaceAsync("visibility-contributors", 50.0005, 19.0005);

        await bob.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 5, "Bob visible");
        await charlie.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 1, "Charlie hidden");

        var (content, mapPlaces) = await FetchMapPlacesWithContentAsync(alice.Client);
        var mapPlace = Assert.Single(mapPlaces, x => x.PlaceId == placeId);

        var contributor = Assert.Single(mapPlace.RecentContributors);
        Assert.Equal(bob.Session.User.Id, contributor.Id);
        Assert.Equal("Bob", contributor.DisplayName);
        Assert.Equal(1, mapPlace.VisiblePlaceReviewCount);
        Assert.Equal(0, mapPlace.VisibleDishReviewCount);
        Assert.Equal(5.0, mapPlace.AveragePlaceRating);
        Assert.Equal(1, mapPlace.ContributorCount);
        Assert.DoesNotContain(mapPlace.RecentContributors, x => x.Id == charlie.Session.User.Id);
        Assert.DoesNotContain("Hidden Charlie", content, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-charlie.jpg", content, StringComparison.Ordinal);
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
        return (content, mapPlaces!);
    }

    private static async Task SetPlaceReviewUpdatedAtAsync(
        TrustMapWebApplicationFactory factory,
        Guid reviewId,
        DateTime updatedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        await dbContext.PlaceReviews
            .Where(x => x.Id == reviewId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt, updatedAtUtc));
    }

    private static async Task SetDishReviewUpdatedAtAsync(
        TrustMapWebApplicationFactory factory,
        Guid reviewId,
        DateTime updatedAtUtc)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        await dbContext.DishReviews
            .Where(x => x.Id == reviewId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UpdatedAt, updatedAtUtc));
    }

    private static DateTime UtcDate(int year, int month, int day) =>
        new(year, month, day, 12, 0, 0, DateTimeKind.Utc);
}
