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
            .ActiveOnly()
            .SingleOrDefaultAsync(x => x.Id == targetUserId, cancellationToken)
            ?? throw ApiException.NotFound("User not found.", "user_not_found");

        var relationshipStatus = viewerUserId == targetUserId
            ? RelationshipStatus.Self
            : await socialGraphService.GetRelationshipStatusAsync(viewerUserId, targetUserId, cancellationToken);

        var accessMap = await privacyAccessService.GetAccessMapAsync(viewerUserId, [targetUserId], cancellationToken);
        var access = accessMap[targetUserId];
        var canViewProfile = true;
        var canViewFriends = access.IsSelf
            || (access.IsDirectFriend && user.FriendListVisibility != TrustMap.Domain.Enums.VisibilityLevel.Private);
        var canViewReviews = access.IsSelf || access.IsDirectFriend;
        var canViewProfilePicture = access.CanAccess(user.ProfilePictureVisibility);

        var friendCount = await socialGraphService.GetFriendCountAsync(targetUserId, cancellationToken);
        // Counts are public; review content is restricted independently.
        var visiblePlaceReviewCount = await dbContext.PlaceReviews.AsNoTracking()
            .CountAsync(x => x.AuthorUserId == targetUserId, cancellationToken);
        var visibleDishReviewCount = await dbContext.DishReviews.AsNoTracking()
            .CountAsync(x => x.AuthorUserId == targetUserId, cancellationToken);

        return user.ToProfileDto(
            relationshipStatus,
            friendCount,
            visiblePlaceReviewCount,
            visibleDishReviewCount,
            isMe: viewerUserId == targetUserId,
            canViewProfile,
            canViewFriends,
            canViewReviews,
            canViewProfilePicture);
    }
}
