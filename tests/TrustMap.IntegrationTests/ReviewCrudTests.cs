using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrustMap.Api.Features.Reviews;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
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
            },
            JsonDefaults.Options);

        updateResponse.EnsureSuccessStatusCode();
        var updated = (await updateResponse.Content.ReadFromJsonAsync<PlaceReviewDto>(JsonDefaults.Options))!;
        Assert.Equal(5, updated.Rating);
        Assert.Equal(VisibilityLevel.Friends, updated.Visibility);

        var deleteResponse = await client.HttpClient.DeleteAsync($"/place-reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.HttpClient.GetAsync($"/place-reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task UpdatingPlaceReview_WithIdenticalValues_PreservesUpdatedAt()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("place-no-op");
        var reviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Nice");
        var originalUpdatedAt = UtcDate(2026, 1, 2);
        await SetPlaceReviewUpdatedAtAsync(factory, reviewId, originalUpdatedAt);

        var updateResponse = await client.HttpClient.PatchAsJsonAsync(
            $"/place-reviews/{reviewId}",
            new UpdatePlaceReviewRequest
            {
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Friends,
                Rating = 4,
                Title = " Review ",
                Body = " Nice "
            },
            JsonDefaults.Options);

        updateResponse.EnsureSuccessStatusCode();
        var updated = (await updateResponse.Content.ReadFromJsonAsync<PlaceReviewDto>(JsonDefaults.Options))!;
        Assert.Equal(originalUpdatedAt, updated.UpdatedAtUtc);

        var fetched = await client.HttpClient.GetFromJsonAsync<PlaceReviewDto>(
            $"/place-reviews/{reviewId}",
            JsonDefaults.Options);
        Assert.NotNull(fetched);
        Assert.Equal(originalUpdatedAt, fetched!.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdatingPlaceReview_WithRealChange_UpdatesUpdatedAt()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("place-real-update");
        var reviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Nice");
        var originalUpdatedAt = UtcDate(2026, 1, 2);
        await SetPlaceReviewUpdatedAtAsync(factory, reviewId, originalUpdatedAt);

        var updateResponse = await client.HttpClient.PatchAsJsonAsync(
            $"/place-reviews/{reviewId}",
            new UpdatePlaceReviewRequest
            {
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Friends,
                Rating = 5,
                Title = "Review",
                Body = "Excellent"
            },
            JsonDefaults.Options);

        updateResponse.EnsureSuccessStatusCode();
        var updated = (await updateResponse.Content.ReadFromJsonAsync<PlaceReviewDto>(JsonDefaults.Options))!;
        Assert.True(updated.UpdatedAtUtc > originalUpdatedAt);
    }

    [Fact]
    public async Task UploadingPlaceReviewPhoto_UpdatesUpdatedAt()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("place-photo-update");
        var reviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Nice");
        var originalUpdatedAt = UtcDate(2026, 1, 2);
        await SetPlaceReviewUpdatedAtAsync(factory, reviewId, originalUpdatedAt);

        await UploadPlaceReviewPhotoAsync(client, reviewId);

        var fetched = await client.HttpClient.GetFromJsonAsync<PlaceReviewDto>(
            $"/place-reviews/{reviewId}",
            JsonDefaults.Options);
        Assert.NotNull(fetched);
        Assert.True(fetched!.UpdatedAtUtc > originalUpdatedAt);
        Assert.Single(fetched.Photos);
    }

    [Fact]
    public async Task CreatePlaceReview_WithNonRestaurantDefaultCategory_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("bars");

        var response = await client.HttpClient.PostAsJsonAsync(
            "/place-reviews",
            new CreatePlaceReviewRequest
            {
                PlaceId = placeId,
                CategoryId = TestApiClient.AlternateDefaultCategoryId,
                Visibility = VisibilityLevel.Public,
                Rating = 4,
                Title = "Solid",
                Body = "Works for bars"
            },
            JsonDefaults.Options);

        response.EnsureSuccessStatusCode();
        var review = (await response.Content.ReadFromJsonAsync<PlaceReviewDto>(JsonDefaults.Options))!;
        Assert.Equal(TestApiClient.AlternateDefaultCategoryId, review.CategoryId);
        Assert.Equal("Bars", review.CategoryName);
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
            },
            JsonDefaults.Options);

        updateResponse.EnsureSuccessStatusCode();
        var updated = (await updateResponse.Content.ReadFromJsonAsync<DishReviewDto>(JsonDefaults.Options))!;
        Assert.Equal("Pierogi Ruskie", updated.DishName);
        Assert.Equal(VisibilityLevel.Friends, updated.Visibility);

        var deleteResponse = await client.HttpClient.DeleteAsync($"/dish-reviews/{dishReviewId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await client.HttpClient.GetAsync($"/dish-reviews/{dishReviewId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task UpdatingDishReview_WithIdenticalValues_PreservesUpdatedAt()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("dish-no-op");
        var placeReviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Good");
        var createResponse = await client.HttpClient.PostAsJsonAsync(
            "/dish-reviews",
            new CreateDishReviewRequest
            {
                PlaceId = placeId,
                CategoryId = TestApiClient.DefaultCategoryId,
                PlaceReviewId = placeReviewId,
                Visibility = VisibilityLevel.Friends,
                DishName = "Pierogi",
                Rating = 5,
                Body = "Tasty",
                PriceAmount = 29.90m,
                CurrencyCode = "pln"
            },
            JsonDefaults.Options);
        createResponse.EnsureSuccessStatusCode();
        var created = (await createResponse.Content.ReadFromJsonAsync<DishReviewDto>(JsonDefaults.Options))!;

        var originalUpdatedAt = UtcDate(2026, 1, 2);
        await SetDishReviewUpdatedAtAsync(factory, created.Id, originalUpdatedAt);

        var updateResponse = await client.HttpClient.PatchAsJsonAsync(
            $"/dish-reviews/{created.Id}",
            new UpdateDishReviewRequest
            {
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Friends,
                DishName = " Pierogi ",
                Rating = 5,
                Body = " Tasty ",
                PriceAmount = 29.900m,
                CurrencyCode = "pln"
            },
            JsonDefaults.Options);

        updateResponse.EnsureSuccessStatusCode();
        var updated = (await updateResponse.Content.ReadFromJsonAsync<DishReviewDto>(JsonDefaults.Options))!;
        Assert.Equal(originalUpdatedAt, updated.UpdatedAtUtc);

        var fetched = await client.HttpClient.GetFromJsonAsync<DishReviewDto>(
            $"/dish-reviews/{created.Id}",
            JsonDefaults.Options);
        Assert.NotNull(fetched);
        Assert.Equal(originalUpdatedAt, fetched!.UpdatedAtUtc);
    }

    [Fact]
    public async Task UpdatingDishReview_WithRealChange_UpdatesUpdatedAt()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("dish-real-update");
        var placeReviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Good");
        var dishReviewId = await client.CreateDishReviewAsync(placeId, placeReviewId, VisibilityLevel.Friends, "Pierogi", 5);
        var originalUpdatedAt = UtcDate(2026, 1, 2);
        await SetDishReviewUpdatedAtAsync(factory, dishReviewId, originalUpdatedAt);

        var updateResponse = await client.HttpClient.PatchAsJsonAsync(
            $"/dish-reviews/{dishReviewId}",
            new UpdateDishReviewRequest
            {
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Friends,
                DishName = "Pierogi Ruskie",
                Rating = 5,
                Body = "Body for Pierogi",
                PriceAmount = null,
                CurrencyCode = null
            },
            JsonDefaults.Options);

        updateResponse.EnsureSuccessStatusCode();
        var updated = (await updateResponse.Content.ReadFromJsonAsync<DishReviewDto>(JsonDefaults.Options))!;
        Assert.True(updated.UpdatedAtUtc > originalUpdatedAt);
    }

    [Fact]
    public async Task UploadingDishReviewPhoto_UpdatesUpdatedAt()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("dish-photo-update");
        var placeReviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 4, "Good");
        var dishReviewId = await client.CreateDishReviewAsync(placeId, placeReviewId, VisibilityLevel.Friends, "Pierogi", 5);
        var originalUpdatedAt = UtcDate(2026, 1, 2);
        await SetDishReviewUpdatedAtAsync(factory, dishReviewId, originalUpdatedAt);

        await UploadDishReviewPhotoAsync(client, dishReviewId);

        var fetched = await client.HttpClient.GetFromJsonAsync<DishReviewDto>(
            $"/dish-reviews/{dishReviewId}",
            JsonDefaults.Options);
        Assert.NotNull(fetched);
        Assert.True(fetched!.UpdatedAtUtc > originalUpdatedAt);
        Assert.Single(fetched.Photos);
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

    private static async Task<PhotoDto> UploadPlaceReviewPhotoAsync(TestApiClient client, Guid reviewId)
    {
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.CreateJpegBytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(fileContent, "file", "photo.jpg");
        multipart.Add(new StringContent(reviewId.ToString()), "placeReviewId");

        var response = await client.HttpClient.PostAsync("/photos/upload", multipart);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PhotoDto>(JsonDefaults.Options))!;
    }

    private static async Task<PhotoDto> UploadDishReviewPhotoAsync(TestApiClient client, Guid reviewId)
    {
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.CreateJpegBytes());
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(fileContent, "file", "photo.jpg");
        multipart.Add(new StringContent(reviewId.ToString()), "dishReviewId");

        var response = await client.HttpClient.PostAsync("/photos/upload", multipart);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PhotoDto>(JsonDefaults.Options))!;
    }

    private static DateTime UtcDate(int year, int month, int day) =>
        new(year, month, day, 12, 0, 0, DateTimeKind.Utc);
}
