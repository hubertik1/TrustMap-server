using TrustMap.Api.Common;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Entities;

namespace TrustMap.Api.Features.Common;

public static class DtoMapper
{
    public static UserSummaryDto ToSummaryDto(this User user, bool canViewProfilePicture) =>
        new(user.Id, user.Handle, user.DisplayName, canViewProfilePicture ? user.AvatarUrl : null);

    public static UserProfileDto ToProfileDto(
        this User user,
        RelationshipStatus relationshipStatus,
        int friendCount,
        int visiblePlaceReviewCount,
        int visibleDishReviewCount,
        bool isMe,
        bool canViewProfile,
        bool canViewFriends,
        bool canViewReviews,
        bool canViewProfilePicture) =>
        new(
            user.Id,
            user.Handle,
            user.DisplayName,
            canViewProfile ? user.Bio : null,
            canViewProfilePicture ? user.AvatarUrl : null,
            relationshipStatus,
            canViewProfile && canViewFriends ? friendCount : 0,
            canViewProfile && canViewReviews ? visiblePlaceReviewCount : 0,
            canViewProfile && canViewReviews ? visibleDishReviewCount : 0,
            isMe,
            user.ReviewVisibility,
            user.FriendListVisibility,
            user.ProfileVisibility,
            user.ProfilePictureVisibility,
            canViewProfile,
            canViewProfile && canViewFriends,
            canViewProfile && canViewReviews,
            canViewProfilePicture);

    public static PlaceSummaryDto ToSummaryDto(this Place place, Guid currentUserId) =>
        place.ToSummaryDto([], currentUserId);

    public static PlaceSummaryDto ToSummaryDto(
        this Place place,
        IReadOnlyList<string> categoryNames,
        Guid currentUserId) =>
        new(
            place.Id,
            place.Name,
            place.DisplayName,
            place.CustomDisplayName,
            place.SourceType,
            place.CanEditCustomDisplayName(currentUserId),
            place.Address,
            place.City,
            place.CountryCode,
            place.Latitude,
            place.Longitude,
            place.CreatedByUserId,
            categoryNames);

    public static PhotoDto ToDto(this Photo photo) =>
        new(
            photo.Id,
            photo.PublicUrl,
            photo.ContentType,
            photo.SizeBytes,
            photo.Width,
            photo.Height,
            photo.MediumUrl,
            photo.ThumbnailUrl,
            photo.CreatedAt);

    public static PlaceReviewDto ToDto(this PlaceReview review, Guid currentUserId, bool canViewAuthorProfilePicture) =>
        review.ToDto(
            currentUserId,
            canViewAuthorProfilePicture,
            review.Place.CategoryAssignments
                .FirstOrDefault(x => x.AssignedByUserId == review.AuthorUserId));

    public static PlaceReviewDto ToDto(
        this PlaceReview review,
        Guid currentUserId,
        bool canViewAuthorProfilePicture,
        PlaceCategoryAssignment? categoryAssignment) =>
        new(
            review.Id,
            review.PlaceId,
            categoryAssignment?.CategoryId,
            categoryAssignment?.Category.Name,
            review.Visibility,
            review.Rating,
            review.Title,
            review.Body,
            review.CreatedAt,
            review.UpdatedAt,
            review.AuthorUser.ToSummaryDto(canViewAuthorProfilePicture),
            review.Place.ToSummaryDto(
                categoryAssignment is null ? [] : [categoryAssignment.Category.Name],
                currentUserId),
            review.Photos.OrderBy(x => x.CreatedAt).Select(x => x.ToDto()).ToArray());

    public static DishReviewDto ToDto(this DishReview review, Guid currentUserId, bool canViewAuthorProfilePicture) =>
        review.ToDto(
            currentUserId,
            canViewAuthorProfilePicture,
            review.Place.CategoryAssignments
                .FirstOrDefault(x => x.AssignedByUserId == review.AuthorUserId));

    public static DishReviewDto ToDto(
        this DishReview review,
        Guid currentUserId,
        bool canViewAuthorProfilePicture,
        PlaceCategoryAssignment? categoryAssignment) =>
        new(
            review.Id,
            review.PlaceId,
            review.PlaceReviewId,
            categoryAssignment?.CategoryId,
            categoryAssignment?.Category.Name,
            review.Visibility,
            review.DishName,
            review.Rating,
            review.Body,
            review.PriceAmount,
            review.CurrencyCode,
            review.CreatedAt,
            review.UpdatedAt,
            review.AuthorUser.ToSummaryDto(canViewAuthorProfilePicture),
            review.Place.ToSummaryDto(
                categoryAssignment is null ? [] : [categoryAssignment.Category.Name],
                currentUserId),
            review.Photos.OrderBy(x => x.CreatedAt).Select(x => x.ToDto()).ToArray());
}
