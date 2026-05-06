using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Feed;
using TrustMap.Api.Services;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("feed")]
public sealed class FeedController(
    TrustMapDbContext dbContext,
    PrivacyAccessService privacyAccessService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FeedResponseDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FeedResponseDto>> GetFeed(
        [FromQuery] DateTime? beforeUtc,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);
        var friendIds = socialAccess.FriendIds;
        take = Math.Clamp(take, 1, 100);
        var cursor = beforeUtc ?? DateTime.MaxValue;

        var placeReviews = await dbContext.PlaceReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
            .Include(x => x.Photos)
            .VisibleInSocialScope(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .Where(x => x.UpdatedAt < cursor)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var dishReviews = await dbContext.DishReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
            .Include(x => x.Photos)
            .VisibleInSocialScope(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .Where(x => x.UpdatedAt < cursor)
            .OrderByDescending(x => x.UpdatedAt)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var authorAccess = await privacyAccessService.GetAccessMapAsync(
            currentUserId,
            placeReviews.Select(x => x.AuthorUserId).Concat(dishReviews.Select(x => x.AuthorUserId)).ToArray(),
            cancellationToken);

        var items = placeReviews
            .Select(x => new FeedItemDto(
                FeedActivityType.PlaceReview,
                x.Id,
                x.Visibility,
                x.CreatedAt,
                x.UpdatedAt,
                x.AuthorUser.ToSummaryDto(authorAccess.GetValueOrDefault(x.AuthorUserId)?.CanAccess(x.AuthorUser.ProfilePictureVisibility) ?? false),
                x.Place.ToSummaryDto(currentUserId),
                x.Rating,
                x.Body,
                x.Title,
                null,
                x.Photos.OrderBy(photo => photo.CreatedAt).Select(photo => photo.ToDto()).ToArray()))
            .Concat(dishReviews.Select(x => new FeedItemDto(
                FeedActivityType.DishReview,
                x.Id,
                x.Visibility,
                x.CreatedAt,
                x.UpdatedAt,
                x.AuthorUser.ToSummaryDto(authorAccess.GetValueOrDefault(x.AuthorUserId)?.CanAccess(x.AuthorUser.ProfilePictureVisibility) ?? false),
                x.Place.ToSummaryDto(currentUserId),
                x.Rating,
                x.Body,
                null,
                x.DishName,
                x.Photos.OrderBy(photo => photo.CreatedAt).Select(photo => photo.ToDto()).ToArray())))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(take + 1)
            .ToList();

        var nextCursorUtc = items.Count > take ? items.Take(take).Last().UpdatedAtUtc : (DateTime?)null;
        if (items.Count > take)
        {
            items = items.Take(take).ToList();
        }

        return Ok(new FeedResponseDto(items, nextCursorUtc));
    }
}
