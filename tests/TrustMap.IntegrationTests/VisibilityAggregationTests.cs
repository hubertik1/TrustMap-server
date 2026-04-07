using System.Net.Http.Json;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Feed;
using TrustMap.Api.Features.Friends;
using TrustMap.Api.Features.Map;
using TrustMap.Domain.Enums;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class VisibilityAggregationTests
{
    [Fact]
    public async Task Feed_RespectsSocialScopeAndVisibility()
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

        var friendRequest = await aliceClient.SendFriendRequestAsync(bob.User.Id);
        await bobClient.HttpClient.PostAsync($"/friend-requests/{friendRequest.Id}/accept", content: null);

        var alicePlace = await aliceClient.CreatePlaceAsync("feed-alice", 52.20, 21.00);
        var bobPlace = await bobClient.CreatePlaceAsync("feed-bob", 52.21, 21.01);
        var charliePlace = await charlieClient.CreatePlaceAsync("feed-charlie", 52.22, 21.02);

        await aliceClient.CreatePlaceReviewAsync(alicePlace, VisibilityLevel.Private, 4, "Alice private");
        await bobClient.CreatePlaceReviewAsync(bobPlace, VisibilityLevel.Friends, 5, "Bob friends");
        await charlieClient.CreatePlaceReviewAsync(charliePlace, VisibilityLevel.Public, 3, "Charlie public");

        var feedResponse = await aliceClient.HttpClient.GetAsync("/feed");
        Assert.True(feedResponse.IsSuccessStatusCode, await feedResponse.Content.ReadAsStringAsync());
        var feed = await feedResponse.Content.ReadFromJsonAsync<FeedResponseDto>(JsonDefaults.Options);

        Assert.NotNull(feed);
        Assert.Equal(2, feed!.Items.Count);
        Assert.Contains(feed.Items, x => x.Body == "Alice private");
        Assert.Contains(feed.Items, x => x.Body == "Bob friends");
        Assert.DoesNotContain(feed.Items, x => x.Body == "Charlie public");
    }

    [Fact]
    public async Task Map_RespectsSocialScopeAndVisibility()
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

        var friendRequest = await aliceClient.SendFriendRequestAsync(bob.User.Id);
        await bobClient.HttpClient.PostAsync($"/friend-requests/{friendRequest.Id}/accept", content: null);

        var alicePlace = await aliceClient.CreatePlaceAsync("map-alice", 50.0001, 19.0001);
        var bobPlace = await bobClient.CreatePlaceAsync("map-bob", 50.0002, 19.0002);
        var charliePlace = await charlieClient.CreatePlaceAsync("map-charlie", 50.0003, 19.0003);

        await aliceClient.CreatePlaceReviewAsync(alicePlace, VisibilityLevel.Private, 4, "Alice map");
        await bobClient.CreatePlaceReviewAsync(bobPlace, VisibilityLevel.Friends, 5, "Bob map");
        await charlieClient.CreatePlaceReviewAsync(charliePlace, VisibilityLevel.Public, 3, "Charlie map");

        var mapResponse = await aliceClient.HttpClient.GetAsync("/map/places?north=51&south=49&east=20&west=18");
        Assert.True(mapResponse.IsSuccessStatusCode, await mapResponse.Content.ReadAsStringAsync());
        var mapPlaces = await mapResponse.Content.ReadFromJsonAsync<IReadOnlyList<MapPlaceDto>>(JsonDefaults.Options);

        Assert.NotNull(mapPlaces);
        Assert.Equal(2, mapPlaces!.Count);
        Assert.Contains(mapPlaces, x => x.Name.Contains("map-alice", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(mapPlaces, x => x.Name.Contains("map-bob", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(mapPlaces, x => x.Name.Contains("map-charlie", StringComparison.OrdinalIgnoreCase));
    }
}
