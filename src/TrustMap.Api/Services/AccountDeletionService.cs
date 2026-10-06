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
    Task<AccountDeletionResult> DeleteAccountAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken);
}

public sealed record AccountDeletionResult(bool RequiresManualAppleRevocation, bool CleanupPending);

public sealed class AccountDeletionService(
    TrustMapDbContext dbContext,
    AccountDeletionJobProcessor jobProcessor,
    TimeProvider timeProvider,
    IOptions<PhotoStorageOptions> photoStorageOptionsAccessor,
    ILogger<AccountDeletionService> logger) : IAccountDeletionService
{
    private readonly PhotoStorageOptions _photoStorageOptions = photoStorageOptionsAccessor.Value;

    public async Task<AccountDeletionResult> DeleteAccountAsync(Guid userId, string? ipAddress, CancellationToken cancellationToken)
    {
        var storageKeysToDelete = new HashSet<string>(StringComparer.Ordinal);
        var jobs = new List<AccountDeletionJob>();
        var requiresManualAppleRevocation = false;

        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
            if (user is null || user.Status == UserAccountStatus.Deleted)
            {
                await transaction.CommitAsync(cancellationToken);
                return new AccountDeletionResult(false, false);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var identities = await dbContext.AppleIdentities.AsNoTracking().Where(x => x.UserId == userId)
                .ToListAsync(cancellationToken);
            foreach (var identity in identities)
            {
                if (string.IsNullOrWhiteSpace(identity.ProtectedRefreshToken) || string.IsNullOrWhiteSpace(identity.ClientId))
                {
                    requiresManualAppleRevocation = true;
                    continue;
                }
                jobs.Add(new AccountDeletionJob
                {
                    Kind = AccountDeletionJobKind.AppleRevocation, Payload = identity.ProtectedRefreshToken,
                    ClientId = identity.ClientId, CreatedAtUtc = now, NextAttemptAtUtc = now
                });
            }

            CollectAvatarStorageKeys(user.AvatarUrl, storageKeysToDelete);

            await dbContext.UserBlocks.Where(x => x.BlockerUserId == userId || x.BlockedUserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            var placeReviewIds = await dbContext.PlaceReviews.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.AuthorUserId == userId)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            var dishReviewIds = await dbContext.DishReviews.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.AuthorUserId == userId)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);

            var friendRequestIds = await dbContext.FriendRequests.IgnoreQueryFilters()
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

            jobs.AddRange(storageKeysToDelete.Select(key => new AccountDeletionJob
            {
                Kind = AccountDeletionJobKind.Media, Payload = key, CreatedAtUtc = now, NextAttemptAtUtc = now
            }));
            dbContext.AccountDeletionJobs.AddRange(jobs);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Best effort now; the durable outbox survives timeouts, process crashes and provider outages.
        // External services are called only after the database transaction commits.
        var ids = jobs.Select(x => x.Id).ToList();
        if (ids.Count > 0)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await jobProcessor.ProcessPendingAsync(timeout.Token, ids);
            }
            catch (Exception exception)
            {
                logger.LogWarning("Account deletion cleanup deferred ({ExceptionType}); queued for retry.", exception.GetType().Name);
            }
        }
        var pending = ids.Count > 0;
        if (pending)
        {
            try
            {
                pending = await dbContext.AccountDeletionJobs.AsNoTracking()
                    .AnyAsync(x => ids.Contains(x.Id), CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogWarning("Account deletion cleanup status unavailable ({ExceptionType}); cleanup remains scheduled.", exception.GetType().Name);
            }
        }
        return new AccountDeletionResult(requiresManualAppleRevocation, pending);
    }

    private async Task CollectPhotoStorageKeysAsync(
        Guid userId,
        IReadOnlyCollection<Guid> placeReviewIds,
        IReadOnlyCollection<Guid> dishReviewIds,
        ISet<string> storageKeys,
        CancellationToken cancellationToken)
    {
        var photos = await dbContext.Photos.IgnoreQueryFilters()
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
        await dbContext.Notifications.IgnoreQueryFilters()
            .Where(x => x.RecipientUserId == userId || x.ActorUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        if (placeReviewIds.Count > 0)
        {
            await dbContext.Notifications.IgnoreQueryFilters()
                .Where(x => x.PlaceReviewId.HasValue && placeReviewIds.Contains(x.PlaceReviewId.Value))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (dishReviewIds.Count > 0)
        {
            await dbContext.Notifications.IgnoreQueryFilters()
                .Where(x => x.DishReviewId.HasValue && dishReviewIds.Contains(x.DishReviewId.Value))
                .ExecuteDeleteAsync(cancellationToken);
        }

        if (friendRequestIds.Count > 0)
        {
            await dbContext.Notifications.IgnoreQueryFilters()
                .Where(x => x.FriendRequestId.HasValue && friendRequestIds.Contains(x.FriendRequestId.Value))
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task DeleteRelationshipDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        await dbContext.Friendships.IgnoreQueryFilters()
            .Where(x => x.UserAId == userId || x.UserBId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.FriendRequests.IgnoreQueryFilters()
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
        await dbContext.UserCategoryMemberships.IgnoreQueryFilters()
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.PlaceCategoryAssignments.IgnoreQueryFilters()
            .Where(x => x.AssignedByUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.Categories.IgnoreQueryFilters()
            .Where(x => x.OwnerUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.QuotaUsages
            .Where(x => x.Scope == QuotaSubjectKeys.UserScope && x.SubjectKey == QuotaSubjectKeys.User(userId))
            .ExecuteDeleteAsync(cancellationToken);

        if (placeReviewIds.Count > 0 || dishReviewIds.Count > 0)
        {
            await dbContext.Photos.IgnoreQueryFilters()
                .Where(x =>
                    x.OwnerUserId == userId
                    || (x.PlaceReviewId.HasValue && placeReviewIds.Contains(x.PlaceReviewId.Value))
                    || (x.DishReviewId.HasValue && dishReviewIds.Contains(x.DishReviewId.Value)))
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            await dbContext.Photos.IgnoreQueryFilters()
                .Where(x => x.OwnerUserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await dbContext.DishReviews.IgnoreQueryFilters()
            .Where(x => x.AuthorUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        await dbContext.PlaceReviews.IgnoreQueryFilters()
            .Where(x => x.AuthorUserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private async Task CleanupCustomPlacesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var places = await dbContext.Places.IgnoreQueryFilters()
            .Where(x => x.CreatedByUserId == userId || x.CustomDisplayNameSetByUserId == userId)
            .ToListAsync(cancellationToken);

        foreach (var place in places)
        {
            if (place.CreatedByUserId == userId
                && place.SourceType == PlaceSourceType.CustomPin
                && !place.IsPublicCatalogPlace)
            {
                var hasRemainingReviews =
                    await dbContext.PlaceReviews.IgnoreQueryFilters().AnyAsync(x => x.PlaceId == place.Id, cancellationToken)
                    || await dbContext.DishReviews.IgnoreQueryFilters().AnyAsync(x => x.PlaceId == place.Id, cancellationToken);

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

}
