using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Domain.Common;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed record ViewerAccess(
    Guid UserId,
    bool IsSelf,
    bool IsDirectFriend,
    bool IsFriendOfFriend)
{
    public bool CanAccess(VisibilityLevel visibility)
    {
        EnumGuard.EnsureDefined(visibility, nameof(visibility));

        return visibility switch
        {
            VisibilityLevel.Public => true,
            VisibilityLevel.FriendsOfFriends => IsSelf || IsDirectFriend || IsFriendOfFriend,
            VisibilityLevel.Friends => IsSelf || IsDirectFriend,
            VisibilityLevel.Private => IsSelf,
            _ => false
        };
    }
}

public sealed record ViewerSocialAccess(
    HashSet<Guid> FriendIds,
    HashSet<Guid> FriendOfFriendIds);

public sealed class PrivacyAccessService(TrustMapDbContext dbContext)
{
    public async Task<bool> CanViewerAccessAsync(
        Guid viewerUserId,
        Guid ownerUserId,
        VisibilityLevel visibility,
        CancellationToken cancellationToken)
    {
        var ownerIsActive = await dbContext.Users
            .AsNoTracking()
            .ActiveOnly()
            .AnyAsync(x => x.Id == ownerUserId, cancellationToken);
        if (!ownerIsActive)
        {
            return false;
        }

        var accessMap = await GetAccessMapAsync(viewerUserId, [ownerUserId], cancellationToken);
        return accessMap.GetValueOrDefault(ownerUserId)?.CanAccess(visibility) ?? false;
    }

    public async Task<bool?> CanViewerAccessUserReviewsAsync(
        Guid viewerUserId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var visibility = await dbContext.Users
            .AsNoTracking()
            .ActiveOnly()
            .Where(x => x.Id == ownerUserId)
            .Select(x => new
            {
                x.ProfileVisibility,
                x.ReviewVisibility
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (visibility is null)
        {
            return null;
        }

        var accessMap = await GetAccessMapAsync(viewerUserId, [ownerUserId], cancellationToken);
        var access = accessMap[ownerUserId];

        return access.CanAccess(visibility.ProfileVisibility)
            && access.CanAccess(visibility.ReviewVisibility);
    }

    public async Task<bool> CanViewerAccessPlaceAsync(
        Guid viewerUserId,
        Guid placeId,
        CancellationToken cancellationToken)
    {
        var socialAccess = await GetViewerSocialAccessAsync(viewerUserId, cancellationToken);

        return await dbContext.Places
            .AsNoTracking()
            .VisibleToViewer(viewerUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .AnyAsync(x => x.Id == placeId, cancellationToken);
    }

    public async Task<Dictionary<Guid, ViewerAccess>> GetAccessMapAsync(
        Guid viewerUserId,
        IReadOnlyCollection<Guid> ownerUserIds,
        CancellationToken cancellationToken)
    {
        var targetIds = ownerUserIds.Distinct().ToList();
        var result = targetIds.ToDictionary(
            x => x,
            x => new ViewerAccess(x, x == viewerUserId, IsDirectFriend: false, IsFriendOfFriend: false));

        var nonSelfTargetIds = targetIds.Where(x => x != viewerUserId).ToList();
        if (nonSelfTargetIds.Count == 0)
        {
            return result;
        }

        var friendIds = await GetFriendIdsAsync(viewerUserId, cancellationToken);
        foreach (var userId in nonSelfTargetIds.Where(friendIds.Contains))
        {
            result[userId] = result[userId] with { IsDirectFriend = true };
        }

        if (friendIds.Count == 0)
        {
            return result;
        }

        var friendOfFriendIds = await dbContext.Friendships
            .AsNoTracking()
            .Where(x => x.UserA.Status == UserAccountStatus.Active && x.UserB.Status == UserAccountStatus.Active)
            .Where(x =>
                (friendIds.Contains(x.UserAId) && nonSelfTargetIds.Contains(x.UserBId))
                || (friendIds.Contains(x.UserBId) && nonSelfTargetIds.Contains(x.UserAId)))
            .Select(x => friendIds.Contains(x.UserAId) ? x.UserBId : x.UserAId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var userId in friendOfFriendIds.Where(x => x != viewerUserId && !friendIds.Contains(x)))
        {
            if (result.TryGetValue(userId, out var access))
            {
                result[userId] = access with { IsFriendOfFriend = true };
            }
        }

        return result;
    }

    public async Task<ViewerSocialAccess> GetViewerSocialAccessAsync(
        Guid viewerUserId,
        CancellationToken cancellationToken)
    {
        var friendIds = await GetFriendIdsAsync(viewerUserId, cancellationToken);
        if (friendIds.Count == 0)
        {
            return new ViewerSocialAccess(friendIds, []);
        }

        var friendOfFriendIds = await dbContext.Friendships
            .AsNoTracking()
            .Where(x => x.UserA.Status == UserAccountStatus.Active && x.UserB.Status == UserAccountStatus.Active)
            .Where(x => friendIds.Contains(x.UserAId) || friendIds.Contains(x.UserBId))
            .Select(x => friendIds.Contains(x.UserAId) ? x.UserBId : x.UserAId)
            .Distinct()
            .ToListAsync(cancellationToken);

        friendOfFriendIds.Remove(viewerUserId);
        var friendOfFriendSet = friendOfFriendIds.ToHashSet();
        friendOfFriendSet.ExceptWith(friendIds);

        return new ViewerSocialAccess(friendIds, friendOfFriendSet);
    }

    private async Task<HashSet<Guid>> GetFriendIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var friendIds = await dbContext.Friendships
            .AsNoTracking()
            .Where(x => x.UserA.Status == UserAccountStatus.Active && x.UserB.Status == UserAccountStatus.Active)
            .Where(x => x.UserAId == userId || x.UserBId == userId)
            .Select(x => x.UserAId == userId ? x.UserBId : x.UserAId)
            .ToListAsync(cancellationToken);

        return friendIds.ToHashSet();
    }
}
