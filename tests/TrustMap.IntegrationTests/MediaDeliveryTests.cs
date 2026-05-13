using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class MediaDeliveryTests
{
    [Fact]
    public async Task AnonymousRequestToPrivateReviewPhoto_IsRejected()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-private-anon-alice", "Alice");
        var upload = await CreatePlaceReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Private, "private-anon");

        var response = await factory.CreateClient().GetAsync(upload.Dto.Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task NonOwnerCannotFetchPrivateReviewPhoto()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-private-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "media-private-bob", "Bob");
        var upload = await CreatePlaceReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Private, "private");

        var response = await bob.Client.HttpClient.GetAsync(upload.Dto.Url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCanFetchPrivateReviewPhoto()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-owner-alice", "Alice");
        var upload = await CreatePlaceReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Private, "owner");

        var response = await alice.Client.HttpClient.GetAsync(upload.Dto.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("private", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 0);
    }

    [Fact]
    public async Task FriendCanFetchFriendsOnlyReviewPhoto()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-friend-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "media-friend-bob", "Bob");
        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);
        var upload = await CreatePlaceReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Friends, "friend");

        var response = await bob.Client.HttpClient.GetAsync(upload.Dto.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task NonFriendCannotFetchFriendsOnlyReviewPhoto()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-nonfriend-alice", "Alice");
        var charlie = await CreateAuthenticatedClientAsync(factory, "media-nonfriend-charlie", "Charlie");
        var upload = await CreatePlaceReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Friends, "nonfriend");

        var response = await charlie.Client.HttpClient.GetAsync(upload.Dto.Url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PublicReviewPhotoRequiresAuthenticationAndAllowsAuthenticatedViewer()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-public-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "media-public-bob", "Bob");
        var upload = await CreatePlaceReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Public, "public");

        var anonymousResponse = await factory.CreateClient().GetAsync(upload.Dto.Url);
        var authenticatedResponse = await bob.Client.HttpClient.GetAsync(upload.Dto.Url);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authenticatedResponse.StatusCode);
    }

    [Fact]
    public async Task RawUploadsUrlDoesNotExposeReviewPhoto()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-raw-alice", "Alice");
        var upload = await CreatePlaceReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Public, "raw");

        var response = await alice.Client.HttpClient.GetAsync(upload.RawPublicUrl);

        Assert.StartsWith("/uploads/", upload.RawPublicUrl, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PhotoDtosExposeProtectedMediaUrlsWithoutStorageKeys()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-dto-alice", "Alice");
        var upload = await CreatePlaceReviewPhotoAsync(
            factory,
            alice.Client,
            VisibilityLevel.Public,
            "dto",
            width: 1_600,
            height: 1_200);

        var review = await alice.Client.HttpClient.GetFromJsonAsync<PlaceReviewDto>(
            $"/place-reviews/{upload.ReviewId}",
            JsonDefaults.Options);

        Assert.NotNull(review);
        var dto = Assert.Single(review!.Photos);
        Assert.Equal($"/media/photos/{upload.Dto.Id:D}", dto.Url);
        Assert.Equal($"/media/photos/{upload.Dto.Id:D}/variants/medium", dto.MediumUrl);
        Assert.Equal($"/media/photos/{upload.Dto.Id:D}/variants/thumbnail", dto.ThumbnailUrl);
        Assert.DoesNotContain("/uploads/", dto.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(upload.StorageKey, dto.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(upload.MediumStorageKey!, dto.MediumUrl!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(upload.ThumbnailStorageKey!, dto.ThumbnailUrl!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VariantEndpointRejectsInvalidAndTraversalVariantNames()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-variant-alice", "Alice");
        var upload = await CreatePlaceReviewPhotoAsync(
            factory,
            alice.Client,
            VisibilityLevel.Public,
            "variant",
            width: 1_600,
            height: 1_200);

        var thumbnailResponse = await alice.Client.HttpClient.GetAsync(upload.Dto.ThumbnailUrl);
        var invalidResponse = await alice.Client.HttpClient.GetAsync($"/media/photos/{upload.Dto.Id:D}/variants/full");
        var traversalResponse = await alice.Client.HttpClient.GetAsync($"/media/photos/{upload.Dto.Id:D}/variants/..%5csecret");

        Assert.Equal(HttpStatusCode.OK, thumbnailResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalidResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, traversalResponse.StatusCode);
    }

    [Fact]
    public async Task MissingStoredReviewPhotoReturnsNotFound()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-missing-alice", "Alice");
        var upload = await CreatePlaceReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Private, "missing");
        File.Delete(GetStoragePath(factory, upload.StorageKey));

        var response = await alice.Client.HttpClient.GetAsync(upload.Dto.Url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UnauthorizedMediaResponseDoesNotLeakStorageDetails()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-leak-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "media-leak-bob", "Bob");
        var upload = await CreatePlaceReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Private, "leak");

        var response = await bob.Client.HttpClient.GetAsync(upload.Dto.Url);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(upload.StorageKey, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(factory.PhotoRootPath, body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/uploads/", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DishReviewPhotoUsesDishReviewVisibility()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-dish-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "media-dish-bob", "Bob");
        var upload = await CreateDishReviewPhotoAsync(factory, alice.Client, VisibilityLevel.Private, "dish");

        var ownerResponse = await alice.Client.HttpClient.GetAsync(upload.Dto.Url);
        var otherResponse = await bob.Client.HttpClient.GetAsync(upload.Dto.Url);

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
    }

    [Fact]
    public async Task AvatarUrlUsesAuthenticatedMediaEndpointAndRawUploadsAreNotPublic()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-avatar-alice", "Alice");
        var updated = await alice.Client.ReplaceAvatarAsync(TestImages.CreateJpegBytes(width: 320, height: 320));
        var rawAvatarUrl = await GetStoredAvatarUrlAsync(factory, alice.Session.User.Id);

        var mediaResponse = await alice.Client.HttpClient.GetAsync(updated.AvatarUrl);
        var rawResponse = await alice.Client.HttpClient.GetAsync(rawAvatarUrl);

        Assert.Equal($"/media/users/{alice.Session.User.Id:D}/avatar", updated.AvatarUrl);
        Assert.Equal(HttpStatusCode.OK, mediaResponse.StatusCode);
        Assert.Equal("image/jpeg", mediaResponse.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("/uploads/", rawAvatarUrl, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, rawResponse.StatusCode);
    }

    [Fact]
    public async Task AvatarEndpointRespectsProfilePictureVisibility()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "media-avatar-private-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "media-avatar-private-bob", "Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "media-avatar-private-charlie", "Charlie");
        var updated = await alice.Client.ReplaceAvatarAsync(TestImages.CreateJpegBytes(width: 320, height: 320));
        await alice.Client.UpdateMePrivacyAsync(profilePictureVisibility: VisibilityLevel.Friends);
        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);

        var friendResponse = await bob.Client.HttpClient.GetAsync(updated.AvatarUrl);
        var nonFriendResponse = await charlie.Client.HttpClient.GetAsync(updated.AvatarUrl);

        Assert.Equal(HttpStatusCode.OK, friendResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nonFriendResponse.StatusCode);
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

    private static async Task<UploadedPhoto> CreatePlaceReviewPhotoAsync(
        TrustMapWebApplicationFactory factory,
        TestApiClient client,
        VisibilityLevel visibility,
        string suffix,
        int width = 640,
        int height = 480)
    {
        var placeId = await client.CreatePlaceAsync($"media-place-{suffix}");
        var reviewId = await client.CreatePlaceReviewAsync(placeId, visibility, 5, $"Photo {suffix}");
        var upload = await UploadPlaceReviewPhotoAsync(client, reviewId, width, height);
        return await LoadUploadedPhotoAsync(factory, upload, reviewId);
    }

    private static async Task<UploadedPhoto> CreateDishReviewPhotoAsync(
        TrustMapWebApplicationFactory factory,
        TestApiClient client,
        VisibilityLevel visibility,
        string suffix)
    {
        var placeId = await client.CreatePlaceAsync($"media-dish-place-{suffix}");
        await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 5, $"Parent {suffix}");
        var dishReviewId = await client.CreateDishReviewAsync(placeId, null, visibility, $"Dish {suffix}", 5);
        var upload = await UploadDishReviewPhotoAsync(client, dishReviewId);
        return await LoadUploadedPhotoAsync(factory, upload, dishReviewId);
    }

    private static async Task<PhotoDto> UploadPlaceReviewPhotoAsync(
        TestApiClient client,
        Guid reviewId,
        int width,
        int height)
    {
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(TestImages.CreateJpegBytes(width: width, height: height));
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
        var fileContent = new ByteArrayContent(TestImages.CreateJpegBytes(width: 640, height: 480));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(fileContent, "file", "photo.jpg");
        multipart.Add(new StringContent(reviewId.ToString()), "dishReviewId");

        var response = await client.HttpClient.PostAsync("/photos/upload", multipart);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PhotoDto>(JsonDefaults.Options))!;
    }

    private static async Task<UploadedPhoto> LoadUploadedPhotoAsync(
        TrustMapWebApplicationFactory factory,
        PhotoDto dto,
        Guid reviewId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var photo = await dbContext.Photos.AsNoTracking().SingleAsync(x => x.Id == dto.Id);
        return new UploadedPhoto(
            dto,
            reviewId,
            photo.PublicUrl,
            photo.StorageKey,
            photo.MediumStorageKey,
            photo.ThumbnailStorageKey);
    }

    private static async Task<string> GetStoredAvatarUrlAsync(TrustMapWebApplicationFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        return (await dbContext.Users.AsNoTracking().SingleAsync(x => x.Id == userId)).AvatarUrl!;
    }

    private static string GetStoragePath(TrustMapWebApplicationFactory factory, string storageKey) =>
        Path.Combine(factory.PhotoRootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));

    private sealed record UploadedPhoto(
        PhotoDto Dto,
        Guid ReviewId,
        string RawPublicUrl,
        string StorageKey,
        string? MediumStorageKey,
        string? ThumbnailStorageKey);
}
