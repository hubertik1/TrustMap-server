namespace TrustMap.Api.Features.Map;

public sealed record MapPlaceDto(
    Guid PlaceId,
    string Name,
    string Address,
    string? City,
    string? CountryCode,
    double Latitude,
    double Longitude,
    int VisiblePlaceReviewCount,
    int VisibleDishReviewCount,
    double? AveragePlaceRating,
    int ContributorCount,
    DateTime LatestActivityAtUtc);
