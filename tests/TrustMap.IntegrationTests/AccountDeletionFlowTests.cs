using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Reviews;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class AccountDeletionFlowTests
{
    [Fact]
    public async Task DeleteMe_AnonymousUser_ReturnsUnauthorized()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var response = await factory.CreateClient().DeleteAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMe_SoftDeletesUserAndRemovesPrivateData()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "delete-alice", "Alice Delete");
        var bob = await CreateAuthenticatedClientAsync(factory, "delete-bob", "Bob Keeper");
        var charlie = await CreateAuthenticatedClientAsync(factory, "delete-charlie", "Charlie Request");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);
        _ = await charlie.Client.SendFriendRequestAsync(alice.Session.User.Id);

        await alice.Client.ReplaceAvatarAsync(TestImages.CreateJpegBytes(1600, 1200), "alice-delete-avatar.jpg");
        await RegisterDeviceTokenAsync(alice.Client, "00abff");
        var customCategory = await alice.Client.CreateCategoryAsync("Deletion Category");

        var deleteOnlyPlaceId = await alice.Client.CreatePlaceAsync("delete-only");
        var deleteOnlyReviewId = await alice.Client.CreatePlaceReviewAsync(
            deleteOnlyPlaceId,
            VisibilityLevel.Friends,
            5,
            "Alice-only review");
        var deleteOnlyDishReviewId = await alice.Client.CreateDishReviewAsync(
            deleteOnlyPlaceId,
            deleteOnlyReviewId,
            VisibilityLevel.Friends,
            "Pierogi",
            5);
        _ = await UploadPhotoAsync(alice.Client, placeReviewId: deleteOnlyReviewId);
        _ = await UploadPhotoAsync(alice.Client, dishReviewId: deleteOnlyDishReviewId);

        var sharedCustomPlaceId = await alice.Client.CreatePlaceAsync("shared-custom");
        await alice.Client.UpdateCustomPlaceDisplayNameAsync(sharedCustomPlaceId, "Alice Secret Spot");
        _ = await alice.Client.CreatePlaceReviewAsync(
            sharedCustomPlaceId,
            VisibilityLevel.Friends,
            4,
            "Alice shared review");
        _ = await bob.Client.CreatePlaceReviewAsync(
            sharedCustomPlaceId,
            VisibilityLevel.Friends,
            4,
            "Bob surviving review");

        var publicCatalogPlace = await alice.Client.CreatePlaceSummaryAsync(
            "public-provider",
            provider: "apple",
            providerPlaceId: "apple-public-provider-delete");
        _ = await alice.Client.CreatePlaceReviewAsync(
            publicCatalogPlace.Id,
            VisibilityLevel.Friends,
            3,
            "Public catalog review");

        List<string> mediaPaths;
        string oldHandle;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var user = await dbContext.Users.AsNoTracking().SingleAsync(x => x.Id == alice.Session.User.Id);
            oldHandle = user.Handle;

            var avatarStorageKeys = EnumerateAvatarStorageKeys(ResolveUploadStorageKey(user.AvatarUrl!));
            var photoStorageKeys = await dbContext.Photos
                .AsNoTracking()
                .Where(x => x.OwnerUserId == alice.Session.User.Id)
                .ToListAsync();

            mediaPaths = avatarStorageKeys
                .Concat(photoStorageKeys.SelectMany(x => x.EnumerateStorageKeys()))
                .Distinct(StringComparer.Ordinal)
                .Select(x => ToStoragePath(factory.PhotoRootPath, x))
                .ToList();
        }

        Assert.All(mediaPaths, path => Assert.True(File.Exists(path), $"Expected media file to exist before deletion: {path}"));

        var deleteResponse = await alice.Client.HttpClient.DeleteAsync("/me");
        var secondDeleteResponse = await alice.Client.HttpClient.DeleteAsync("/me");

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondDeleteResponse.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var deletedUser = await dbContext.Users.SingleAsync(x => x.Id == alice.Session.User.Id);

            Assert.Equal(UserAccountStatus.Deleted, deletedUser.Status);
            Assert.NotNull(deletedUser.DeletedAtUtc);
            Assert.Equal($"deleted-user-{alice.Session.User.Id:N}"[..25], deletedUser.Handle);
            Assert.Equal("Deleted user", deletedUser.DisplayName);
            Assert.Null(deletedUser.Bio);
            Assert.Null(deletedUser.Email);
            Assert.Null(deletedUser.AvatarUrl);
            Assert.Equal(VisibilityLevel.Private, deletedUser.ProfileVisibility);
            Assert.Equal(VisibilityLevel.Private, deletedUser.ProfilePictureVisibility);

            Assert.Empty(await dbContext.AppleIdentities.Where(x => x.UserId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.RefreshTokens.Where(x => x.UserId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.DeviceTokens.Where(x => x.UserId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.Friendships.Where(x => x.UserAId == alice.Session.User.Id || x.UserBId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.FriendRequests.Where(x => x.SenderUserId == alice.Session.User.Id || x.ReceiverUserId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.Notifications.Where(x => x.ActorUserId == alice.Session.User.Id || x.RecipientUserId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.PlaceReviews.Where(x => x.AuthorUserId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.DishReviews.Where(x => x.AuthorUserId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.Photos.Where(x => x.OwnerUserId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.Categories.Where(x => x.OwnerUserId == alice.Session.User.Id).ToListAsync());
            Assert.False(await dbContext.Categories.AnyAsync(x => x.Id == customCategory.Id));
            Assert.Empty(await dbContext.UserCategoryMemberships.Where(x => x.UserId == alice.Session.User.Id).ToListAsync());
            Assert.Empty(await dbContext.PlaceCategoryAssignments.Where(x => x.AssignedByUserId == alice.Session.User.Id).ToListAsync());

            Assert.False(await dbContext.Places.AnyAsync(x => x.Id == deleteOnlyPlaceId));
            Assert.True(await dbContext.Places.AnyAsync(x => x.Id == publicCatalogPlace.Id));

            var sharedCustomPlace = await dbContext.Places.SingleAsync(x => x.Id == sharedCustomPlaceId);
            Assert.Null(sharedCustomPlace.CreatedByUserId);
            Assert.Null(sharedCustomPlace.CustomDisplayName);
            Assert.Null(sharedCustomPlace.CustomDisplayNameSetByUserId);
            Assert.True(await dbContext.PlaceReviews.AnyAsync(x => x.PlaceId == sharedCustomPlaceId && x.AuthorUserId == bob.Session.User.Id));
        }

        Assert.All(mediaPaths, path => Assert.False(File.Exists(path), $"Expected media file to be deleted after account deletion: {path}"));

        var oldHandleBase = oldHandle.Split('#')[0];
        var searchResults = await bob.Client.SearchUsersAsync(oldHandleBase);
        Assert.DoesNotContain(searchResults, x => x.User.Id == alice.Session.User.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.HttpClient.GetAsync($"/users/{alice.Session.User.Id}")).StatusCode);
        Assert.Empty(await bob.Client.HttpClient.GetFromJsonAsync<IReadOnlyList<TrustMap.Api.Features.Friends.FriendDto>>("/friends", JsonDefaults.Options) ?? []);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await bob.Client.HttpClient.PostAsJsonAsync(
                "/friend-requests",
                new TrustMap.Api.Features.Friends.CreateFriendRequestRequest { ReceiverUserId = alice.Session.User.Id },
                JsonDefaults.Options)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await alice.Client.HttpClient.GetAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RegisterDeviceTokenAsync(alice.Client, "00abff")).StatusCode);

        var refreshResponse = await alice.Client.HttpClient.PostAsJsonAsync(
            "/auth/refresh",
            new RefreshTokenRequest { RefreshToken = alice.Session.RefreshToken },
            JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);

        var newAliceClient = new TestApiClient(factory.CreateClient());
        var newAliceSession = await newAliceClient.SignInAsync("delete-alice", "Alice Again");
        Assert.NotEqual(alice.Session.User.Id, newAliceSession.User.Id);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var appleIdentity = await dbContext.AppleIdentities.SingleAsync(x => x.UserId == newAliceSession.User.Id);
            Assert.Equal("delete-alice", appleIdentity.Subject);
            Assert.False(await dbContext.AppleIdentities.AnyAsync(x => x.UserId == alice.Session.User.Id));
        }
    }

    [Fact]
    public async Task DeleteMe_StorageDeletionFailureAfterCommit_DoesNotUndoAccountDeletion()
    {
        var logs = new CapturingLoggerProvider();
        await using var factory = new TrustMapWebApplicationFactory(
            services =>
            {
                services.RemoveAll<IPhotoStorage>();
                services.AddSingleton<IPhotoStorage, ThrowingDeletePhotoStorage>();
                services.AddSingleton<ILoggerProvider>(logs);
            });
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "delete-storage-failure", "Storage Failure");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var user = await dbContext.Users.SingleAsync(x => x.Id == alice.Session.User.Id);
            user.UpdateProfile(user.Handle, user.DisplayName, user.Bio, "/uploads/account-delete/fail.jpg");

            var place = new Place(
                Guid.CreateVersion7(),
                "Failure Place",
                "Failure Address",
                "Warsaw",
                "PL",
                52.2,
                21.0,
                provider: null,
                providerPlaceId: null,
                createdByUserId: user.Id);
            var review = new PlaceReview(
                Guid.CreateVersion7(),
                place.Id,
                user.Id,
                VisibilityLevel.Friends,
                5,
                "Failure",
                "Failure body");
            var photo = new Photo(
                Guid.CreateVersion7(),
                user.Id,
                place.Id,
                review.Id,
                null,
                "local",
                "account-delete/photo.jpg",
                "/uploads/account-delete/photo.jpg",
                "account-delete/photo-medium.jpg",
                "/uploads/account-delete/photo-medium.jpg",
                "account-delete/photo-thumb.jpg",
                "/uploads/account-delete/photo-thumb.jpg",
                "photo.jpg",
                "image/jpeg",
                10,
                1,
                1);

            dbContext.Places.Add(place);
            dbContext.PlaceReviews.Add(review);
            dbContext.Photos.Add(photo);
            await dbContext.SaveChangesAsync();
        }

        var response = await alice.Client.HttpClient.DeleteAsync("/me");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var deletedUser = await dbContext.Users.SingleAsync(x => x.Id == alice.Session.User.Id);
            Assert.Equal(UserAccountStatus.Deleted, deletedUser.Status);
            Assert.Empty(await dbContext.Photos.Where(x => x.OwnerUserId == alice.Session.User.Id).ToListAsync());
        }

        Assert.Contains(
            logs.Entries,
            entry => entry.Level == LogLevel.Warning
                && entry.CategoryName.EndsWith(".AccountDeletionService", StringComparison.Ordinal)
                && entry.Message.Contains("Failed to delete account media blob", StringComparison.Ordinal));
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

    private static async Task MakeFriendsAsync(
        TestApiClient senderClient,
        TestApiClient receiverClient,
        Guid receiverUserId)
    {
        var request = await senderClient.SendFriendRequestAsync(receiverUserId);
        var response = await receiverClient.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> RegisterDeviceTokenAsync(TestApiClient client, string token)
    {
        return await client.HttpClient.PostAsJsonAsync(
            "/me/device-tokens",
            new
            {
                Token = token,
                Platform = "ios",
                Environment = "development",
                AppVersion = "1.0",
                BuildNumber = "1"
            },
            JsonDefaults.Options);
    }

    private static async Task<PhotoDto> UploadPhotoAsync(
        TestApiClient client,
        Guid? placeReviewId = null,
        Guid? dishReviewId = null)
    {
        using var multipart = new MultipartFormDataContent();

        if (placeReviewId.HasValue)
        {
            multipart.Add(new StringContent(placeReviewId.Value.ToString("D")), "placeReviewId");
        }

        if (dishReviewId.HasValue)
        {
            multipart.Add(new StringContent(dishReviewId.Value.ToString("D")), "dishReviewId");
        }

        var fileContent = new ByteArrayContent(TestImages.CreateJpegBytes(1600, 1200));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(fileContent, "file", "review-photo.jpg");

        var response = await client.HttpClient.PostAsync("/photos/upload", multipart);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PhotoDto>(JsonDefaults.Options))!;
    }

    private static string ResolveUploadStorageKey(string rawUrl)
    {
        const string requestPath = "/uploads/";
        Assert.StartsWith(requestPath, rawUrl, StringComparison.Ordinal);
        return rawUrl[requestPath.Length..];
    }

    private static IEnumerable<string> EnumerateAvatarStorageKeys(string originalStorageKey)
    {
        yield return originalStorageKey;

        var extension = Path.GetExtension(originalStorageKey);
        var basePath = extension.Length == 0
            ? originalStorageKey
            : originalStorageKey[..^extension.Length];
        var variantExtension = extension.Length == 0 ? ".jpg" : extension;

        yield return $"{basePath}-medium{variantExtension}";
        yield return $"{basePath}-thumb{variantExtension}";
    }

    private static string ToStoragePath(string rootPath, string storageKey)
    {
        return Path.Combine(rootPath, storageKey.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class ThrowingDeletePhotoStorage : IPhotoStorage
    {
        public Task<StoredPhoto> SaveAsync(Stream stream, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<StoredFileResult?> OpenReadAsync(
            string storageKey,
            string? contentTypeHint,
            CancellationToken cancellationToken) =>
            Task.FromResult<StoredFileResult?>(null);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new IOException("Storage delete failed.");
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<CapturedLog> _entries = new();

        public IReadOnlyCollection<CapturedLog> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(categoryName, _entries);

        public void Dispose()
        {
        }
    }

    private sealed class CapturingLogger(
        string categoryName,
        ConcurrentQueue<CapturedLog> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(new CapturedLog(
                categoryName,
                logLevel,
                formatter(state, exception)));
        }
    }

    private sealed record CapturedLog(
        string CategoryName,
        LogLevel Level,
        string Message);

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
        }
    }
}
