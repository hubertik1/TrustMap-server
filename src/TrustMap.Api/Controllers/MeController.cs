using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Users;
using TrustMap.Api.Services;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("me")]
public sealed class MeController(
    TrustMapDbContext dbContext,
    HandleGenerator handleGenerator,
    ProfileQueryService profileQueryService,
    IPhotoStorage photoStorage,
    ILogger<MeController> logger,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public Task<UserProfileDto> Get(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        return profileQueryService.GetProfileAsync(currentUserId, currentUserId, cancellationToken);
    }

    [HttpPatch]
    [Consumes("application/json")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserProfileDto>> PatchJson(
        [FromBody] UpdateMeRequest request,
        CancellationToken cancellationToken) =>
        PatchInternal(request, AvatarUpdateAction.Keep, avatarFile: null, cancellationToken);

    [HttpPatch]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public Task<ActionResult<UserProfileDto>> PatchMultipart(
        [FromForm] UpdateMeMultipartRequest request,
        CancellationToken cancellationToken)
    {
        var jsonRequest = new UpdateMeRequest
        {
            Handle = request.Handle,
            DisplayName = request.DisplayName,
            Bio = request.Bio,
            AvatarUrl = null
        };

        return PatchInternal(jsonRequest, request.AvatarAction, request.Avatar, cancellationToken);
    }

    [HttpPatch("privacy")]
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

        if (previousReviewVisibility != request.ReviewVisibility)
        {
            var placeReviews = await dbContext.PlaceReviews
                .Where(x => x.AuthorUserId == currentUserId && x.Visibility != VisibilityLevel.Private)
                .ToListAsync(cancellationToken);

            foreach (var review in placeReviews)
            {
                review.Update(request.ReviewVisibility, review.Rating, review.Title, review.Body);
            }

            var dishReviews = await dbContext.DishReviews
                .Where(x => x.AuthorUserId == currentUserId && x.Visibility != VisibilityLevel.Private)
                .ToListAsync(cancellationToken);

            foreach (var review in dishReviews)
            {
                review.Update(
                    request.ReviewVisibility,
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
        AvatarUpdateAction avatarAction,
        IFormFile? avatarFile,
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

        if (avatarAction == AvatarUpdateAction.Replace && avatarFile is null)
        {
            throw ApiException.BadRequest("Attach an avatar image when replacing the profile photo.", "avatar_file_missing");
        }

        if (avatarAction != AvatarUpdateAction.Replace && avatarFile is not null)
        {
            throw ApiException.BadRequest("Unexpected avatar file upload.", "avatar_file_unexpected");
        }

        var previousAvatarUrl = user.AvatarUrl;
        StoredPhoto? replacementAvatar = null;
        var avatarUrl = request.AvatarUrl;

        if (avatarAction == AvatarUpdateAction.Replace)
        {
            await using var stream = avatarFile!.OpenReadStream();
            replacementAvatar = await photoStorage.SaveAsync(
                stream,
                avatarFile.FileName,
                avatarFile.ContentType,
                cancellationToken);
            avatarUrl = replacementAvatar.PublicUrl;
        }
        else if (avatarAction == AvatarUpdateAction.Remove)
        {
            avatarUrl = null;
        }

        user.UpdateProfile(handle, request.DisplayName, request.Bio, avatarUrl);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
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
    {
        storageKey = string.Empty;

        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            return false;
        }

        var requestPath = configuration["PhotoStorage:RequestPath"]?.TrimEnd('/') ?? "/uploads";
        var path = avatarUrl;

        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var absoluteUri))
        {
            path = absoluteUri.AbsolutePath;
        }

        if (!path.StartsWith(requestPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        storageKey = path[requestPath.Length..].TrimStart('/');
        return !string.IsNullOrWhiteSpace(storageKey);
    }

    private static IEnumerable<string> EnumerateAvatarStorageKeys(string originalStorageKey)
    {
        yield return originalStorageKey;

        var extension = Path.GetExtension(originalStorageKey);
        var basePath = extension.Length == 0
            ? originalStorageKey
            : originalStorageKey[..^extension.Length];

        if (!basePath.EndsWith("-medium", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{basePath}-medium.jpg";
        }

        if (!basePath.EndsWith("-thumb", StringComparison.OrdinalIgnoreCase))
        {
            yield return $"{basePath}-thumb.jpg";
        }
    }
}
