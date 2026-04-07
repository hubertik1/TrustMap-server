using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Users;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class ProfileQueryService(
    TrustMapDbContext dbContext,
    SocialGraphService socialGraphService)
{
    public async Task<UserProfileDto> GetProfileAsync(
        Guid viewerUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == targetUserId, cancellationToken)
            ?? throw ApiException.NotFound("User not found.", "user_not_found");

        var relationshipStatus = viewerUserId == targetUserId
            ? RelationshipStatus.Self
            : await socialGraphService.GetRelationshipStatusAsync(viewerUserId, targetUserId, cancellationToken);

        var friendIds = await socialGraphService.GetFriendIdsAsync(viewerUserId, cancellationToken);
        var friendCount = await socialGraphService.GetFriendCountAsync(targetUserId, cancellationToken);

        var visiblePlaceReviewCount = await dbContext.PlaceReviews
            .AsNoTracking()
            .VisibleToViewer(viewerUserId, friendIds)
            .CountAsync(x => x.AuthorUserId == targetUserId, cancellationToken);

        var visibleDishReviewCount = await dbContext.DishReviews
            .AsNoTracking()
            .VisibleToViewer(viewerUserId, friendIds)
            .CountAsync(x => x.AuthorUserId == targetUserId, cancellationToken);

        return user.ToProfileDto(
            relationshipStatus,
            friendCount,
            visiblePlaceReviewCount,
            visibleDishReviewCount,
            isMe: viewerUserId == targetUserId);
    }
}
