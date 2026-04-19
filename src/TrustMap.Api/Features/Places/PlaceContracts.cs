using System.ComponentModel.DataAnnotations;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Features.Places;

public sealed class CreatePlaceRequest
{
    [Required]
    [StringLength(200)]
    public string Name { get; init; } = null!;

    [Required]
    [StringLength(300)]
    public string Address { get; init; } = null!;

    [StringLength(120)]
    public string? City { get; init; }

    [StringLength(2)]
    public string? CountryCode { get; init; }

    [Range(-90, 90)]
    public double Latitude { get; init; }

    [Range(-180, 180)]
    public double Longitude { get; init; }

    [StringLength(40)]
    public string? Provider { get; init; }

    [StringLength(200)]
    public string? ProviderPlaceId { get; init; }
}

public sealed class UpdateCustomPlaceDisplayNameRequest
{
    [Required]
    [StringLength(200)]
    public string DisplayName { get; init; } = null!;
}

public sealed record PlaceSummaryDto(
    Guid Id,
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
    IReadOnlyList<string> CategoryNames);

public sealed record PlaceDetailDto(
    Guid Id,
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
    Guid? ViewerPlaceReviewId,
    int ViewerDishReviewCount,
    DateTime? LatestActivityAtUtc);
