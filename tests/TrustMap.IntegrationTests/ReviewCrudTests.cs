using System.Net;
using System.Net.Http.Json;
using TrustMap.Api.Features.Reviews;
using TrustMap.Domain.Enums;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class ReviewCrudTests
{
    [Fact]
    public async Task CreateEditDeletePlaceReview_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("alpha");
        var reviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Nice");

        var updateResponse = await client.HttpClient.PatchAsJsonAsync(
            $"/place-reviews/{reviewId}",
            new UpdatePlaceReviewRequest
            {
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Public,
                Rating = 5,
                Title = "Updated",
                Body = "Excellent"
            });

        updateResponse.EnsureSuccessStatusCode();
        var updated = (await updateResponse.Content.ReadFromJsonAsync<PlaceReviewDto>(JsonDefaults.Options))!;
        Assert.Equal(5, updated.Rating);
        Assert.Equal(VisibilityLevel.Public, updated.Visibility);

        var deleteResponse = await client.HttpClient.DeleteAsync($"/place-reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.HttpClient.GetAsync($"/place-reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task CreateEditDeleteDishReview_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("beta");
        var placeReviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Good");
        var dishReviewId = await client.CreateDishReviewAsync(placeId, placeReviewId, VisibilityLevel.Friends, "Pierogi", 5);

        var updateResponse = await client.HttpClient.PatchAsJsonAsync(
            $"/dish-reviews/{dishReviewId}",
            new UpdateDishReviewRequest
            {
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Public,
                DishName = "Pierogi Ruskie",
                Rating = 4,
                Body = "Still great",
                PriceAmount = 29.90m,
                CurrencyCode = "PLN"
            });

        updateResponse.EnsureSuccessStatusCode();
        var updated = (await updateResponse.Content.ReadFromJsonAsync<DishReviewDto>(JsonDefaults.Options))!;
        Assert.Equal("Pierogi Ruskie", updated.DishName);
        Assert.Equal(VisibilityLevel.Public, updated.Visibility);

        var deleteResponse = await client.HttpClient.DeleteAsync($"/dish-reviews/{dishReviewId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.HttpClient.GetAsync($"/dish-reviews/{dishReviewId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
}
