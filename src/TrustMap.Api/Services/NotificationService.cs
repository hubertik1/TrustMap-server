using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Services.PushNotifications;
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

        var notificationIds = notifications.Select(x => x.Id).ToList();
        var persistedNotifications = await dbContext.Notifications
            .AsNoTracking()
            .Include(x => x.ActorUser)
            .Where(x => notificationIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (persistedNotifications.Count == 0)
        {
            return;
        }

        var recipientIds = persistedNotifications.Select(x => x.RecipientUserId).Distinct().ToList();
        var deviceTokens = await dbContext.DeviceTokens
            .AsNoTracking()
            .Where(x => recipientIds.Contains(x.UserId) && x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        if (deviceTokens.Count == 0)
        {
            return;
        }

        var tokensByUser = deviceTokens
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        foreach (var notification in persistedNotifications)
        {
            if (!tokensByUser.TryGetValue(notification.RecipientUserId, out var recipientTokens))
            {
                continue;
            }

            foreach (var deviceToken in recipientTokens)
            {
                try
                {
                    await pushNotificationSender.SendAsync(
                        ToPushNotification(notification, deviceToken.Token),
                        cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to send push notification {NotificationId} to user {RecipientUserId}.",
                        notification.Id,
                        notification.RecipientUserId);
                }
            }
        }
    }

    private async Task<HashSet<Guid>> GetDirectFriendIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var friendIds = await dbContext.Friendships
            .AsNoTracking()
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

    private static PushNotificationRequest ToPushNotification(UserNotification notification, string deviceToken)
    {
        var actorMention = BuildActorMention(notification.ActorUser);
        var body = notification.Type switch
        {
            NotificationType.PlaceReviewAdded => $"{actorMention} added a place review",
            NotificationType.DishReviewAdded => $"{actorMention} added a dish review",
            NotificationType.FriendRequestSent => $"{actorMention} sent you a friend request",
            _ => "You have a new notification"
        };

        return new PushNotificationRequest(
            deviceToken,
            "TrustMap",
            body,
            BuildPayloadData(notification));
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

        return data;
    }

    private static string BuildActorMention(User actor)
    {
        var handle = actor.Handle.Trim();
        var hashIndex = handle.IndexOf('#', StringComparison.Ordinal);
        var baseHandle = hashIndex >= 0 ? handle[..hashIndex] : handle;
        baseHandle = baseHandle.Trim().TrimStart('@');

        if (string.IsNullOrWhiteSpace(baseHandle))
        {
            baseHandle = actor.DisplayName.Trim();
        }

        return string.IsNullOrWhiteSpace(baseHandle) ? "Someone" : $"@{baseHandle}";
    }
}
