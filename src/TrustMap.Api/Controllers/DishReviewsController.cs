using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("dish-reviews")]
public sealed class DishReviewsController(
    TrustMapDbContext dbContext,
    SocialGraphService socialGraphService,
    CategoryAssignmentService categoryAssignmentService,
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
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);
        take = Math.Clamp(take, 1, 100);

        var query = dbContext.DishReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .VisibleToViewer(currentUserId, friendIds);

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
            query = query.Where(x => x.AuthorUserId == authorUserId.Value);
        }

        var reviews = await query
            .OrderByDescending(x => x.UpdatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(reviews.Select(x => x.ToDto(currentUserId)).ToArray());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DishReviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DishReviewDto>> GetReview(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);

        var review = await dbContext.DishReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .VisibleToViewer(currentUserId, friendIds)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Dish review not found.", "dish_review_not_found");

        return Ok(review.ToDto(currentUserId));
    }

    [HttpPost]
    [ProducesResponseType<DishReviewDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<DishReviewDto>> CreateReview(
        [FromBody] CreateDishReviewRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var place = await dbContext.Places.SingleOrDefaultAsync(x => x.Id == request.PlaceId, cancellationToken)
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
        await dbContext.SaveChangesAsync(cancellationToken);

        var created = await dbContext.DishReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .SingleAsync(x => x.Id == review.Id, cancellationToken);

        return Created($"/dish-reviews/{review.Id}", created.ToDto(currentUserId));
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<DishReviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DishReviewDto>> UpdateReview(
        Guid id,
        [FromBody] UpdateDishReviewRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
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

        review.Update(request.Visibility, request.DishName, request.Rating, request.Body, request.PriceAmount, request.CurrencyCode);
        await categoryAssignmentService.UpsertPlaceAssignmentAsync(currentUserId, review.PlaceId, request.CategoryId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.DishReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .SingleAsync(x => x.Id == id, cancellationToken);

        return Ok(updated.ToDto(currentUserId));
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
}
