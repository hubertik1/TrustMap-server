using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Users;
using TrustMap.Api.Quotas;
using TrustMap.Api.RateLimiting;
using TrustMap.Api.Services;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicyNames.AuthenticatedUser)]
[Route("me")]
public sealed class MeController(
    TrustMapDbContext dbContext,
    HandleGenerator handleGenerator,
    ProfileQueryService profileQueryService,
    IAccountDeletionService accountDeletionService,
    IPhotoStorage photoStorage,
    IQuotaService quotaService,
    ILogger<MeController> logger,
    IOptions<PhotoStorageOptions> photoStorageOptionsAccessor,
    IOptions<QuotaOptions> quotaOptionsAccessor) : ControllerBase
{
    private readonly PhotoStorageOptions _photoStorageOptions = photoStorageOptionsAccessor.Value;
    private readonly QuotaOptions _quotaOptions = quotaOptionsAccessor.Value;

    [HttpGet]
    [Authorize]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public Task<UserProfileDto> Get(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        return profileQueryService.GetProfileAsync(currentUserId, currentUserId, cancellationToken);
    }

    [HttpPatch]
    [Authorize]
    [Consumes("application/json")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserProfileDto>> PatchJson(
        [FromBody] UpdateMeRequest request,
        CancellationToken cancellationToken) =>
        PatchInternal(request, cancellationToken);

    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteMe(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        await accountDeletionService.DeleteAccountAsync(
            currentUserId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return NoContent();
    }

    [HttpPut("avatar")]
    [Authorize]
    [Consumes("multipart/form-data")]
    [EnableRateLimiting(RateLimitPolicyNames.Upload)]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserProfileDto>> PutAvatar(
        [FromForm] UpdateAvatarRequest request,
        CancellationToken cancellationToken)
    {
        EnsureValidUploadFile(request.Avatar, "avatar");

        var currentUserId = User.GetRequiredUserId();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == currentUserId, cancellationToken)
            ?? throw ApiException.NotFound("User not found.", "user_not_found");

        var previousAvatarUrl = user.AvatarUrl;
        StoredPhoto? replacementAvatar = null;
        var quotaConsumptions = BuildAvatarUploadQuotaConsumptions(currentUserId, request.Avatar.Length);
        var quotaPrecheck = await quotaService.CheckAsync(quotaConsumptions, cancellationToken);
        QuotaRejection.ThrowIfRejected(HttpContext, quotaPrecheck);

        try
        {
            await using var stream = request.Avatar.OpenReadStream();
            replacementAvatar = await photoStorage.SaveAsync(stream, cancellationToken);

            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var quotaResult = await quotaService.CheckAndConsumeAsync(quotaConsumptions, cancellationToken);
            QuotaRejection.ThrowIfRejected(HttpContext, quotaResult);

            user.UpdateProfile(user.Handle, user.DisplayName, user.Bio, replacementAvatar.PublicUrl);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (replacementAvatar is not null)
            {
                await DeleteStoredPhotoAsync(replacementAvatar, currentUserId, cancellationToken);
            }

            throw;
        }

        if (!string.Equals(previousAvatarUrl, user.AvatarUrl, StringComparison.OrdinalIgnoreCase))
        {
            await DeleteAvatarAtUrlAsync(previousAvatarUrl, cancellationToken);
        }

        logger.LogInformation(
            "Accepted avatar upload for user {UserId} with {UploadBytes} bytes.",
            currentUserId,
            request.Avatar.Length);
        var profile = await profileQueryService.GetProfileAsync(currentUserId, currentUserId, cancellationToken);
        return Ok(profile);
    }

    [HttpDelete("avatar")]
    [Authorize]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserProfileDto>> DeleteAvatar(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == currentUserId, cancellationToken)
            ?? throw ApiException.NotFound("User not found.", "user_not_found");

        var previousAvatarUrl = user.AvatarUrl;
        user.UpdateProfile(user.Handle, user.DisplayName, user.Bio, avatarUrl: null);
        await dbContext.SaveChangesAsync(cancellationToken);
        await DeleteAvatarAtUrlAsync(previousAvatarUrl, cancellationToken);

        var profile = await profileQueryService.GetProfileAsync(currentUserId, currentUserId, cancellationToken);
        return Ok(profile);
    }

    [HttpPatch("privacy")]
    [Authorize]
    [Consumes("application/json")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<UserProfileDto>> PatchPrivacy(
        [FromBody] UpdateMePrivacyRequest request,
        CancellationToken cancellationToken)
    {
        EnsureValidPrivacyRequest(request);

        var currentUserId = User.GetRequiredUserId();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == currentUserId, cancellationToken)
            ?? throw ApiException.NotFound("User not found.", "user_not_found");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var previousReviewVisibility = user.ReviewVisibility;
        user.UpdatePrivacySettings(
            request.ReviewVisibility,
            request.FriendListVisibility,
            request.ProfileVisibility,
            request.ProfilePictureVisibility);

        if (previousReviewVisibility != VisibilityLevel.Friends)
        {
            var placeReviews = await dbContext.PlaceReviews
                .Where(x => x.AuthorUserId == currentUserId && x.Visibility != VisibilityLevel.Private)
                .ToListAsync(cancellationToken);

            foreach (var review in placeReviews)
            {
                review.Update(VisibilityLevel.Friends, review.Rating, review.Title, review.Body);
            }

            var dishReviews = await dbContext.DishReviews
                .Where(x => x.AuthorUserId == currentUserId && x.Visibility != VisibilityLevel.Private)
                .ToListAsync(cancellationToken);

            foreach (var review in dishReviews)
            {
                review.Update(
                    VisibilityLevel.Friends,
                    review.DishName,
                    review.Rating,
                    review.Body,
                    review.PriceAmount,
                    review.CurrencyCode);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var profile = await profileQueryService.GetProfileAsync(currentUserId, currentUserId, cancellationToken);
        return Ok(profile);
    }

    private static void EnsureValidPrivacyRequest(UpdateMePrivacyRequest request)
    {
        EnsureDefinedVisibility(request.ReviewVisibility, "Review visibility");
        EnsureDefinedVisibility(request.FriendListVisibility, "Friend list visibility");
        EnsureDefinedVisibility(request.ProfileVisibility, "Profile visibility");
        EnsureDefinedVisibility(request.ProfilePictureVisibility, "Profile picture visibility");

        if (request.ReviewVisibility == VisibilityLevel.Private)
        {
            throw ApiException.BadRequest("Review visibility cannot be private.", "review_visibility_private");
        }

        if (request.ProfileVisibility == VisibilityLevel.Private)
        {
            throw ApiException.BadRequest("Profile visibility cannot be private.", "profile_visibility_private");
        }

        if (request.ProfilePictureVisibility == VisibilityLevel.Private)
        {
            throw ApiException.BadRequest("Profile picture visibility cannot be private.", "profile_picture_visibility_private");
        }
    }

    private static void EnsureDefinedVisibility(VisibilityLevel visibility, string displayName)
    {
        if (!Enum.IsDefined(visibility))
        {
            throw ApiException.BadRequest($"{displayName} is invalid.", "invalid_visibility");
        }
    }

    private async Task<ActionResult<UserProfileDto>> PatchInternal(
        UpdateMeRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == currentUserId, cancellationToken)
            ?? throw ApiException.NotFound("User not found.", "user_not_found");

        var handle = await handleGenerator.GenerateUpdatedHandleAsync(
            request.Handle,
            user.Handle,
            request.DisplayName,
            user.Email,
            currentUserId,
            cancellationToken);
        user.UpdateProfile(handle, request.DisplayName, request.Bio, user.AvatarUrl);
        await dbContext.SaveChangesAsync(cancellationToken);

        var profile = await profileQueryService.GetProfileAsync(currentUserId, currentUserId, cancellationToken);
        return Ok(profile);
    }

    private async Task DeleteStoredPhotoAsync(
        StoredPhoto storedPhoto,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await photoStorage.DeleteAsync(storedPhoto.StorageKey, cancellationToken);

            if (!string.IsNullOrWhiteSpace(storedPhoto.Medium?.StorageKey))
            {
                await photoStorage.DeleteAsync(storedPhoto.Medium.StorageKey, cancellationToken);
            }

            if (!string.IsNullOrWhiteSpace(storedPhoto.Thumbnail?.StorageKey))
            {
                await photoStorage.DeleteAsync(storedPhoto.Thumbnail.StorageKey, cancellationToken);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to roll back avatar upload for user {UserId}.", userId);
        }
    }

    private async Task DeleteAvatarAtUrlAsync(string? avatarUrl, CancellationToken cancellationToken)
    {
        if (!TryResolveStorageKey(avatarUrl, out var storageKey))
        {
            return;
        }

        foreach (var key in EnumerateAvatarStorageKeys(storageKey))
        {
            try
            {
                await photoStorage.DeleteAsync(key, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to delete avatar blob {StorageKey}.", key);
            }
        }
    }

    private bool TryResolveStorageKey(string? avatarUrl, out string storageKey)
        => PhotoStorageUrl.TryResolveStorageKey(avatarUrl, _photoStorageOptions.RequestPath, out storageKey);

    private void EnsureValidUploadFile(IFormFile? file, string fieldName)
    {
        if (file is null)
        {
            throw ApiException.BadRequest($"Attach an image file in the '{fieldName}' form field.", "photo_file_missing");
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

    private static IEnumerable<string> EnumerateAvatarStorageKeys(string originalStorageKey)
    {
        yield return originalStorageKey;

        var extension = Path.GetExtension(originalStorageKey);
        var basePath = extension.Length == 0
            ? originalStorageKey
            : originalStorageKey[..^extension.Length];
        var variantExtension = extension.Length == 0 ? ".jpg" : extension;

        if (!basePath.EndsWith("-medium", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{basePath}-medium{variantExtension}";
        }

        if (!basePath.EndsWith("-thumb", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{basePath}-thumb{variantExtension}";
        }
    }

    private IReadOnlyCollection<QuotaConsumption> BuildAvatarUploadQuotaConsumptions(Guid userId, long uploadBytes) =>
    [
        new(
            QuotaSubjectKeys.UserScope,
            QuotaSubjectKeys.User(userId),
            QuotaNames.AvatarChangesPerDay,
            QuotaPeriodKind.Day,
            CountCost: 1,
            ByteCost: 0,
            CountLimit: _quotaOptions.Uploads.MaxAvatarChangesPerUserPerDay,
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
