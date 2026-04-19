using TrustMap.Api.Common;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Entities;

namespace TrustMap.Api.Features.Common;

public static class DtoMapper
{
    public static UserSummaryDto ToSummaryDto(this User user) =>
        new(user.Id, user.Handle, user.DisplayName, user.AvatarUrl);

    public static UserProfileDto ToProfileDto(
        this User user,
        RelationshipStatus relationshipStatus,
        int friendCount,
        int visiblePlaceReviewCount,
        int visibleDishReviewCount,
        bool isMe) =>
        new(
            user.Id,
            user.Handle,
            user.DisplayName,
            user.Bio,
            user.AvatarUrl,
            relationshipStatus,
            friendCount,
            visiblePlaceReviewCount,
            visibleDishReviewCount,
            isMe);

    public static PlaceSummaryDto ToSummaryDto(this Place place) =>
        place.ToSummaryDto([]);

    public static PlaceSummaryDto ToSummaryDto(this Place place, IReadOnlyList<string> categoryNames) =>
        new(
            place.Id,
            place.Name,
            place.Address,
            place.City,
            place.CountryCode,
            place.Latitude,
            place.Longitude,
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

    public static PlaceReviewDto ToDto(this PlaceReview review) =>
        review.ToDto(
            review.Place.CategoryAssignments
                .FirstOrDefault(x => x.AssignedByUserId == review.AuthorUserId));

    public static PlaceReviewDto ToDto(this PlaceReview review, PlaceCategoryAssignment? categoryAssignment) =>
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
            review.AuthorUser.ToSummaryDto(),
            review.Place.ToSummaryDto(categoryAssignment is null ? [] : [categoryAssignment.Category.Name]),
            review.Photos.OrderBy(x => x.CreatedAt).Select(x => x.ToDto()).ToArray());

    public static DishReviewDto ToDto(this DishReview review) =>
        review.ToDto(
            review.Place.CategoryAssignments
                .FirstOrDefault(x => x.AssignedByUserId == review.AuthorUserId));

    public static DishReviewDto ToDto(this DishReview review, PlaceCategoryAssignment? categoryAssignment) =>
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
            review.AuthorUser.ToSummaryDto(),
            review.Place.ToSummaryDto(categoryAssignment is null ? [] : [categoryAssignment.Category.Name]),
            review.Photos.OrderBy(x => x.CreatedAt).Select(x => x.ToDto()).ToArray());
}
