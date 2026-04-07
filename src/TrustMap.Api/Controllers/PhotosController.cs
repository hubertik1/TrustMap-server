using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Reviews;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("photos")]
public sealed class PhotosController(
    TrustMapDbContext dbContext,
    IPhotoStorage photoStorage,
    ILogger<PhotosController> logger) : ControllerBase
{
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<PhotoDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PhotoDto>> Upload(
        [FromForm] UploadPhotoRequest request,
        CancellationToken cancellationToken)
    {
        if ((request.PlaceReviewId.HasValue && request.DishReviewId.HasValue)
            || (!request.PlaceReviewId.HasValue && !request.DishReviewId.HasValue))
        {
            throw ApiException.BadRequest("Provide exactly one review parent for the uploaded photo.", "photo_invalid_parent");
        }

        var currentUserId = User.GetRequiredUserId();

        Guid placeId;
        Guid? placeReviewId = null;
        Guid? dishReviewId = null;

        if (request.PlaceReviewId.HasValue)
        {
            var review = await dbContext.PlaceReviews.SingleOrDefaultAsync(x => x.Id == request.PlaceReviewId.Value, cancellationToken)
                ?? throw ApiException.NotFound("Place review not found.", "place_review_not_found");

            if (review.AuthorUserId != currentUserId)
            {
                throw ApiException.Forbidden("You can upload photos only to your own place review.", "photo_forbidden");
            }

            placeId = review.PlaceId;
            placeReviewId = review.Id;
        }
        else
        {
            var review = await dbContext.DishReviews.SingleOrDefaultAsync(x => x.Id == request.DishReviewId!.Value, cancellationToken)
                ?? throw ApiException.NotFound("Dish review not found.", "dish_review_not_found");

            if (review.AuthorUserId != currentUserId)
            {
                throw ApiException.Forbidden("You can upload photos only to your own dish review.", "photo_forbidden");
            }

            placeId = review.PlaceId;
            dishReviewId = review.Id;
        }

        await using var stream = request.File.OpenReadStream();
        var storedPhoto = await photoStorage.SaveAsync(
            stream,
            request.File.FileName,
            request.File.ContentType,
            cancellationToken);

        var photo = new Photo(
            Guid.CreateVersion7(),
            currentUserId,
            placeId,
            placeReviewId,
            dishReviewId,
            storedPhoto.StorageProvider,
            storedPhoto.StorageKey,
            storedPhoto.PublicUrl,
            request.File.FileName,
            storedPhoto.ContentType,
            storedPhoto.SizeBytes,
            storedPhoto.Width,
            storedPhoto.Height);

        dbContext.Photos.Add(photo);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await photoStorage.DeleteAsync(storedPhoto.StorageKey, cancellationToken);
            throw;
        }

        return Created($"/photos/{photo.Id}", photo.ToDto());
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var photo = await dbContext.Photos.SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Photo not found.", "photo_not_found");

        if (photo.OwnerUserId != currentUserId)
        {
            throw ApiException.Forbidden("You can delete only your own photos.", "photo_forbidden");
        }

        dbContext.Photos.Remove(photo);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await photoStorage.DeleteAsync(photo.StorageKey, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to delete photo blob {StorageKey} after metadata deletion.", photo.StorageKey);
        }

        return NoContent();
    }
}
