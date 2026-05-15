using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Quotas;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Services;

public interface IAccountDeletionService
{
    Task DeleteAccountAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken);
}

public sealed class AccountDeletionService(
    TrustMapDbContext dbContext,
    IPhotoStorage photoStorage,
    TimeProvider timeProvider,
    IOptions<PhotoStorageOptions> photoStorageOptionsAccessor,
    ILogger<AccountDeletionService> logger) : IAccountDeletionService
{
    private readonly PhotoStorageOptions _photoStorageOptions = photoStorageOptionsAccessor.Value;

    public async Task DeleteAccountAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken)
    {
        var storageKeysToDelete = new HashSet<string>(StringComparer.Ordinal);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
            if (user is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            CollectAvatarStorageKeys(user.AvatarUrl, storageKeysToDelete);

            var placeReviewIds = await dbContext.PlaceReviews
                .AsNoTracking()
                .Where(x => x.AuthorUserId == userId)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            var dishReviewIds = await dbContext.DishReviews
                .AsNoTracking()
                .Where(x => x.AuthorUserId == userId)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            var friendRequestIds = await dbContext.FriendRequests
                .AsNoTracking()
                .Where(x => x.SenderUserId == userId || x.ReceiverUserId == userId)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            await CollectPhotoStorageKeysAsync(userId, placeReviewIds, dishReviewIds, storageKeysToDelete, cancellationToken);
            await DeleteRelatedNotificationsAsync(userId, placeReviewIds, dishReviewIds, friendRequestIds, cancellationToken);
            await DeleteRelationshipDataAsync(userId, cancellationToken);
            await DeleteSessionAndIdentityDataAsync(userId, cancellationToken);
            await DeleteReviewAndPersonalizationDataAsync(userId, placeReviewIds, dishReviewIds, cancellationToken);
            await CleanupCustomPlacesAsync(userId, cancellationToken);

            user.MarkDeleted(timeProvider.GetUtcNow().UtcDateTime);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await DeleteStorageKeysAfterCommitAsync(userId, storageKeysToDelete);
    }

    private async Task CollectPhotoStorageKeysAsync(
        Guid userId,
        IReadOnlyCollection<Guid> placeReviewIds,
        IReadOnlyCollection<Guid> dishReviewIds,
        ISet<string> storageKeys,
        CancellationToken cancellationToken)
    {
        var photos = await dbContext.Photos
            .AsNoTracking()
            .Where(x =>
                x.OwnerUserId == userId
                || (x.PlaceReviewId.HasValue && placeReviewIds.Contains(x.PlaceReviewId.Value))
                || (x.DishReviewId.HasValue && dishReviewIds.Contains(x.DishReviewId.Value)))
            .ToListAsync(cancellationToken);

        foreach (var storageKey in photos.SelectMany(x => x.EnumerateStorageKeys()))
        {
            AddStorageKey(storageKeys, storageKey);
        }
    }

    private async Task DeleteRelatedNotificationsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> placeReviewIds,
        IReadOnlyCollection<Guid> dishReviewIds,
        IReadOnlyCollection<Guid> friendRequestIds,
        CancellationToken cancellationToken)
    {
        await dbContext.Notifications
            .Where(x => x.RecipientUserId == userId || x.ActorUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (placeReviewIds.Count > 0)
        {
            await dbContext.Notifications
                .Where(x => x.PlaceReviewId.HasValue && placeReviewIds.Contains(x.PlaceReviewId.Value))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (dishReviewIds.Count > 0)
        {
            await dbContext.Notifications
                .Where(x => x.DishReviewId.HasValue && dishReviewIds.Contains(x.DishReviewId.Value))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (friendRequestIds.Count > 0)
        {
            await dbContext.Notifications
                .Where(x => x.FriendRequestId.HasValue && friendRequestIds.Contains(x.FriendRequestId.Value))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task DeleteRelationshipDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        await dbContext.Friendships
            .Where(x => x.UserAId == userId || x.UserBId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.FriendRequests
            .Where(x => x.SenderUserId == userId || x.ReceiverUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task DeleteSessionAndIdentityDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        await dbContext.RefreshTokens
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.DeviceTokens
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.AppleIdentities
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task DeleteReviewAndPersonalizationDataAsync(
        Guid userId,
        IReadOnlyCollection<Guid> placeReviewIds,
        IReadOnlyCollection<Guid> dishReviewIds,
        CancellationToken cancellationToken)
    {
        await dbContext.UserCategoryMemberships
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.PlaceCategoryAssignments
            .Where(x => x.AssignedByUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Categories
            .Where(x => x.OwnerUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.QuotaUsages
            .Where(x => x.Scope == QuotaSubjectKeys.UserScope && x.SubjectKey == QuotaSubjectKeys.User(userId))
            .ExecuteDeleteAsync(cancellationToken);

        if (placeReviewIds.Count > 0 || dishReviewIds.Count > 0)
        {
            await dbContext.Photos
                .Where(x =>
                    x.OwnerUserId == userId
                    || (x.PlaceReviewId.HasValue && placeReviewIds.Contains(x.PlaceReviewId.Value))
                    || (x.DishReviewId.HasValue && dishReviewIds.Contains(x.DishReviewId.Value)))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await dbContext.Photos
                .Where(x => x.OwnerUserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await dbContext.DishReviews
            .Where(x => x.AuthorUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.PlaceReviews
            .Where(x => x.AuthorUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task CleanupCustomPlacesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var places = await dbContext.Places
            .Where(x => x.CreatedByUserId == userId || x.CustomDisplayNameSetByUserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var place in places)
        {
            if (place.CreatedByUserId == userId
                && place.SourceType == PlaceSourceType.CustomPin
                && !place.IsPublicCatalogPlace)
            {
                var hasRemainingReviews =
                    await dbContext.PlaceReviews.AnyAsync(x => x.PlaceId == place.Id, cancellationToken)
                    || await dbContext.DishReviews.AnyAsync(x => x.PlaceId == place.Id, cancellationToken);

                if (!hasRemainingReviews)
                {
                    dbContext.Places.Remove(place);
                    continue;
                }
            }

            place.DetachDeletedUserReferences(userId);
        }
    }

    private void CollectAvatarStorageKeys(string? avatarUrl, ISet<string> storageKeys)
    {
        if (!PhotoStorageUrl.TryResolveStorageKey(avatarUrl, _photoStorageOptions.RequestPath, out var originalStorageKey))
        {
            return;
        }

        foreach (var storageKey in EnumerateAvatarStorageKeys(originalStorageKey))
        {
            AddStorageKey(storageKeys, storageKey);
        }
    }

    private async Task DeleteStorageKeysAfterCommitAsync(Guid userId, IReadOnlyCollection<string> storageKeys)
    {
        foreach (var storageKey in storageKeys)
        {
            try
            {
                await photoStorage.DeleteAsync(storageKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to delete account media blob for user {UserId}; storage key hash prefix {StorageKeyHashPrefix}.",
                    userId,
                    BuildStorageKeyHashPrefix(storageKey));
            }
        }
    }

    private static void AddStorageKey(ISet<string> storageKeys, string? storageKey)
    {
        if (!string.IsNullOrWhiteSpace(storageKey))
        {
            storageKeys.Add(storageKey.Trim());
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

    private static string BuildStorageKeyHashPrefix(string storageKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(storageKey));
        return Convert.ToHexString(hash.AsSpan(0, 6)).ToLowerInvariant();
    }
}
