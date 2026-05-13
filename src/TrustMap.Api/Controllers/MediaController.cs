using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("media")]
public sealed class MediaController(
    TrustMapDbContext dbContext,
    PrivacyAccessService privacyAccessService,
    IPhotoStorage photoStorage,
    IOptions<PhotoStorageOptions> photoStorageOptionsAccessor) : ControllerBase
{
    private readonly PhotoStorageOptions _photoStorageOptions = photoStorageOptionsAccessor.Value;

    [HttpGet("photos/{photoId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetPhoto(Guid photoId, CancellationToken cancellationToken) =>
        GetPhotoVariant(photoId, "original", cancellationToken);

    [HttpGet("photos/{photoId:guid}/variants/{variantName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhotoVariant(
        Guid photoId,
        string variantName,
        CancellationToken cancellationToken)
    {
        var normalizedVariant = NormalizeVariantName(variantName);
        if (normalizedVariant is null)
        {
            return NotFound();
        }

        var currentUserId = User.GetRequiredUserId();
        var photo = await dbContext.Photos
            .AsNoTracking()
            .Include(x => x.PlaceReview)
            .Include(x => x.DishReview)
            .SingleOrDefaultAsync(x => x.Id == photoId, cancellationToken);

        if (photo is null || !await CanAccessPhotoAsync(photo, currentUserId, cancellationToken))
        {
            return NotFound();
        }

        var storageKey = GetPhotoStorageKey(photo, normalizedVariant);
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            return NotFound();
        }

        return await OpenStoredMediaAsync(storageKey, photo.ContentType, cancellationToken);
    }

    [HttpGet("users/{userId:guid}/avatar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> GetAvatar(Guid userId, CancellationToken cancellationToken) =>
        GetAvatarVariant(userId, "original", cancellationToken);

    [HttpGet("users/{userId:guid}/avatar/variants/{variantName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvatarVariant(
        Guid userId,
        string variantName,
        CancellationToken cancellationToken)
    {
        var normalizedVariant = NormalizeVariantName(variantName);
        if (normalizedVariant is null)
        {
            return NotFound();
        }

        var currentUserId = User.GetRequiredUserId();
        var user = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == userId)
            .Select(x => new
            {
                x.Id,
                x.AvatarUrl,
                x.ProfilePictureVisibility
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (user is null
            || !await privacyAccessService.CanViewerAccessAsync(
                currentUserId,
                user.Id,
                user.ProfilePictureVisibility,
                cancellationToken)
            || !PhotoStorageUrl.TryResolveStorageKey(
                user.AvatarUrl,
                _photoStorageOptions.RequestPath,
                out var originalStorageKey))
        {
            return NotFound();
        }

        var storageKey = GetDerivedVariantStorageKey(originalStorageKey, normalizedVariant);
        return await OpenStoredMediaAsync(storageKey, contentTypeHint: null, cancellationToken);
    }

    private async Task<bool> CanAccessPhotoAsync(
        Photo photo,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var reviewAccess = GetReviewAccess(photo);
        if (reviewAccess is null)
        {
            return false;
        }

        return await privacyAccessService.CanViewerAccessAsync(
            currentUserId,
            reviewAccess.AuthorUserId,
            reviewAccess.Visibility,
            cancellationToken);
    }

    private static ReviewAccess? GetReviewAccess(Photo photo)
    {
        if (photo.PlaceReview is not null)
        {
            return new ReviewAccess(photo.PlaceReview.AuthorUserId, photo.PlaceReview.Visibility);
        }

        if (photo.DishReview is not null)
        {
            return new ReviewAccess(photo.DishReview.AuthorUserId, photo.DishReview.Visibility);
        }

        return null;
    }

    private async Task<IActionResult> OpenStoredMediaAsync(
        string storageKey,
        string? contentTypeHint,
        CancellationToken cancellationToken)
    {
        StoredFileResult? storedFile;
        try
        {
            storedFile = await photoStorage.OpenReadAsync(storageKey, contentTypeHint, cancellationToken);
        }
        catch (PhotoStorageException)
        {
            return NotFound();
        }

        if (storedFile is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private,max-age=300";
        Response.Headers.Vary = "Authorization";
        Response.ContentLength = storedFile.Length;

        return File(storedFile.Stream, storedFile.ContentType);
    }

    private static string? GetPhotoStorageKey(Photo photo, string variantName) =>
        variantName switch
        {
            "original" => photo.StorageKey,
            "medium" => photo.MediumStorageKey,
            "thumbnail" => photo.ThumbnailStorageKey,
            _ => null
        };

    private static string GetDerivedVariantStorageKey(string originalStorageKey, string variantName)
    {
        if (variantName == "original")
        {
            return originalStorageKey;
        }

        var extension = Path.GetExtension(originalStorageKey);
        var basePath = extension.Length == 0
            ? originalStorageKey
            : originalStorageKey[..^extension.Length];
        var variantExtension = extension.Length == 0 ? ".jpg" : extension;
        var suffix = variantName == "thumbnail" ? "thumb" : variantName;
        return $"{basePath}-{suffix}{variantExtension}";
    }

    private static string? NormalizeVariantName(string variantName)
    {
        var normalized = variantName.Trim().ToLowerInvariant();
        return normalized is "original" or "medium" or "thumbnail"
            ? normalized
            : null;
    }

    private sealed record ReviewAccess(Guid AuthorUserId, VisibilityLevel Visibility);
}
