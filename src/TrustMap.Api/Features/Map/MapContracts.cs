namespace TrustMap.Api.Features.Map;

public sealed record MapPlaceDto(
    Guid PlaceId,
    string Name,
    string Address,
    string? City,
    string? CountryCode,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> CategoryNames,
    int VisiblePlaceReviewCount,
    int VisibleDishReviewCount,
    double? AveragePlaceRating,
    int ContributorCount,
    DateTime LatestActivityAtUtc);
