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
[Route("place-reviews")]
public sealed class PlaceReviewsController(
    TrustMapDbContext dbContext,
    SocialGraphService socialGraphService,
    CategoryAssignmentService categoryAssignmentService,
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
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);

        take = Math.Clamp(take, 1, 100);

        var query = dbContext.PlaceReviews
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
    [ProducesResponseType<PlaceReviewDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaceReviewDto>> GetReview(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);

        var review = await dbContext.PlaceReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .VisibleToViewer(currentUserId, friendIds)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Place review not found.", "place_review_not_found");

        return Ok(review.ToDto(currentUserId));
    }

    [HttpPost]
    [ProducesResponseType<PlaceReviewDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceReviewDto>> CreateReview(
        [FromBody] CreatePlaceReviewRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var place = await dbContext.Places.SingleOrDefaultAsync(x => x.Id == request.PlaceId, cancellationToken)
            ?? throw ApiException.NotFound("Place not found.", "place_not_found");

        var exists = await dbContext.PlaceReviews.AnyAsync(
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
            request.Visibility,
            request.Rating,
            request.Title,
            request.Body);

        dbContext.PlaceReviews.Add(review);
        await categoryAssignmentService.UpsertPlaceAssignmentAsync(currentUserId, place.Id, request.CategoryId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var created = await dbContext.PlaceReviews
            .AsNoTracking()
            .Include(x => x.AuthorUser)
            .Include(x => x.Place)
                .ThenInclude(x => x.CategoryAssignments)
                .ThenInclude(x => x.Category)
            .Include(x => x.Photos)
            .SingleAsync(x => x.Id == review.Id, cancellationToken);

        return Created($"/place-reviews/{review.Id}", created.ToDto(currentUserId));
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

        review.Update(request.Visibility, request.Rating, request.Title, request.Body);
        await categoryAssignmentService.UpsertPlaceAssignmentAsync(currentUserId, review.PlaceId, request.CategoryId, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.PlaceReviews
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
}
