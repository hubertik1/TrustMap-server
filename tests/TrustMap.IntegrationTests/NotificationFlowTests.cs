using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TrustMap.Api.Features.Notifications;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Services;
using TrustMap.Api.Services.PushNotifications;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class NotificationFlowTests
{
    [Fact]
    public async Task PlaceReview_CreatesNotificationOnlyForDirectFriends()
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
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);
        await CreateFriendshipAsync(factory, bob.User.Id, charlie.User.Id);

        var place = await aliceClient.CreatePlaceSummaryAsync("place-notification");
        var reviewId = await aliceClient.CreatePlaceReviewAsync(place.Id, VisibilityLevel.Friends, 5, "Great");

        var bobNotifications = await FetchNotificationsAsync(bobClient);
        var aliceNotifications = await FetchNotificationsAsync(aliceClient);
        var charlieNotifications = await FetchNotificationsAsync(charlieClient);

        var notification = Assert.Single(bobNotifications.Items);
        Assert.Equal(NotificationType.PlaceReviewAdded, notification.Type);
        Assert.Equal(alice.User.Id, notification.Actor.Id);
        Assert.Equal(alice.User.Handle, notification.Actor.Handle);
        Assert.Equal(place.Id, notification.PlaceId);
        Assert.Equal(place.DisplayName, notification.PlaceName);
        Assert.Equal(reviewId, notification.PlaceReviewId);
        Assert.Equal(5, notification.Rating);
        Assert.Null(notification.ReadAtUtc);
        Assert.Empty(aliceNotifications.Items);
        Assert.Empty(charlieNotifications.Items);

        var privatePlaceId = await aliceClient.CreatePlaceAsync("private-place-notification");
        await aliceClient.CreatePlaceReviewAsync(privatePlaceId, VisibilityLevel.Private, 4, "Private");

        bobNotifications = await FetchNotificationsAsync(bobClient);
        Assert.Single(bobNotifications.Items);

        var publicPlaceId = await aliceClient.CreatePlaceAsync("public-place-notification");
        await aliceClient.CreatePlaceReviewAsync(publicPlaceId, VisibilityLevel.Public, 3, "Public");

        bobNotifications = await FetchNotificationsAsync(bobClient);
        charlieNotifications = await FetchNotificationsAsync(charlieClient);
        Assert.Equal(2, bobNotifications.Items.Count);
        Assert.Empty(charlieNotifications.Items);

        await InvokeDuplicatePlaceReviewNotificationCreationAsync(factory, alice.User.Id, place.Id, reviewId);
        bobNotifications = await FetchNotificationsAsync(bobClient);
        Assert.Equal(2, bobNotifications.Items.Count);
    }

    [Fact]
    public async Task DishReview_NotificationIncludesDishPlaceActorAndRating()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice-dish", "Alice Dish");
        var bob = await bobClient.SignInAsync("bob-dish", "Bob Dish");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);

        var place = await aliceClient.CreatePlaceSummaryAsync("dish-notification");
        var parentReviewId = await aliceClient.CreatePlaceReviewAsync(place.Id, VisibilityLevel.Private, 4, "Private parent");
        var dishReviewId = await aliceClient.CreateDishReviewAsync(
            place.Id,
            parentReviewId,
            VisibilityLevel.Friends,
            "Pierogi",
            5);

        var notifications = await FetchNotificationsAsync(bobClient);

        var notification = Assert.Single(notifications.Items);
        Assert.Equal(NotificationType.DishReviewAdded, notification.Type);
        Assert.Equal(alice.User.Id, notification.Actor.Id);
        Assert.Equal(place.Id, notification.PlaceId);
        Assert.Equal(place.DisplayName, notification.PlaceName);
        Assert.Equal(dishReviewId, notification.DishReviewId);
        Assert.Equal("Pierogi", notification.DishName);
        Assert.Equal(5, notification.Rating);
    }

    [Fact]
    public async Task FriendRequest_NotificationLifecycleMatchesRequestState()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());
        var charlieClient = new TestApiClient(factory.CreateClient());
        var daveClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice-request", "Alice Request");
        var bob = await bobClient.SignInAsync("bob-request", "Bob Request");
        var charlie = await charlieClient.SignInAsync("charlie-request", "Charlie Request");
        var dave = await daveClient.SignInAsync("dave-request", "Dave Request");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        charlieClient.Authenticate(charlie.AccessToken);
        daveClient.Authenticate(dave.AccessToken);

        var request = await aliceClient.SendFriendRequestAsync(bob.User.Id);

        var bobNotifications = await FetchNotificationsAsync(bobClient);
        var notification = Assert.Single(bobNotifications.Items);
        Assert.Equal(NotificationType.FriendRequestSent, notification.Type);
        Assert.Equal(request.Id, notification.FriendRequestId);
        Assert.Equal(FriendRequestStatus.Pending, notification.FriendRequestStatus);
        Assert.Equal(alice.User.Id, notification.Actor.Id);
        Assert.Null(notification.ReadAtUtc);
        Assert.Equal(1, await FetchUnreadCountAsync(bobClient));
        Assert.Empty((await FetchNotificationsAsync(aliceClient)).Items);

        var acceptResponse = await bobClient.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        acceptResponse.EnsureSuccessStatusCode();

        bobNotifications = await FetchNotificationsAsync(bobClient);
        notification = Assert.Single(bobNotifications.Items);
        Assert.Equal(FriendRequestStatus.Accepted, notification.FriendRequestStatus);
        Assert.NotNull(notification.ReadAtUtc);
        Assert.Equal(0, await FetchUnreadCountAsync(bobClient));

        var cancelRequest = await aliceClient.SendFriendRequestAsync(charlie.User.Id);
        Assert.Single((await FetchNotificationsAsync(charlieClient)).Items);
        var cancelResponse = await aliceClient.HttpClient.PostAsync($"/friend-requests/{cancelRequest.Id}/cancel", content: null);
        cancelResponse.EnsureSuccessStatusCode();
        Assert.Empty((await FetchNotificationsAsync(charlieClient)).Items);

        var rejectRequest = await aliceClient.SendFriendRequestAsync(dave.User.Id);
        var rejectResponse = await daveClient.HttpClient.PostAsync($"/friend-requests/{rejectRequest.Id}/reject", content: null);
        rejectResponse.EnsureSuccessStatusCode();
        var daveNotification = Assert.Single((await FetchNotificationsAsync(daveClient)).Items);
        Assert.Equal(FriendRequestStatus.Rejected, daveNotification.FriendRequestStatus);
        Assert.NotNull(daveNotification.ReadAtUtc);
    }

    [Fact]
    public async Task MarkRead_IsIdempotentAndCannotMutateAnotherUsersNotification()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice-read", "Alice Read");
        var bob = await bobClient.SignInAsync("bob-read", "Bob Read");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        await aliceClient.SendFriendRequestAsync(bob.User.Id);
        var notification = Assert.Single((await FetchNotificationsAsync(bobClient)).Items);

        var forbiddenResponse = await aliceClient.HttpClient.PatchAsync($"/notifications/{notification.Id}/read", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        var firstReadResponse = await bobClient.HttpClient.PatchAsync($"/notifications/{notification.Id}/read", content: null);
        var secondReadResponse = await bobClient.HttpClient.PatchAsync($"/notifications/{notification.Id}/read", content: null);

        Assert.Equal(HttpStatusCode.NoContent, firstReadResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondReadResponse.StatusCode);
        Assert.Equal(0, await FetchUnreadCountAsync(bobClient));
    }

    [Fact]
    public async Task MarkAllRead_MarksOnlyVisibleOwnNotifications()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice-read-all", "Alice Read All");
        var bob = await bobClient.SignInAsync("bob-read-all", "Bob Read All");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);

        var hiddenPlaceId = await aliceClient.CreatePlaceAsync("read-all-hidden");
        await aliceClient.CreatePlaceReviewAsync(hiddenPlaceId, VisibilityLevel.Friends, 4, "Hidden after unfriend");

        await DeleteFriendshipAsync(factory, alice.User.Id, bob.User.Id);
        var visibleRequest = await aliceClient.SendFriendRequestAsync(bob.User.Id);

        var visibleNotifications = await FetchNotificationsAsync(bobClient);
        var visibleNotification = Assert.Single(visibleNotifications.Items);
        Assert.Equal(NotificationType.FriendRequestSent, visibleNotification.Type);
        Assert.Equal(visibleRequest.Id, visibleNotification.FriendRequestId);
        Assert.Equal(1, await FetchUnreadCountAsync(bobClient));

        var readAllResponse = await bobClient.HttpClient.PatchAsync("/notifications/read-all", content: null);
        Assert.Equal(HttpStatusCode.NoContent, readAllResponse.StatusCode);

        Assert.Equal(0, await FetchUnreadCountAsync(bobClient));

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var notifications = await dbContext.Notifications
            .AsNoTracking()
            .Where(x => x.RecipientUserId == bob.User.Id)
            .ToListAsync();

        Assert.Contains(notifications, x => x.Type == NotificationType.FriendRequestSent && x.ReadAt != null);
        Assert.Contains(notifications, x => x.Type == NotificationType.PlaceReviewAdded && x.ReadAt == null);
    }

    [Fact]
    public async Task ReviewDeletionAndPrivateVisibilityRemoveNotifications()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice-review-cleanup", "Alice Cleanup");
        var bob = await bobClient.SignInAsync("bob-review-cleanup", "Bob Cleanup");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);

        var deletedPlaceId = await aliceClient.CreatePlaceAsync("delete-notification");
        var deletedReviewId = await aliceClient.CreatePlaceReviewAsync(deletedPlaceId, VisibilityLevel.Friends, 4, "Will delete");
        Assert.Single((await FetchNotificationsAsync(bobClient)).Items);

        var deleteResponse = await aliceClient.HttpClient.DeleteAsync($"/place-reviews/{deletedReviewId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.Empty((await FetchNotificationsAsync(bobClient)).Items);

        var privatePlaceId = await aliceClient.CreatePlaceAsync("private-update-notification");
        var privateReviewId = await aliceClient.CreatePlaceReviewAsync(privatePlaceId, VisibilityLevel.Friends, 4, "Will hide");
        Assert.Single((await FetchNotificationsAsync(bobClient)).Items);

        var updateResponse = await aliceClient.HttpClient.PatchAsJsonAsync(
            $"/place-reviews/{privateReviewId}",
            new UpdatePlaceReviewRequest
            {
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Private,
                Rating = 4,
                Title = "Review",
                Body = "Will hide"
            },
            JsonDefaults.Options);

        updateResponse.EnsureSuccessStatusCode();
        Assert.Empty((await FetchNotificationsAsync(bobClient)).Items);
    }

    [Fact]
    public async Task NotificationsPaginationUsesCursorAndClampsTake()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice-pages", "Alice Pages");
        var bob = await bobClient.SignInAsync("bob-pages", "Bob Pages");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);

        var oldestPlaceId = await aliceClient.CreatePlaceAsync("page-oldest");
        await aliceClient.CreatePlaceReviewAsync(oldestPlaceId, VisibilityLevel.Friends, 3, "Oldest");
        await Task.Delay(TimeSpan.FromMilliseconds(20));

        var middlePlaceId = await aliceClient.CreatePlaceAsync("page-middle");
        await aliceClient.CreatePlaceReviewAsync(middlePlaceId, VisibilityLevel.Friends, 4, "Middle");
        await Task.Delay(TimeSpan.FromMilliseconds(20));

        var newestPlaceId = await aliceClient.CreatePlaceAsync("page-newest");
        await aliceClient.CreatePlaceReviewAsync(newestPlaceId, VisibilityLevel.Friends, 5, "Newest");

        var firstPage = await FetchNotificationsAsync(bobClient, "/notifications?take=1");
        Assert.Single(firstPage.Items);
        Assert.Equal(newestPlaceId, firstPage.Items[0].PlaceId);
        Assert.NotNull(firstPage.NextCursorUtc);

        var secondPage = await FetchNotificationsAsync(
            bobClient,
            $"/notifications?take=1&beforeUtc={Uri.EscapeDataString(firstPage.NextCursorUtc!.Value.ToString("O"))}");
        Assert.Single(secondPage.Items);
        Assert.Equal(middlePlaceId, secondPage.Items[0].PlaceId);

        var clampedPage = await FetchNotificationsAsync(bobClient, "/notifications?take=0");
        Assert.Single(clampedPage.Items);
    }

    [Fact]
    public async Task PushSendingFailureDoesNotFailInAppNotificationCreation()
    {
        await using var factory = new TrustMapWebApplicationFactory(services =>
        {
            services.RemoveAll<IPushNotificationSender>();
            services.AddSingleton<IPushNotificationSender, ThrowingPushNotificationSender>();
        });
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice-push-failure", "Alice Push Failure");
        var bob = await bobClient.SignInAsync("bob-push-failure", "Bob Push Failure");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);

        var tokenResponse = await bobClient.HttpClient.PostAsJsonAsync(
            "/me/device-tokens",
            new
            {
                token = "00abff",
                platform = "ios",
                environment = "development"
            });
        tokenResponse.EnsureSuccessStatusCode();

        var placeId = await aliceClient.CreatePlaceAsync("push-failure");
        await aliceClient.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 5, "Push failure should not fail review creation");

        var notification = Assert.Single((await FetchNotificationsAsync(bobClient)).Items);
        Assert.Equal(NotificationType.PlaceReviewAdded, notification.Type);
        Assert.Equal(1, await FetchUnreadCountAsync(bobClient));
    }

    private static async Task CreateFriendshipAsync(
        TrustMapWebApplicationFactory factory,
        Guid firstUserId,
        Guid secondUserId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        dbContext.Friendships.Add(new Friendship(Guid.CreateVersion7(), firstUserId, secondUserId));
        await dbContext.SaveChangesAsync();
    }

    private static async Task DeleteFriendshipAsync(
        TrustMapWebApplicationFactory factory,
        Guid firstUserId,
        Guid secondUserId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var pairKey = Friendship.BuildPairKey(firstUserId, secondUserId);

        await dbContext.Friendships
            .Where(x => x.PairKey == pairKey)
            .ExecuteDeleteAsync();
    }

    private static async Task InvokeDuplicatePlaceReviewNotificationCreationAsync(
        TrustMapWebApplicationFactory factory,
        Guid actorUserId,
        Guid placeId,
        Guid placeReviewId)
    {
        using var scope = factory.Services.CreateScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<NotificationService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();

        await notificationService.CreatePlaceReviewAddedAsync(
            actorUserId,
            placeId,
            placeReviewId,
            VisibilityLevel.Friends,
            CancellationToken.None);
        await notificationService.CreatePlaceReviewAddedAsync(
            actorUserId,
            placeId,
            placeReviewId,
            VisibilityLevel.Friends,
            CancellationToken.None);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<NotificationsResponseDto> FetchNotificationsAsync(
        TestApiClient client,
        string path = "/notifications")
    {
        var response = await client.HttpClient.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NotificationsResponseDto>(JsonDefaults.Options))!;
    }

    private static async Task<int> FetchUnreadCountAsync(TestApiClient client)
    {
        var response = await client.HttpClient.GetAsync("/notifications/unread-count");
        response.EnsureSuccessStatusCode();
        var count = (await response.Content.ReadFromJsonAsync<UnreadNotificationCountDto>(JsonDefaults.Options))!;
        return count.Count;
    }

    private sealed class ThrowingPushNotificationSender : IPushNotificationSender
    {
        public Task SendAsync(PushNotificationRequest notification, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated APNs failure.");
    }
}
