using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Features.Notifications;

public sealed record NotificationDto(
    Guid Id,
    NotificationType Type,
    DateTime CreatedAtUtc,
    DateTime? ReadAtUtc,
    UserSummaryDto Actor,
    Guid? PlaceId,
    string? PlaceName,
    Guid? PlaceReviewId,
    Guid? DishReviewId,
    string? DishName,
    Guid? FriendRequestId,
    FriendRequestStatus? FriendRequestStatus,
    int? Rating);

public sealed record NotificationsResponseDto(
    IReadOnlyList<NotificationDto> Items,
    DateTime? NextCursorUtc);

public sealed record UnreadNotificationCountDto(int Count);
