using TrustMap.Domain.Enums;

namespace TrustMap.Domain.Entities;

public sealed class UserNotification
{
    private UserNotification()
    {
    }

    public UserNotification(
        Guid id,
        Guid recipientUserId,
        Guid actorUserId,
        NotificationType type,
        Guid? placeId = null,
        Guid? placeReviewId = null,
        Guid? dishReviewId = null,
        Guid? friendRequestId = null)
    {
        if (recipientUserId == actorUserId)
        {
            throw new InvalidOperationException("Cannot create a notification for the acting user.");
        }

        EnsureValidReferences(type, placeId, placeReviewId, dishReviewId, friendRequestId);

        Id = id;
        RecipientUserId = recipientUserId;
        ActorUserId = actorUserId;
        Type = type;
        PlaceId = placeId;
        PlaceReviewId = placeReviewId;
        DishReviewId = dishReviewId;
        FriendRequestId = friendRequestId;
        CreatedAt = DateTime.UtcNow;
        DeduplicationKey = BuildDeduplicationKey(recipientUserId, type, placeReviewId, dishReviewId, friendRequestId);
    }

    public Guid Id { get; private set; }

    public Guid RecipientUserId { get; private set; }

    public Guid ActorUserId { get; private set; }

    public NotificationType Type { get; private set; }

    public Guid? PlaceId { get; private set; }

    public Guid? PlaceReviewId { get; private set; }

    public Guid? DishReviewId { get; private set; }

    public Guid? FriendRequestId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? ReadAt { get; private set; }

    public string DeduplicationKey { get; private set; } = null!;

    public User RecipientUser { get; private set; } = null!;

    public User ActorUser { get; private set; } = null!;

    public Place? Place { get; private set; }

    public PlaceReview? PlaceReview { get; private set; }

    public DishReview? DishReview { get; private set; }

    public FriendRequest? FriendRequest { get; private set; }

    public void MarkAsRead()
    {
        ReadAt ??= DateTime.UtcNow;
    }

    private static string BuildDeduplicationKey(
        Guid recipientUserId,
        NotificationType type,
        Guid? placeReviewId,
        Guid? dishReviewId,
        Guid? friendRequestId)
    {
        var referenceId = type switch
        {
            NotificationType.PlaceReviewAdded => placeReviewId!.Value,
            NotificationType.DishReviewAdded => dishReviewId!.Value,
            NotificationType.FriendRequestSent => friendRequestId!.Value,
            _ => throw new InvalidOperationException("Unsupported notification type.")
        };

        return $"{recipientUserId:N}:{type}:{referenceId:N}";
    }

    private static void EnsureValidReferences(
        NotificationType type,
        Guid? placeId,
        Guid? placeReviewId,
        Guid? dishReviewId,
        Guid? friendRequestId)
    {
        var isValid = type switch
        {
            NotificationType.PlaceReviewAdded =>
                placeId.HasValue
                && placeReviewId.HasValue
                && dishReviewId is null
                && friendRequestId is null,

            NotificationType.DishReviewAdded =>
                placeId.HasValue
                && dishReviewId.HasValue
                && placeReviewId is null
                && friendRequestId is null,

            NotificationType.FriendRequestSent =>
                friendRequestId.HasValue
                && placeId is null
                && placeReviewId is null
                && dishReviewId is null,

            _ => false
        };

        if (!isValid)
        {
            throw new InvalidOperationException("Notification references do not match the notification type.");
        }
    }
}
