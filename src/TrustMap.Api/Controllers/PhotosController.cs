using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Quotas;
using TrustMap.Api.RateLimiting;
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
    IQuotaService quotaService,
    ILogger<PhotosController> logger,
    IOptions<PhotoStorageOptions> photoStorageOptionsAccessor,
    IOptions<QuotaOptions> quotaOptionsAccessor) : ControllerBase
{
    private readonly PhotoStorageOptions _photoStorageOptions = photoStorageOptionsAccessor.Value;
    private readonly QuotaOptions _quotaOptions = quotaOptionsAccessor.Value;

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(RateLimitPolicyNames.Upload)]
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

        EnsureValidUploadFile(request.File);

        var currentUserId = User.GetRequiredUserId();

        Guid placeId;
        Guid? placeReviewId = null;
        Guid? dishReviewId = null;
        PlaceReview? placeReview = null;
        DishReview? dishReview = null;

        if (request.PlaceReviewId.HasValue)
        {
            placeReview = await dbContext.PlaceReviews.SingleOrDefaultAsync(x => x.Id == request.PlaceReviewId.Value, cancellationToken)
                ?? throw ApiException.NotFound("Place review not found.", "place_review_not_found");

            if (placeReview.AuthorUserId != currentUserId)
            {
                throw ApiException.Forbidden("You can upload photos only to your own place review.", "photo_forbidden");
            }

            placeId = placeReview.PlaceId;
            placeReviewId = placeReview.Id;
        }
        else
        {
            dishReview = await dbContext.DishReviews.SingleOrDefaultAsync(x => x.Id == request.DishReviewId!.Value, cancellationToken)
                ?? throw ApiException.NotFound("Dish review not found.", "dish_review_not_found");

            if (dishReview.AuthorUserId != currentUserId)
            {
                throw ApiException.Forbidden("You can upload photos only to your own dish review.", "photo_forbidden");
            }

            placeId = dishReview.PlaceId;
            dishReviewId = dishReview.Id;
        }

        var quotaConsumptions = BuildPhotoUploadQuotaConsumptions(currentUserId, request.File.Length);
        var quotaPrecheck = await quotaService.CheckAsync(quotaConsumptions, cancellationToken);
        QuotaRejection.ThrowIfRejected(HttpContext, quotaPrecheck);

        await using var stream = request.File.OpenReadStream();
        var storedPhoto = await photoStorage.SaveAsync(stream, cancellationToken);

        var photo = new Photo(
            Guid.CreateVersion7(),
            currentUserId,
            placeId,
            placeReviewId,
            dishReviewId,
            storedPhoto.StorageProvider,
            storedPhoto.StorageKey,
            storedPhoto.PublicUrl,
            storedPhoto.Medium?.StorageKey,
            storedPhoto.Medium?.PublicUrl,
            storedPhoto.Thumbnail?.StorageKey,
            storedPhoto.Thumbnail?.PublicUrl,
            GetStoredFileName(storedPhoto.StorageKey),
            storedPhoto.ContentType,
            storedPhoto.SizeBytes,
            storedPhoto.Width,
            storedPhoto.Height);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var quotaResult = await quotaService.CheckAndConsumeAsync(quotaConsumptions, cancellationToken);
            QuotaRejection.ThrowIfRejected(HttpContext, quotaResult);

            dbContext.Photos.Add(photo);
            placeReview?.Touch();
            dishReview?.Touch();
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await DeleteStoredPhotoAsync(storedPhoto, cancellationToken);
            throw;
        }

        logger.LogInformation(
            "Accepted photo upload for user {UserId} with {UploadBytes} bytes.",
            currentUserId,
            request.File.Length);
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
        if (photo.PlaceReviewId.HasValue)
        {
            var review = await dbContext.PlaceReviews.SingleOrDefaultAsync(x => x.Id == photo.PlaceReviewId.Value, cancellationToken);
            review?.Touch();
        }
        else if (photo.DishReviewId.HasValue)
        {
            var review = await dbContext.DishReviews.SingleOrDefaultAsync(x => x.Id == photo.DishReviewId.Value, cancellationToken);
            review?.Touch();
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var storageKey in photo.EnumerateStorageKeys())
        {
            try
            {
                await photoStorage.DeleteAsync(storageKey, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to delete photo blob {StorageKey} after metadata deletion for photo {PhotoId}.", storageKey, photo.Id);
            }
        }

        return NoContent();
    }

    private void EnsureValidUploadFile(IFormFile? file)
    {
        if (file is null)
        {
            throw ApiException.BadRequest("Attach an image file in the 'file' form field.", "photo_file_missing");
        }

        if (file.Length <= 0)
        {
            throw ApiException.BadRequest("Attach a non-empty image file.", "photo_empty");
        }

        var maxUploadBytes = _photoStorageOptions.MaxUploadBytes > 0
            ? _photoStorageOptions.MaxUploadBytes
            : PhotoStorageOptions.DefaultMaxUploadBytes;
        if (file.Length > maxUploadBytes)
        {
            throw ApiException.BadRequest(
                $"Uploaded image must be {maxUploadBytes} bytes or smaller.",
                "photo_too_large");
        }
    }

    private async Task DeleteStoredPhotoAsync(StoredPhoto storedPhoto, CancellationToken cancellationToken)
    {
        foreach (var storageKey in EnumerateStorageKeys(storedPhoto))
        {
            try
            {
                await photoStorage.DeleteAsync(storageKey, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to roll back uploaded photo blob {StorageKey}.", storageKey);
            }
        }
    }

    private static IEnumerable<string> EnumerateStorageKeys(StoredPhoto storedPhoto)
    {
        yield return storedPhoto.StorageKey;

        if (!string.IsNullOrWhiteSpace(storedPhoto.Medium?.StorageKey))
        {
            yield return storedPhoto.Medium.StorageKey;
        }

        if (!string.IsNullOrWhiteSpace(storedPhoto.Thumbnail?.StorageKey))
        {
            yield return storedPhoto.Thumbnail.StorageKey;
        }
    }

    private static string GetStoredFileName(string storageKey)
    {
        var slashIndex = storageKey.LastIndexOf('/');
        return slashIndex < 0
            ? storageKey
            : storageKey[(slashIndex + 1)..];
    }

    private IReadOnlyCollection<QuotaConsumption> BuildPhotoUploadQuotaConsumptions(Guid userId, long uploadBytes) =>
    [
        new(
            QuotaSubjectKeys.UserScope,
            QuotaSubjectKeys.User(userId),
            QuotaNames.UploadPhotosPerDay,
            QuotaPeriodKind.Day,
            CountCost: 1,
            ByteCost: 0,
            CountLimit: _quotaOptions.Uploads.MaxPhotosPerUserPerDay,
            ByteLimit: null),
        new(
            QuotaSubjectKeys.UserScope,
            QuotaSubjectKeys.User(userId),
            QuotaNames.UploadBytesPerDay,
            QuotaPeriodKind.Day,
            CountCost: 0,
            ByteCost: uploadBytes,
            CountLimit: null,
            ByteLimit: _quotaOptions.Uploads.MaxBytesPerUserPerDay)
    ];
}
