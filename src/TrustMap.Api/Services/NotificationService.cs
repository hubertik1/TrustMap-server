using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Services.PushNotifications;
using TrustMap.Domain.Common;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class NotificationService(
    TrustMapDbContext dbContext,
    IPushNotificationSender pushNotificationSender,
    ILogger<NotificationService> logger)
{
    public async Task<IReadOnlyList<UserNotification>> CreatePlaceReviewAddedAsync(
        Guid actorUserId,
        Guid placeId,
        Guid placeReviewId,
        VisibilityLevel visibility,
        CancellationToken cancellationToken)
    {
        EnumGuard.EnsureDefined(visibility, nameof(visibility));

        if (visibility == VisibilityLevel.Private)
        {
            return [];
        }

        var recipientIds = await GetDirectFriendIdsAsync(actorUserId, cancellationToken);
        var notifications = recipientIds
            .Where(recipientUserId => recipientUserId != actorUserId)
            .Select(recipientUserId => new UserNotification(
                Guid.CreateVersion7(),
                recipientUserId,
                actorUserId,
                NotificationType.PlaceReviewAdded,
                placeId: placeId,
                placeReviewId: placeReviewId))
            .ToArray();

        return await AddMissingNotificationsAsync(notifications, cancellationToken);
    }

    public async Task<IReadOnlyList<UserNotification>> CreateDishReviewAddedAsync(
        Guid actorUserId,
        Guid placeId,
        Guid dishReviewId,
        VisibilityLevel visibility,
        CancellationToken cancellationToken)
    {
        EnumGuard.EnsureDefined(visibility, nameof(visibility));

        if (visibility == VisibilityLevel.Private)
        {
            return [];
        }

        var recipientIds = await GetDirectFriendIdsAsync(actorUserId, cancellationToken);
        var notifications = recipientIds
            .Where(recipientUserId => recipientUserId != actorUserId)
            .Select(recipientUserId => new UserNotification(
                Guid.CreateVersion7(),
                recipientUserId,
                actorUserId,
                NotificationType.DishReviewAdded,
                placeId: placeId,
                dishReviewId: dishReviewId))
            .ToArray();

        return await AddMissingNotificationsAsync(notifications, cancellationToken);
    }

    public async Task<IReadOnlyList<UserNotification>> CreateFriendRequestSentAsync(
        Guid senderUserId,
        Guid receiverUserId,
        Guid friendRequestId,
        CancellationToken cancellationToken)
    {
        if (senderUserId == receiverUserId)
        {
            return [];
        }

        var notification = new UserNotification(
            Guid.CreateVersion7(),
            receiverUserId,
            senderUserId,
            NotificationType.FriendRequestSent,
            friendRequestId: friendRequestId);

        return await AddMissingNotificationsAsync([notification], cancellationToken);
    }

    public async Task MarkFriendRequestNotificationReadAsync(
        Guid friendRequestId,
        CancellationToken cancellationToken)
    {
        var notifications = await dbContext.Notifications
            .Where(x => x.FriendRequestId == friendRequestId && x.ReadAt == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.MarkAsRead();
        }
    }

    public async Task DeleteFriendRequestNotificationAsync(
        Guid friendRequestId,
        CancellationToken cancellationToken)
    {
        var notifications = await dbContext.Notifications
            .Where(x => x.FriendRequestId == friendRequestId)
            .ToListAsync(cancellationToken);

        dbContext.Notifications.RemoveRange(notifications);
    }

    public async Task DeleteReviewNotificationsAsync(
        Guid? placeReviewId = null,
        Guid? dishReviewId = null,
        CancellationToken cancellationToken = default)
    {
        if (placeReviewId is null && dishReviewId is null)
        {
            return;
        }

        var notifications = await dbContext.Notifications
            .Where(x =>
                (placeReviewId.HasValue && x.PlaceReviewId == placeReviewId.Value)
                || (dishReviewId.HasValue && x.DishReviewId == dishReviewId.Value))
            .ToListAsync(cancellationToken);

        dbContext.Notifications.RemoveRange(notifications);
    }

    public async Task SendPushNotificationsAsync(
        IReadOnlyCollection<UserNotification> notifications,
        CancellationToken cancellationToken)
    {
        if (notifications.Count == 0)
        {
            return;
        }

        try
        {
            var notificationIds = notifications.Select(x => x.Id).ToList();
            var persistedNotifications = await dbContext.Notifications
                .AsNoTracking()
                .Include(x => x.ActorUser)
                .Where(x => notificationIds.Contains(x.Id))
                .Where(x => x.ActorUser.Status == UserAccountStatus.Active && x.RecipientUser.Status == UserAccountStatus.Active)
                .ToListAsync(cancellationToken);

            if (persistedNotifications.Count == 0)
            {
                return;
            }

            var recipientIds = persistedNotifications.Select(x => x.RecipientUserId).Distinct().ToList();
            var unreadCounts = await dbContext.Notifications
                .AsNoTracking()
                .Where(x => recipientIds.Contains(x.RecipientUserId) && x.ReadAt == null)
                .GroupBy(x => x.RecipientUserId)
                .Select(x => new { RecipientUserId = x.Key, Count = x.Count() })
                .ToDictionaryAsync(x => x.RecipientUserId, x => x.Count, cancellationToken);

            var deviceTokens = await dbContext.DeviceTokens
                .Where(x => recipientIds.Contains(x.UserId) && x.RevokedAt == null && x.User.Status == UserAccountStatus.Active)
                .ToListAsync(cancellationToken);

            if (deviceTokens.Count == 0)
            {
                return;
            }

            var tokensByUser = deviceTokens
                .GroupBy(x => x.UserId)
                .ToDictionary(x => x.Key, x => x.ToArray());
            var revokedAnyToken = false;

            foreach (var notification in persistedNotifications)
            {
                if (!tokensByUser.TryGetValue(notification.RecipientUserId, out var recipientTokens))
                {
                    continue;
                }

                foreach (var deviceToken in recipientTokens)
                {
                    if (deviceToken.RevokedAt is not null)
                    {
                        continue;
                    }

                    var deviceTokenHashPrefix = BuildDeviceTokenHashPrefix(deviceToken.Token);
                    try
                    {
                        var badgeCount = unreadCounts.TryGetValue(notification.RecipientUserId, out var unreadCount)
                            ? unreadCount
                            : (int?)null;
                        var result = await pushNotificationSender.SendAsync(
                            ToPushNotification(notification, deviceToken, badgeCount),
                            cancellationToken);

                        if (result.Success)
                        {
                            continue;
                        }

                        logger.LogWarning(
                            "Push notification {NotificationId} to user {RecipientUserId} failed with status {StatusCode}, provider reason {ProviderReason}, device token hash {DeviceTokenHashPrefix}.",
                            notification.Id,
                            notification.RecipientUserId,
                            result.StatusCode,
                            result.ProviderReason,
                            deviceTokenHashPrefix);

                        if (result.ShouldInvalidateDeviceToken)
                        {
                            deviceToken.Revoke();
                            revokedAnyToken = true;
                            logger.LogInformation(
                                "Revoked invalid push device token hash {DeviceTokenHashPrefix} for user {RecipientUserId}.",
                                deviceTokenHashPrefix,
                                notification.RecipientUserId);
                        }
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        logger.LogWarning(
                            "Failed to send push notification {NotificationId} to user {RecipientUserId}; exception type {ExceptionType}; device token hash {DeviceTokenHashPrefix}.",
                            notification.Id,
                            notification.RecipientUserId,
                            exception.GetType().Name,
                            deviceTokenHashPrefix);
                    }
                }
            }

            if (revokedAnyToken)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Push notification dispatch failed after in-app notifications were saved.");
        }
    }

    private async Task<HashSet<Guid>> GetDirectFriendIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var friendIds = await dbContext.Friendships
            .AsNoTracking()
            .Where(x => x.UserA.Status == UserAccountStatus.Active && x.UserB.Status == UserAccountStatus.Active)
            .Where(x => x.UserAId == userId || x.UserBId == userId)
            .Select(x => x.UserAId == userId ? x.UserBId : x.UserAId)
            .ToListAsync(cancellationToken);

        return friendIds.ToHashSet();
    }

    private async Task<IReadOnlyList<UserNotification>> AddMissingNotificationsAsync(
        IReadOnlyCollection<UserNotification> notifications,
        CancellationToken cancellationToken)
    {
        if (notifications.Count == 0)
        {
            return [];
        }

        var deduplicationKeys = notifications.Select(x => x.DeduplicationKey).ToList();
        var existingKeys = await dbContext.Notifications
            .AsNoTracking()
            .Where(x => deduplicationKeys.Contains(x.DeduplicationKey))
            .Select(x => x.DeduplicationKey)
            .ToListAsync(cancellationToken);

        var knownKeys = existingKeys.ToHashSet(StringComparer.Ordinal);
        foreach (var pendingKey in dbContext.ChangeTracker
                     .Entries<UserNotification>()
                     .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Unchanged)
                     .Select(x => x.Entity.DeduplicationKey))
        {
            knownKeys.Add(pendingKey);
        }

        var addedNotifications = new List<UserNotification>();
        foreach (var notification in notifications)
        {
            if (knownKeys.Add(notification.DeduplicationKey))
            {
                dbContext.Notifications.Add(notification);
                addedNotifications.Add(notification);
            }
        }

        return addedNotifications;
    }

    private static PushNotificationRequest ToPushNotification(
        UserNotification notification,
        UserDeviceToken deviceToken,
        int? badgeCount)
    {
        var actorMention = BuildActorMention(notification.ActorUser);
        var fallbackActor = actorMention ?? "Someone";
        var body = notification.Type switch
        {
            NotificationType.PlaceReviewAdded => $"{fallbackActor} added a place review",
            NotificationType.DishReviewAdded => $"{fallbackActor} added a dish review",
            NotificationType.FriendRequestSent => $"{fallbackActor} sent you a friend request",
            _ => "You have a new notification"
        };
        // iOS resolves these keys from the app's Localizable.strings, using its
        // current language even when the app is closed. User names stay literal.
        var localizationKey = notification.Type switch
        {
            NotificationType.PlaceReviewAdded => actorMention is null
                ? "push.place_review_added_unknown_actor" : "ui.value_added_a_place_review",
            NotificationType.DishReviewAdded => actorMention is null
                ? "push.dish_review_added_unknown_actor" : "ui.value_added_a_dish_review",
            NotificationType.FriendRequestSent => actorMention is null
                ? "push.friend_request_sent_unknown_actor" : "ui.value_sent_you_a_friend_request",
            _ => "push.new_notification"
        };
        string[] localizationArguments = actorMention is not null
            && notification.Type is NotificationType.PlaceReviewAdded or NotificationType.DishReviewAdded or NotificationType.FriendRequestSent
                ? [actorMention] : [];

        return new PushNotificationRequest(
            deviceToken.Token,
            deviceToken.Environment,
            "TrustMap",
            body,
            BuildPayloadData(notification),
            badgeCount,
            localizationKey,
            localizationArguments);
    }

    private static IReadOnlyDictionary<string, string> BuildPayloadData(UserNotification notification)
    {
        var data = new Dictionary<string, string>
        {
            ["notificationId"] = notification.Id.ToString("D"),
            ["type"] = notification.Type.ToString()
        };

        if (notification.PlaceId.HasValue)
        {
            data["placeId"] = notification.PlaceId.Value.ToString("D");
        }

        if (notification.FriendRequestId.HasValue)
        {
            data["friendRequestId"] = notification.FriendRequestId.Value.ToString("D");
        }

        if (notification.PlaceReviewId.HasValue)
        {
            data["placeReviewId"] = notification.PlaceReviewId.Value.ToString("D");
        }

        if (notification.DishReviewId.HasValue)
        {
            data["dishReviewId"] = notification.DishReviewId.Value.ToString("D");
        }

        return data;
    }

    private static string BuildDeviceTokenHashPrefix(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }

    private static string? BuildActorMention(User actor)
    {
        var handle = actor.Handle.Trim();
        var hashIndex = handle.IndexOf('#', StringComparison.Ordinal);
        var baseHandle = hashIndex >= 0 ? handle[..hashIndex] : handle;
        baseHandle = baseHandle.Trim().TrimStart('@');

        if (string.IsNullOrWhiteSpace(baseHandle))
        {
            baseHandle = actor.DisplayName.Trim();
        }

        return string.IsNullOrWhiteSpace(baseHandle) ? null : $"@{baseHandle}";
    }
}
