using System.ComponentModel.DataAnnotations;

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

public sealed record PlaceSummaryDto(
    Guid Id,
    string Name,
    string Address,
    string? City,
    string? CountryCode,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> CategoryNames);

public sealed record PlaceDetailDto(
    Guid Id,
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
    Guid? ViewerPlaceReviewId,
    int ViewerDishReviewCount,
    DateTime? LatestActivityAtUtc);
