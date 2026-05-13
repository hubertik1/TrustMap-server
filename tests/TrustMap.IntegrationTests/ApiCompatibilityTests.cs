using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrustMap.Api.Features.Categories;
using TrustMap.Api.Features.Feed;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class ApiCompatibilityTests
{
    [Fact]
    public async Task GetAndPatchMe_Works()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var me = await client.HttpClient.GetFromJsonAsync<UserProfileDto>("/me", JsonDefaults.Options);
        Assert.NotNull(me);
        Assert.Equal(session.User.Id, me!.Id);
        Assert.True(me.IsMe);

        var updated = await client.UpdateMeAsync(
            "alice refined#1234",
            "Alice Refined",
            bio: "Updated bio");

        Assert.Equal("Alice Refined", updated.DisplayName);
        Assert.Null(updated.AvatarUrl);

        var reloaded = await client.HttpClient.GetFromJsonAsync<UserProfileDto>("/me", JsonDefaults.Options);
        Assert.NotNull(reloaded);
        Assert.Equal(updated.Handle, reloaded!.Handle);
        Assert.Equal("Alice Refined", reloaded.DisplayName);
        Assert.Equal("Updated bio", reloaded.Bio);
        Assert.Null(reloaded.AvatarUrl);
    }

    [Fact]
    public async Task PutMeAvatar_WithMultipartAvatarReplacement_StoresAvatarUrl()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.CreateJpegBytes(width: 320, height: 320));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(fileContent, "avatar", "avatar.jpg");

        var response = await client.HttpClient.PutAsync("/me/avatar", multipart);
        response.EnsureSuccessStatusCode();

        var updated = await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options);
        Assert.NotNull(updated);
        Assert.NotNull(updated.AvatarUrl);
        Assert.Equal($"/media/users/{session.User.Id:D}/avatar", updated.AvatarUrl);
    }

    [Fact]
    public async Task PlaceSearch_IsCaseInsensitive_ForNameAndAddress()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        await client.HttpClient.PostAsJsonAsync(
            "/places",
            new CreatePlaceRequest
            {
                Name = "Caffè Aurora",
                Address = "Main STREET 1",
                City = "Warsaw",
                CountryCode = "PL",
                Latitude = 52.2297,
                Longitude = 21.0122
            });

        var byNameResponse = await client.HttpClient.GetAsync("/places/search?q=aurora");
        var byAddressResponse = await client.HttpClient.GetAsync("/places/search?q=street");

        byNameResponse.EnsureSuccessStatusCode();
        byAddressResponse.EnsureSuccessStatusCode();

        var byName = await byNameResponse.Content.ReadFromJsonAsync<IReadOnlyList<PlaceSummaryDto>>(JsonDefaults.Options);
        var byAddress = await byAddressResponse.Content.ReadFromJsonAsync<IReadOnlyList<PlaceSummaryDto>>(JsonDefaults.Options);

        Assert.NotNull(byName);
        Assert.NotNull(byAddress);
        Assert.Contains(byName!, x => x.Name == "Caffè Aurora");
        Assert.Contains(byAddress!, x => x.Address == "Main STREET 1");
    }

    [Fact]
    public async Task Feed_UsesCursorWithoutSkippingOlderItems()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var olderPlaceId = await client.CreatePlaceAsync("older");
        await client.CreatePlaceReviewAsync(olderPlaceId, VisibilityLevel.Public, 4, "Older item");

        await Task.Delay(TimeSpan.FromMilliseconds(30));

        var newerPlaceId = await client.CreatePlaceAsync("newer");
        await client.CreatePlaceReviewAsync(newerPlaceId, VisibilityLevel.Public, 5, "Newer item");

        var firstPage = await client.HttpClient.GetFromJsonAsync<FeedResponseDto>("/feed?take=1", JsonDefaults.Options);
        Assert.NotNull(firstPage);
        Assert.Single(firstPage!.Items);
        Assert.Equal("Newer item", firstPage.Items[0].Body);
        Assert.NotNull(firstPage.NextCursorUtc);

        var secondPage = await client.HttpClient.GetFromJsonAsync<FeedResponseDto>(
            $"/feed?take=1&beforeUtc={Uri.EscapeDataString(firstPage.NextCursorUtc!.Value.ToString("O"))}",
            JsonDefaults.Options);

        Assert.NotNull(secondPage);
        Assert.Single(secondPage!.Items);
        Assert.Equal("Older item", secondPage.Items[0].Body);
    }

    [Fact]
    public async Task DeletingPlaceReview_CascadesPhotoMetadata()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("photo");
        var reviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 5, "Photo review");

        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.CreateJpegBytes(width: 1_600, height: 1_200));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(fileContent, "file", "photo.jpg");
        multipart.Add(new StringContent(reviewId.ToString()), "placeReviewId");

        var uploadResponse = await client.HttpClient.PostAsync("/photos/upload", multipart);
        uploadResponse.EnsureSuccessStatusCode();

        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<PhotoDto>(JsonDefaults.Options);
        Assert.NotNull(uploaded);
        Assert.Equal("image/jpeg", uploaded!.ContentType);
        Assert.True(uploaded.SizeBytes > 0);
        Assert.Equal(1_600, uploaded.Width);
        Assert.Equal(1_200, uploaded.Height);
        Assert.NotNull(uploaded.MediumUrl);
        Assert.NotNull(uploaded.ThumbnailUrl);
        Assert.NotEqual(default, uploaded.CreatedAtUtc);

        var deleteReviewResponse = await client.HttpClient.DeleteAsync($"/place-reviews/{reviewId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteReviewResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var photoExists = await dbContext.Photos.AnyAsync(x => x.Id == uploaded.Id);
        Assert.False(photoExists);

        var deletePhotoResponse = await client.HttpClient.DeleteAsync($"/photos/{uploaded.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deletePhotoResponse.StatusCode);
    }

    [Fact]
    public async Task DuplicatePlaceReview_ForSameUserAndPlace_IsRejected()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("duplicate");
        await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 4, "Original");

        var duplicateResponse = await client.HttpClient.PostAsJsonAsync(
            "/place-reviews",
            new CreatePlaceReviewRequest
            {
                PlaceId = placeId,
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Public,
                Rating = 5,
                Title = "Duplicate",
                Body = "Should fail"
            });

        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task Categories_ExposeAllDefaultOptions_AndSupportCustomCategoryCrud()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var myCategories = await client.HttpClient.GetFromJsonAsync<IReadOnlyList<CategoryDto>>("/categories/my", JsonDefaults.Options);
        Assert.NotNull(myCategories);
        Assert.True(myCategories!.Count > 1);
        var category = Assert.Single(myCategories!, x => x.Id == TestApiClient.DefaultCategoryId);
        Assert.Equal(TestApiClient.DefaultCategoryId, category.Id);
        Assert.Equal("Restaurants", category.Name);
        Assert.True(category.IsDefault);

        var friendCategories = await client.HttpClient.GetFromJsonAsync<IReadOnlyList<CategoryDto>>("/categories/friends", JsonDefaults.Options);
        Assert.NotNull(friendCategories);
        Assert.Empty(friendCategories!);

        var createCategoryResponse = await client.HttpClient.PostAsJsonAsync(
            "/categories",
            new CreateCategoryRequest
            {
                Name = "Sunset Spots"
            });
        Assert.Equal(HttpStatusCode.Created, createCategoryResponse.StatusCode);

        var createdCategory = await createCategoryResponse.Content.ReadFromJsonAsync<CategoryDto>(JsonDefaults.Options);
        Assert.NotNull(createdCategory);
        Assert.False(createdCategory!.IsDefault);
        Assert.True(createdCategory.IsOwnedByCurrentUser);
        Assert.True(createdCategory.IsVisibleForCurrentUser);

        var placeId = await client.CreatePlaceAsync("default-categories");
        var placeReviewResponse = await client.HttpClient.PostAsJsonAsync(
            "/place-reviews",
            new CreatePlaceReviewRequest
            {
                PlaceId = placeId,
                CategoryId = TestApiClient.AlternateDefaultCategoryId,
                Visibility = VisibilityLevel.Public,
                Rating = 5,
                Title = "Default category works",
                Body = "Should succeed"
            });

        placeReviewResponse.EnsureSuccessStatusCode();
        var review = (await placeReviewResponse.Content.ReadFromJsonAsync<PlaceReviewDto>(JsonDefaults.Options))!;
        Assert.Equal(TestApiClient.AlternateDefaultCategoryId, review.CategoryId);
        Assert.Equal("Bars", review.CategoryName);
    }

    [Fact]
    public async Task DishReviews_StillRequireRestaurantsCategory()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync("dish-category-check");
        await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 4, "Base review");

        var response = await client.HttpClient.PostAsJsonAsync(
            "/dish-reviews",
            new CreateDishReviewRequest
            {
                PlaceId = placeId,
                PlaceReviewId = null,
                CategoryId = TestApiClient.AlternateDefaultCategoryId,
                Visibility = VisibilityLevel.Public,
                DishName = "Burger",
                Rating = 4,
                Body = "Should fail"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

}
