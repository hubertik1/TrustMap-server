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
[Route("dish-reviews")]
public sealed class DishReviewsController(
    TrustMapDbContext dbContext,
    PrivacyAccessService privacyAccessService,
    CategoryAssignmentService categoryAssignmentService,
    NotificationService notificationService,
    IPhotoStorage photoStorage,
    ILogger<DishReviewsController> logger) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DishReviewDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DishReviewDto>>> GetReviews(
        [FromQuery] Guid? placeId,
        [FromQuery] Guid? placeReviewId,
        [FromQuery] Guid? authorUserId,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);
        take = Math.Clamp(take, 1, 100);

        var query = dbContext.DishReviews
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

        if (placeReviewId.HasValue)
        {
            query = query.Where(x => x.PlaceReviewId == placeReviewId.Value);
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
    [ProducesResponseType<DishReviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DishReviewDto>> GetReview(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);

        var review = await dbContext.DishReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Dish review not found.", "dish_review_not_found");

        var authorAccess = await privacyAccessService.GetAccessMapAsync(currentUserId, [review.AuthorUserId], cancellationToken);
        return Ok(review.ToDto(
            currentUserId,
            authorAccess[review.AuthorUserId].CanAccess(review.AuthorUser.ProfilePictureVisibility)));
    }

    [HttpPost]
    [ProducesResponseType<DishReviewDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DishReviewDto>> CreateReview(
        [FromBody] CreateDishReviewRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        EnsureRestaurantCategory(request.CategoryId);
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);
        var place = await dbContext.Places
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .SingleOrDefaultAsync(x => x.Id == request.PlaceId, cancellationToken)
            ?? throw ApiException.NotFound("Place not found.", "place_not_found");

        var supportsDishReviews = await dbContext.PlaceCategoryAssignments
            .AsNoTracking()
            .AnyAsync(
                x => x.PlaceId == request.PlaceId && x.CategoryId == CategoryAssignmentService.RestaurantsCategoryId,
                cancellationToken);

        if (!supportsDishReviews)
        {
            throw ApiException.BadRequest(
                $"Dish reviews can be created only for places in the {CategoryAssignmentService.RestaurantsCategoryName} category.",
                "dish_review_place_not_restaurant");
        }

        if (request.PlaceReviewId.HasValue)
        {
            var placeReview = await dbContext.PlaceReviews.SingleOrDefaultAsync(x => x.Id == request.PlaceReviewId.Value, cancellationToken)
                ?? throw ApiException.NotFound("Parent place review not found.", "place_review_not_found");

            if (placeReview.AuthorUserId != currentUserId || placeReview.PlaceId != request.PlaceId)
            {
                throw ApiException.BadRequest("Dish review can only reference your own place review for the same place.", "dish_review_invalid_parent");
            }
        }

        var review = new DishReview(
            Guid.CreateVersion7(),
            place.Id,
            currentUserId,
            request.PlaceReviewId,
            request.Visibility,
            request.DishName,
            request.Rating,
            request.Body,
            request.PriceAmount,
            request.CurrencyCode);

        dbContext.DishReviews.Add(review);
        await categoryAssignmentService.UpsertPlaceAssignmentAsync(currentUserId, place.Id, request.CategoryId, cancellationToken);
        var notifications = await notificationService.CreateDishReviewAddedAsync(
            currentUserId,
            place.Id,
            review.Id,
            request.Visibility,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await notificationService.SendPushNotificationsAsync(notifications, CancellationToken.None);

        var created = await dbContext.DishReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .SingleAsync(x => x.Id == review.Id, cancellationToken);

        return Created($"/dish-reviews/{review.Id}", created.ToDto(currentUserId, canViewAuthorProfilePicture: true));
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<DishReviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DishReviewDto>> UpdateReview(
        Guid id,
        [FromBody] UpdateDishReviewRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        EnsureRestaurantCategory(request.CategoryId);
        var review = await dbContext.DishReviews
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
            .Include(x => x.Photos)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Dish review not found.", "dish_review_not_found");

        if (review.AuthorUserId != currentUserId)
        {
            throw ApiException.Forbidden("You can edit only your own dish reviews.", "dish_review_forbidden");
        }

        var currentCategoryId = await GetAssignedCategoryIdAsync(currentUserId, review.PlaceId, cancellationToken);
        var categoryChanged = currentCategoryId != request.CategoryId;

        if (!categoryChanged
            && !review.HasChanges(
                request.Visibility,
                request.DishName,
                request.Rating,
                request.Body,
                request.PriceAmount,
                request.CurrencyCode))
        {
            return Ok(await LoadReviewDtoAsync(id, currentUserId, cancellationToken));
        }

        var previousVisibility = review.Visibility;
        var reviewFieldsChanged = review.Update(request.Visibility, request.DishName, request.Rating, request.Body, request.PriceAmount, request.CurrencyCode);
        if (previousVisibility != VisibilityLevel.Private && request.Visibility == VisibilityLevel.Private)
        {
            await notificationService.DeleteReviewNotificationsAsync(dishReviewId: review.Id, cancellationToken: cancellationToken);
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

    private async Task<DishReviewDto> LoadReviewDtoAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var review = await dbContext.DishReviews
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
        var review = await dbContext.DishReviews
            .Include(x => x.Photos)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Dish review not found.", "dish_review_not_found");

        if (review.AuthorUserId != currentUserId)
        {
            throw ApiException.Forbidden("You can delete only your own dish reviews.", "dish_review_forbidden");
        }

        var storageKeys = review.Photos
            .SelectMany(x => x.EnumerateStorageKeys())
            .Distinct()
            .ToArray();
        var placeId = review.PlaceId;
        await notificationService.DeleteReviewNotificationsAsync(dishReviewId: review.Id, cancellationToken: cancellationToken);
        dbContext.DishReviews.Remove(review);
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
                logger.LogWarning(exception, "Failed to delete photo blob {StorageKey} after dish review deletion.", storageKey);
            }
        }

        return NoContent();
    }

    private static void EnsureRestaurantCategory(Guid categoryId)
    {
        if (categoryId == CategoryAssignmentService.RestaurantsCategoryId)
        {
            return;
        }

        throw ApiException.BadRequest(
            $"Dish reviews can be created only in the {CategoryAssignmentService.RestaurantsCategoryName} category.",
            "dish_review_category_not_supported");
    }
}
