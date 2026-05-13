using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class PhotoUploadSecurityTests
{
    public static IEnumerable<object[]> ValidImageUploads =>
    [
        [TestImages.CreateJpegBytes(), "client-name.jpg"],
        [TestImages.CreatePngBytes(), "client-name.png"]
    ];

    [Theory]
    [MemberData(nameof(ValidImageUploads))]
    public async Task UploadingValidJpegOrPng_SucceedsWithBackendGeneratedJpeg(byte[] imageBytes, string originalFileName)
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var (client, reviewId) = await CreateAuthenticatedPlaceReviewAsync(factory, "valid-image");

        var response = await UploadPlaceReviewPhotoAsync(
            client,
            reviewId,
            imageBytes,
            originalFileName,
            "application/octet-stream");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var uploaded = (await response.Content.ReadFromJsonAsync<PhotoDto>(JsonDefaults.Options))!;

        Assert.Equal("image/jpeg", uploaded.ContentType);
        Assert.EndsWith(".jpg", uploaded.Url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(originalFileName, uploaded.Url, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(GetStoragePath(factory, uploaded.Url)));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var photo = await dbContext.Photos.SingleAsync(x => x.Id == uploaded.Id);
        Assert.EndsWith(".jpg", photo.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(originalFileName, photo.FileName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadingTextFileWithImageContentType_IsRejected()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var (client, reviewId) = await CreateAuthenticatedPlaceReviewAsync(factory, "text-file");
        var response = await UploadPlaceReviewPhotoAsync(
            client,
            reviewId,
            Encoding.UTF8.GetBytes("this is not an image"),
            "photo.jpg",
            "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(EnumerateStoredFiles(factory));
    }

    [Fact]
    public async Task UploadingHtmlFileNamedEvilWithImageContentType_IsRejected()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var (client, reviewId) = await CreateAuthenticatedPlaceReviewAsync(factory, "html-file");
        var response = await UploadPlaceReviewPhotoAsync(
            client,
            reviewId,
            Encoding.UTF8.GetBytes("<!doctype html><script>alert(1)</script>"),
            "evil.html",
            "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(EnumerateStoredFiles(factory));
    }

    [Fact]
    public async Task UploadingFileAboveSizeLimit_IsRejected()
    {
        await using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["PhotoStorage:MaxUploadBytes"] = "128"
            });
        await factory.InitializeAsync();

        var (client, reviewId) = await CreateAuthenticatedPlaceReviewAsync(factory, "too-large");
        var response = await UploadPlaceReviewPhotoAsync(
            client,
            reviewId,
            TestImages.CreateJpegBytes(),
            "photo.jpg",
            "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(EnumerateStoredFiles(factory));
    }

    [Fact]
    public async Task UploadingCorruptedImage_ReturnsBadRequest()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var (client, reviewId) = await CreateAuthenticatedPlaceReviewAsync(factory, "corrupt-image");
        var response = await UploadPlaceReviewPhotoAsync(
            client,
            reviewId,
            [0xFF, 0xD8, 0xFF, 0xE0, 0x00],
            "corrupt.jpg",
            "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task FailedUpload_DoesNotLeavePartialFiles()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var (client, reviewId) = await CreateAuthenticatedPlaceReviewAsync(factory, "partial-files");
        var before = EnumerateStoredFiles(factory).Count;

        var response = await UploadPlaceReviewPhotoAsync(
            client,
            reviewId,
            Encoding.UTF8.GetBytes("not an image"),
            "photo.jpg",
            "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, EnumerateStoredFiles(factory).Count);
    }

    [Fact]
    public async Task SavedImage_DoesNotContainExifOrGpsMetadata()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var (client, reviewId) = await CreateAuthenticatedPlaceReviewAsync(factory, "metadata");
        var response = await UploadPlaceReviewPhotoAsync(
            client,
            reviewId,
            TestImages.CreateJpegWithMetadataBytes(),
            "private-location.jpg",
            "image/jpeg");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var uploaded = (await response.Content.ReadFromJsonAsync<PhotoDto>(JsonDefaults.Options))!;

        using var saved = Image.Load(GetStoragePath(factory, uploaded.Url));
        Assert.Null(saved.Metadata.ExifProfile);
        Assert.Null(saved.Metadata.XmpProfile);
        Assert.Null(saved.Metadata.IptcProfile);
        Assert.All(saved.Frames, frame => Assert.Null(frame.Metadata.ExifProfile));
    }

    [Fact]
    public async Task PatchMe_DoesNotAllowSettingAvatarUrl()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("avatar-json", "Avatar Json");
        client.Authenticate(session.AccessToken);

        using var content = new StringContent(
            """
            {
              "handle": "avatar-json#1234",
              "displayName": "Avatar Json Updated",
              "bio": "updated",
              "avatarUrl": "/uploads/avatars/client-controlled.jpg"
            }
            """,
            Encoding.UTF8,
            "application/json");

        var response = await client.HttpClient.PatchAsync("/me", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = (await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options))!;
        Assert.Equal("Avatar Json Updated", updated.DisplayName);
        Assert.Null(updated.AvatarUrl);
    }

    [Fact]
    public async Task AvatarDeletion_DoesNotAllowPathTraversal()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("avatar-traversal", "Avatar Traversal");
        client.Authenticate(session.AccessToken);

        var outsidePath = Path.GetFullPath(Path.Combine(factory.PhotoRootPath, "../../App_Data/trustmap.db"));
        Directory.CreateDirectory(Path.GetDirectoryName(outsidePath)!);
        await File.WriteAllTextAsync(outsidePath, "do not delete");

        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
                var user = await dbContext.Users.SingleAsync(x => x.Id == session.User.Id);
                user.UpdateProfile(user.Handle, user.DisplayName, user.Bio, "/uploads/../../App_Data/trustmap.db");
                await dbContext.SaveChangesAsync();
            }

            var updated = await client.RemoveAvatarAsync();

            Assert.Null(updated.AvatarUrl);
            Assert.True(File.Exists(outsidePath));
            Assert.Equal("do not delete", await File.ReadAllTextAsync(outsidePath));
        }
        finally
        {
            if (File.Exists(outsidePath))
            {
                File.Delete(outsidePath);
            }
        }
    }

    [Fact]
    public async Task LocalPhotoStorageDeleteAsync_DoesNotDeleteOutsideStorageRoot()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), "trustmap-storage-tests", Guid.CreateVersion7().ToString("N"));
        var outsidePath = Path.GetFullPath(Path.Combine(rootPath, "../outside.txt"));
        Directory.CreateDirectory(rootPath);
        await File.WriteAllTextAsync(outsidePath, "outside");

        try
        {
            var storage = new LocalPhotoStorage(
                Options.Create(new PhotoStorageOptions
                {
                    RootPath = rootPath,
                    RequestPath = "/uploads"
                }),
                NullLogger<LocalPhotoStorage>.Instance);

            var exception = await Assert.ThrowsAsync<PhotoStorageException>(
                () => storage.DeleteAsync("../outside.txt", CancellationToken.None));

            Assert.Equal("photo_invalid_storage_key", exception.ErrorCode);
            Assert.True(File.Exists(outsidePath));
            Assert.Equal("outside", await File.ReadAllTextAsync(outsidePath));
        }
        finally
        {
            if (File.Exists(outsidePath))
            {
                File.Delete(outsidePath);
            }

            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static async Task<(TestApiClient Client, Guid ReviewId)> CreateAuthenticatedPlaceReviewAsync(
        TrustMapWebApplicationFactory factory,
        string suffix)
    {
        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync($"alice-{suffix}", "Alice");
        client.Authenticate(session.AccessToken);

        var placeId = await client.CreatePlaceAsync($"photo-security-{suffix}");
        var reviewId = await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 5, "Secure upload");
        return (client, reviewId);
    }

    private static async Task<HttpResponseMessage> UploadPlaceReviewPhotoAsync(
        TestApiClient client,
        Guid reviewId,
        byte[] bytes,
        string fileName,
        string contentType)
    {
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, "file", fileName);
        multipart.Add(new StringContent(reviewId.ToString()), "placeReviewId");

        return await client.HttpClient.PostAsync("/photos/upload", multipart);
    }

    private static string GetStoragePath(TrustMapWebApplicationFactory factory, string publicUrl)
    {
        Assert.StartsWith("/uploads/", publicUrl, StringComparison.Ordinal);
        var storageKey = publicUrl["/uploads/".Length..];
        return Path.Combine(factory.PhotoRootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
    }

    private static IReadOnlyList<string> EnumerateStoredFiles(TrustMapWebApplicationFactory factory)
    {
        if (!Directory.Exists(factory.PhotoRootPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(factory.PhotoRootPath, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal))
            .ToArray();
    }
}
