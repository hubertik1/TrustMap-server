using System.Net;
using System.Net.Http.Json;
using TrustMap.Api.Features.Feed;
using TrustMap.Api.Features.Map;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Reviews;
using TrustMap.Domain.Enums;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class CustomPlaceNamingTests
{
    [Fact]
    public async Task CreatorCanRenameCustomPin_AndPlaceDetailUsesSharedDisplayName()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var created = await client.CreatePlaceSummaryAsync("custom-pin");
        Assert.Equal(created.Address, created.DisplayName);
        Assert.Equal(PlaceSourceType.CustomPin, created.SourceType);

        var renamed = await client.UpdateCustomPlaceDisplayNameAsync(created.Id, "Secret Bench");
        Assert.Equal("Secret Bench", renamed.DisplayName);
        Assert.Equal("Secret Bench", renamed.CustomDisplayName);
        Assert.Equal(PlaceSourceType.CustomPin, renamed.SourceType);

        var detail = await client.HttpClient.GetFromJsonAsync<PlaceDetailDto>($"/places/{created.Id}", JsonDefaults.Options);
        Assert.NotNull(detail);
        Assert.Equal("Secret Bench", detail!.DisplayName);
        Assert.Equal("Secret Bench", detail.CustomDisplayName);
        Assert.Equal(created.Name, detail.Name);
    }

    [Fact]
    public async Task FriendSeesReviewsAndListsUnderSharedCustomPlaceName()
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

        var created = await aliceClient.CreatePlaceSummaryAsync("shared-pin", latitude: 52.31, longitude: 21.10);
        await aliceClient.UpdateCustomPlaceDisplayNameAsync(created.Id, "Hidden Courtyard");
        await aliceClient.CreatePlaceReviewAsync(created.Id, VisibilityLevel.Friends, 5, "Quiet and perfect");

        var search = await bobClient.HttpClient.GetFromJsonAsync<IReadOnlyList<PlaceSummaryDto>>(
            "/places/search?q=hidden",
            JsonDefaults.Options);
        var detail = await bobClient.HttpClient.GetFromJsonAsync<PlaceDetailDto>($"/places/{created.Id}", JsonDefaults.Options);
        var reviews = await bobClient.HttpClient.GetFromJsonAsync<IReadOnlyList<PlaceReviewDto>>(
            $"/place-reviews?placeId={created.Id}",
            JsonDefaults.Options);
        var feed = await bobClient.HttpClient.GetFromJsonAsync<FeedResponseDto>("/feed", JsonDefaults.Options);
        var mapPlaces = await bobClient.HttpClient.GetFromJsonAsync<IReadOnlyList<MapPlaceDto>>(
            "/map/places?north=53&south=51&east=22&west=20",
            JsonDefaults.Options);

        Assert.NotNull(search);
        Assert.NotNull(detail);
        Assert.NotNull(reviews);
        Assert.NotNull(feed);
        Assert.NotNull(mapPlaces);

        Assert.Contains(search!, x => x.Id == created.Id && x.DisplayName == "Hidden Courtyard");
        Assert.Equal("Hidden Courtyard", detail!.DisplayName);
        Assert.Contains(reviews!, x => x.PlaceId == created.Id && x.Place.DisplayName == "Hidden Courtyard");
        Assert.Contains(feed!.Items, x => x.Place.Id == created.Id && x.Place.DisplayName == "Hidden Courtyard");
        Assert.Contains(mapPlaces!, x => x.PlaceId == created.Id && x.DisplayName == "Hidden Courtyard");
    }

    [Fact]
    public async Task NonCreatorCannotRenameCustomPin()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        var created = await aliceClient.CreatePlaceSummaryAsync("creator-owned");

        var response = await bobClient.HttpClient.PatchAsJsonAsync(
            $"/places/{created.Id}/custom-display-name",
            new UpdateCustomPlaceDisplayNameRequest
            {
                DisplayName = "Should Fail"
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var detail = await aliceClient.HttpClient.GetFromJsonAsync<PlaceDetailDto>($"/places/{created.Id}", JsonDefaults.Options);
        Assert.NotNull(detail);
        Assert.Equal(created.Address, detail!.DisplayName);
        Assert.Null(detail.CustomDisplayName);
    }

    [Fact]
    public async Task ProviderVenueCannotBeRenamed()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var created = await client.CreatePlaceSummaryAsync(
            "provider-venue",
            provider: "apple-maps",
            providerPlaceId: "apple-provider-venue");

        Assert.Equal(PlaceSourceType.ProviderVenue, created.SourceType);

        var response = await client.HttpClient.PatchAsJsonAsync(
            $"/places/{created.Id}/custom-display-name",
            new UpdateCustomPlaceDisplayNameRequest
            {
                DisplayName = "Renamed Venue"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var detail = await client.HttpClient.GetFromJsonAsync<PlaceDetailDto>($"/places/{created.Id}", JsonDefaults.Options);
        Assert.NotNull(detail);
        Assert.Equal(created.Name, detail!.DisplayName);
        Assert.Null(detail.CustomDisplayName);
        Assert.Equal(PlaceSourceType.ProviderVenue, detail.SourceType);
    }
}
