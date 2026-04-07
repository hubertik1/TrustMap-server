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
        new(
            place.Id,
            place.Name,
            place.Address,
            place.City,
            place.CountryCode,
            place.Latitude,
            place.Longitude);

    public static PhotoDto ToDto(this Photo photo) =>
        new(photo.Id, photo.PublicUrl, photo.ContentType, photo.SizeBytes, photo.CreatedAt);

    public static PlaceReviewDto ToDto(this PlaceReview review) =>
        new(
            review.Id,
            review.PlaceId,
            review.Visibility,
            review.Rating,
            review.Title,
            review.Body,
            review.CreatedAt,
            review.UpdatedAt,
            review.AuthorUser.ToSummaryDto(),
            review.Place.ToSummaryDto(),
            review.Photos.OrderBy(x => x.CreatedAt).Select(x => x.ToDto()).ToArray());

    public static DishReviewDto ToDto(this DishReview review) =>
        new(
            review.Id,
            review.PlaceId,
            review.PlaceReviewId,
            review.Visibility,
            review.DishName,
            review.Rating,
            review.Body,
            review.PriceAmount,
            review.CurrencyCode,
            review.CreatedAt,
            review.UpdatedAt,
            review.AuthorUser.ToSummaryDto(),
            review.Place.ToSummaryDto(),
            review.Photos.OrderBy(x => x.CreatedAt).Select(x => x.ToDto()).ToArray());
}
