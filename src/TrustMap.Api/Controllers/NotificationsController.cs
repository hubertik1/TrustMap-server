using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Notifications;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("notifications")]
public sealed class NotificationsController(
    TrustMapDbContext dbContext,
    PrivacyAccessService privacyAccessService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<NotificationsResponseDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NotificationsResponseDto>> GetNotifications(
        [FromQuery] DateTime? beforeUtc,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);
        take = Math.Clamp(take, 1, 100);
        var cursor = beforeUtc ?? DateTime.MaxValue;

        var notifications = await VisibleToCurrentUser(
                dbContext.Notifications.AsNoTracking(),
                currentUserId,
                socialAccess)
            .Include(x => x.ActorUser)
            .Include(x => x.Place)
            .Include(x => x.PlaceReview)
            .Include(x => x.DishReview)
            .Include(x => x.FriendRequest)
            .Where(x => x.CreatedAt < cursor)
            .OrderByDescending(x => x.CreatedAt)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var actorAccess = await privacyAccessService.GetAccessMapAsync(
            currentUserId,
            notifications.Select(x => x.ActorUserId).ToArray(),
            cancellationToken);

        var items = notifications
            .Take(take)
            .Select(x => ToDto(x, actorAccess))
            .ToArray();
        var nextCursorUtc = notifications.Count > take ? items[^1].CreatedAtUtc : (DateTime?)null;

        return Ok(new NotificationsResponseDto(items, nextCursorUtc));
    }

    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadNotificationCountDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadNotificationCountDto>> GetUnreadCount(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);

        var count = await VisibleToCurrentUser(
                dbContext.Notifications.AsNoTracking(),
                currentUserId,
                socialAccess)
            .CountAsync(x => x.ReadAt == null, cancellationToken);

        return Ok(new UnreadNotificationCountDto(count));
    }

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var notification = await dbContext.Notifications
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Notification not found.", "notification_not_found");

        if (notification.RecipientUserId != currentUserId)
        {
            throw ApiException.Forbidden("You can only mark your own notifications as read.", "notification_forbidden");
        }

        notification.MarkAsRead();
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPatch("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);

        var notifications = await VisibleToCurrentUser(
                dbContext.Notifications,
                currentUserId,
                socialAccess)
            .Where(x => x.ReadAt == null)
            .ToListAsync(cancellationToken);

        foreach (var notification in notifications)
        {
            notification.MarkAsRead();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static IQueryable<UserNotification> VisibleToCurrentUser(
        IQueryable<UserNotification> query,
        Guid currentUserId,
        ViewerSocialAccess socialAccess)
    {
        var friendIds = socialAccess.FriendIds;
        var friendOfFriendIds = socialAccess.FriendOfFriendIds;

        return query.Where(x =>
            x.RecipientUserId == currentUserId
            && x.RecipientUser.Status == UserAccountStatus.Active
            && x.ActorUser.Status == UserAccountStatus.Active
            && (
                (x.Type == NotificationType.FriendRequestSent
                    && x.FriendRequest != null)
                || (x.Type == NotificationType.PlaceReviewAdded
                    && x.PlaceReview != null
                    && (
                        x.PlaceReview.AuthorUserId == currentUserId
                        || x.PlaceReview.Visibility == VisibilityLevel.Public
                        || (x.PlaceReview.Visibility == VisibilityLevel.Friends
                            && friendIds.Contains(x.PlaceReview.AuthorUserId))
                        || (x.PlaceReview.Visibility == VisibilityLevel.FriendsOfFriends
                            && (friendIds.Contains(x.PlaceReview.AuthorUserId)
                                || friendOfFriendIds.Contains(x.PlaceReview.AuthorUserId)))))
                || (x.Type == NotificationType.DishReviewAdded
                    && x.DishReview != null
                    && (
                        x.DishReview.AuthorUserId == currentUserId
                        || x.DishReview.Visibility == VisibilityLevel.Public
                        || (x.DishReview.Visibility == VisibilityLevel.Friends
                            && friendIds.Contains(x.DishReview.AuthorUserId))
                        || (x.DishReview.Visibility == VisibilityLevel.FriendsOfFriends
                            && (friendIds.Contains(x.DishReview.AuthorUserId)
                                || friendOfFriendIds.Contains(x.DishReview.AuthorUserId)))))
            ));
    }

    private static NotificationDto ToDto(
        UserNotification notification,
        IReadOnlyDictionary<Guid, ViewerAccess> actorAccess)
    {
        var actorCanViewProfilePicture =
            actorAccess.GetValueOrDefault(notification.ActorUserId)?.CanAccess(notification.ActorUser.ProfilePictureVisibility) ?? false;

        return new NotificationDto(
            notification.Id,
            notification.Type,
            notification.CreatedAt,
            notification.ReadAt,
            notification.ActorUser.ToSummaryDto(actorCanViewProfilePicture),
            notification.PlaceId,
            notification.Place?.DisplayName,
            notification.PlaceReviewId,
            notification.DishReviewId,
            notification.DishReview?.DishName,
            notification.FriendRequestId,
            notification.FriendRequest?.Status,
            notification.Type switch
            {
                NotificationType.PlaceReviewAdded => notification.PlaceReview?.Rating,
                NotificationType.DishReviewAdded => notification.DishReview?.Rating,
                _ => null
            });
    }
}
