using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("place-reviews")]
public sealed class PlaceReviewsController(
    TrustMapDbContext dbContext,
    PrivacyAccessService privacyAccessService,
    CategoryAssignmentService categoryAssignmentService,
    NotificationService notificationService,
    IPhotoStorage photoStorage,
    ILogger<PlaceReviewsController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlaceReviewDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaceReviewDto>>> GetReviews(
        [FromQuery] Guid? placeId,
        [FromQuery] Guid? authorUserId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);

        take = Math.Clamp(take, 1, 100);

        var query = dbContext.PlaceReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds);

        if (placeId.HasValue)
        {
            query = query.Where(x => x.PlaceId == placeId.Value);
        }

        if (authorUserId.HasValue)
        {
            await EnsureCanViewAuthorReviewsAsync(currentUserId, authorUserId.Value, cancellationToken);
            query = query.Where(x => x.AuthorUserId == authorUserId.Value);
        }

        var reviews = await query
            .OrderByDescending(x => x.UpdatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        var authorAccess = await privacyAccessService.GetAccessMapAsync(
            currentUserId,
            reviews.Select(x => x.AuthorUserId).ToArray(),
            cancellationToken);

        return Ok(reviews
            .Select(x => x.ToDto(
                currentUserId,
                authorAccess.GetValueOrDefault(x.AuthorUserId)?.CanAccess(x.AuthorUser.ProfilePictureVisibility) ?? false))
            .ToArray());
    }

    private async Task EnsureCanViewAuthorReviewsAsync(
        Guid currentUserId,
        Guid authorUserId,
        CancellationToken cancellationToken)
    {
        var canViewReviews = await privacyAccessService.CanViewerAccessUserReviewsAsync(
            currentUserId,
            authorUserId,
            cancellationToken);

        if (canViewReviews == false)
        {
            throw ApiException.Forbidden("This user's reviews are private.", "reviews_private");
        }
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PlaceReviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaceReviewDto>> GetReview(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);

        var review = await dbContext.PlaceReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Place review not found.", "place_review_not_found");

        var authorAccess = await privacyAccessService.GetAccessMapAsync(currentUserId, [review.AuthorUserId], cancellationToken);
        return Ok(review.ToDto(
            currentUserId,
            authorAccess[review.AuthorUserId].CanAccess(review.AuthorUser.ProfilePictureVisibility)));
    }

    [HttpPost]
    [ProducesResponseType<PlaceReviewDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceReviewDto>> CreateReview(
        [FromBody] CreatePlaceReviewRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);
        var place = await dbContext.Places
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .SingleOrDefaultAsync(x => x.Id == request.PlaceId, cancellationToken)
            ?? throw ApiException.NotFound("Place not found.", "place_not_found");

        var exists = await dbContext.PlaceReviews.IgnoreQueryFilters().AnyAsync(
            x => x.PlaceId == request.PlaceId && x.AuthorUserId == currentUserId,
            cancellationToken);

        if (exists)
        {
            throw ApiException.Conflict("Only one place review per user and place is supported.", "place_review_exists");
        }

        var review = new PlaceReview(
            Guid.CreateVersion7(),
            place.Id,
            currentUserId,
            NormalizeReviewVisibility(request.Visibility),
            request.Rating,
            request.Title,
            request.Body);

        dbContext.PlaceReviews.Add(review);
        await categoryAssignmentService.UpsertPlaceAssignmentAsync(currentUserId, place.Id, request.CategoryId, cancellationToken);
        var notifications = await notificationService.CreatePlaceReviewAddedAsync(
            currentUserId,
            place.Id,
            review.Id,
            NormalizeReviewVisibility(request.Visibility),
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notificationService.SendPushNotificationsAsync(notifications, CancellationToken.None);

        var created = await dbContext.PlaceReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .SingleAsync(x => x.Id == review.Id, cancellationToken);

        return Created($"/place-reviews/{review.Id}", created.ToDto(currentUserId, canViewAuthorProfilePicture: true));
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<PlaceReviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaceReviewDto>> UpdateReview(
        Guid id,
        [FromBody] UpdatePlaceReviewRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var review = await dbContext.PlaceReviews
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
            .Include(x => x.Photos)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Place review not found.", "place_review_not_found");

        if (review.AuthorUserId != currentUserId)
        {
            throw ApiException.Forbidden("You can edit only your own place reviews.", "place_review_forbidden");
        }

        var currentCategoryId = await GetAssignedCategoryIdAsync(currentUserId, review.PlaceId, cancellationToken);
        var categoryChanged = currentCategoryId != request.CategoryId;

        if (!categoryChanged && !review.HasChanges(NormalizeReviewVisibility(request.Visibility), request.Rating, request.Title, request.Body))
        {
            return Ok(await LoadReviewDtoAsync(id, currentUserId, cancellationToken));
        }

        var previousVisibility = review.Visibility;
        var reviewFieldsChanged = review.Update(NormalizeReviewVisibility(request.Visibility), request.Rating, request.Title, request.Body);
        if (previousVisibility != VisibilityLevel.Private && NormalizeReviewVisibility(request.Visibility) == VisibilityLevel.Private)
        {
            await notificationService.DeleteReviewNotificationsAsync(placeReviewId: review.Id, cancellationToken: cancellationToken);
        }

        if (categoryChanged)
        {
            await categoryAssignmentService.UpsertPlaceAssignmentAsync(currentUserId, review.PlaceId, request.CategoryId, cancellationToken);
            if (!reviewFieldsChanged)
            {
                review.Touch();
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await LoadReviewDtoAsync(id, currentUserId, cancellationToken));
    }

    private async Task<Guid?> GetAssignedCategoryIdAsync(
        Guid currentUserId,
        Guid placeId,
        CancellationToken cancellationToken)
    {
        return await dbContext.PlaceCategoryAssignments
            .AsNoTracking()
            .Where(x => x.PlaceId == placeId && x.AssignedByUserId == currentUserId)
            .Select(x => (Guid?)x.CategoryId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<PlaceReviewDto> LoadReviewDtoAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.PlaceReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .SingleAsync(x => x.Id == id, cancellationToken);

        return review.ToDto(currentUserId, canViewAuthorProfilePicture: true);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteReview(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var review = await dbContext.PlaceReviews
            .Include(x => x.Photos)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Place review not found.", "place_review_not_found");

        if (review.AuthorUserId != currentUserId)
        {
            throw ApiException.Forbidden("You can delete only your own place reviews.", "place_review_forbidden");
        }

        var storageKeys = review.Photos
            .SelectMany(x => x.EnumerateStorageKeys())
            .Distinct()
            .ToArray();
        var placeId = review.PlaceId;
        await notificationService.DeleteReviewNotificationsAsync(placeReviewId: review.Id, cancellationToken: cancellationToken);
        dbContext.PlaceReviews.Remove(review);
        await dbContext.SaveChangesAsync(cancellationToken);
        await categoryAssignmentService.RemoveAssignmentIfUnusedAsync(currentUserId, placeId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var storageKey in storageKeys)
        {
            try
            {
                await photoStorage.DeleteAsync(storageKey, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to delete photo blob {StorageKey} after place review deletion.", storageKey);
            }
        }

        return NoContent();
    }
    private static VisibilityLevel NormalizeReviewVisibility(VisibilityLevel visibility) =>
        visibility == VisibilityLevel.Private ? VisibilityLevel.Private : VisibilityLevel.Friends;

}
