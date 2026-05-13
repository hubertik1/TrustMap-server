using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Features.Map;

public sealed record MapPlaceDto(
    Guid PlaceId,
    string Name,
    string DisplayName,
    string? CustomDisplayName,
    PlaceSourceType SourceType,
    string Address,
    string? City,
    string? CountryCode,
    double Latitude,
    double Longitude,
    Guid? CreatedByUserId,
    IReadOnlyList<string> CategoryNames,
    int VisiblePlaceReviewCount,
    int VisibleDishReviewCount,
    double? AveragePlaceRating,
    int ContributorCount,
    DateTime LatestActivityAtUtc,
    string SearchText = "")
{
    public IReadOnlyList<UserSummaryDto> RecentContributors { get; init; } = [];

    public bool IsReviewedByCurrentUser { get; init; }
}

public sealed record MapPinDto(
    Guid PlaceId,
    string DisplayName,
    double Latitude,
    double Longitude,
    double? AverageRating,
    int ReviewCount,
    int ContributorCount,
    IReadOnlyList<Guid> CategoryIds,
    bool IsReviewedByCurrentUser,
    DateTime? LatestActivityAtUtc);
