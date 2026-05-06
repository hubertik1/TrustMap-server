using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Users;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class ProfileQueryService(
    TrustMapDbContext dbContext,
    SocialGraphService socialGraphService,
    PrivacyAccessService privacyAccessService)
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

        var accessMap = await privacyAccessService.GetAccessMapAsync(viewerUserId, [targetUserId], cancellationToken);
        var access = accessMap[targetUserId];
        var canViewProfile = access.CanAccess(user.ProfileVisibility);
        var canViewFriends = canViewProfile && access.CanAccess(user.FriendListVisibility);
        var canViewProfilePicture = access.CanAccess(user.ProfilePictureVisibility);

        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(viewerUserId, cancellationToken);
        var friendCount = canViewProfile && canViewFriends
            ? await socialGraphService.GetFriendCountAsync(targetUserId, cancellationToken)
            : 0;

        var visiblePlaceReviewCount = canViewProfile
            ? await dbContext.PlaceReviews
                .AsNoTracking()
                .VisibleToViewer(viewerUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
                .CountAsync(x => x.AuthorUserId == targetUserId, cancellationToken)
            : 0;

        var visibleDishReviewCount = canViewProfile
            ? await dbContext.DishReviews
                .AsNoTracking()
                .VisibleToViewer(viewerUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
                .CountAsync(x => x.AuthorUserId == targetUserId, cancellationToken)
            : 0;

        return user.ToProfileDto(
            relationshipStatus,
            friendCount,
            visiblePlaceReviewCount,
            visibleDishReviewCount,
            isMe: viewerUserId == targetUserId,
            canViewProfile,
            canViewFriends,
            canViewProfilePicture);
    }
}
