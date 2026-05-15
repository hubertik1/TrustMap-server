using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class SocialGraphService(TrustMapDbContext dbContext)
{
    public async Task<HashSet<Guid>> GetFriendIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var friendIds = await dbContext.Friendships
            .AsNoTracking()
            .Where(x => x.UserA.Status == UserAccountStatus.Active && x.UserB.Status == UserAccountStatus.Active)
            .Where(x => x.UserAId == userId || x.UserBId == userId)
            .Select(x => x.UserAId == userId ? x.UserBId : x.UserAId)
            .ToListAsync(cancellationToken);

        return friendIds.ToHashSet();
    }

    public async Task<int> GetFriendCountAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.Friendships
            .AsNoTracking()
            .Where(x => x.UserA.Status == UserAccountStatus.Active && x.UserB.Status == UserAccountStatus.Active)
            .CountAsync(x => x.UserAId == userId || x.UserBId == userId, cancellationToken);
    }

    public async Task<RelationshipStatus> GetRelationshipStatusAsync(
        Guid viewerUserId,
        Guid otherUserId,
        CancellationToken cancellationToken)
    {
        var result = await GetRelationshipStatusesAsync(viewerUserId, [otherUserId], cancellationToken);
        return result.TryGetValue(otherUserId, out var status) ? status : RelationshipStatus.None;
    }

    public async Task<Dictionary<Guid, RelationshipStatus>> GetRelationshipStatusesAsync(
        Guid viewerUserId,
        IReadOnlyCollection<Guid> otherUserIds,
        CancellationToken cancellationToken)
    {
        var result = otherUserIds.ToDictionary(x => x, _ => RelationshipStatus.None);

        foreach (var userId in otherUserIds.Where(x => x == viewerUserId))
        {
            result[userId] = RelationshipStatus.Self;
        }

        var targetIds = otherUserIds.Where(x => x != viewerUserId).Distinct().ToList();
        if (targetIds.Count == 0)
        {
            return result;
        }

        var friendships = await dbContext.Friendships
            .AsNoTracking()
            .Where(x => x.UserA.Status == UserAccountStatus.Active && x.UserB.Status == UserAccountStatus.Active)
            .Where(x =>
                (x.UserAId == viewerUserId && targetIds.Contains(x.UserBId))
                || (x.UserBId == viewerUserId && targetIds.Contains(x.UserAId)))
            .ToListAsync(cancellationToken);

        foreach (var friendship in friendships)
        {
            var otherId = friendship.UserAId == viewerUserId ? friendship.UserBId : friendship.UserAId;
            result[otherId] = RelationshipStatus.Friends;
        }

        var pendingRequests = await dbContext.FriendRequests
            .AsNoTracking()
            .Where(x => x.SenderUser.Status == UserAccountStatus.Active && x.ReceiverUser.Status == UserAccountStatus.Active)
            .Where(x =>
                x.Status == FriendRequestStatus.Pending
                && ((x.SenderUserId == viewerUserId && targetIds.Contains(x.ReceiverUserId))
                    || (x.ReceiverUserId == viewerUserId && targetIds.Contains(x.SenderUserId))))
            .ToListAsync(cancellationToken);

        foreach (var request in pendingRequests)
        {
            if (request.SenderUserId == viewerUserId && result[request.ReceiverUserId] == RelationshipStatus.None)
            {
                result[request.ReceiverUserId] = RelationshipStatus.OutgoingRequest;
            }
            else if (request.ReceiverUserId == viewerUserId && result[request.SenderUserId] == RelationshipStatus.None)
            {
                result[request.SenderUserId] = RelationshipStatus.IncomingRequest;
            }
        }

        return result;
    }

    public static string BuildPairKey(Guid leftUserId, Guid rightUserId) =>
        Friendship.BuildPairKey(leftUserId, rightUserId);
}
