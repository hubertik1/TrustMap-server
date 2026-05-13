using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Features.Reviews;

public sealed record PhotoDto(
    Guid Id,
    string Url,
    string ContentType,
    long SizeBytes,
    int? Width,
    int? Height,
    string? MediumUrl,
    string? ThumbnailUrl,
    DateTime CreatedAtUtc);

public sealed record PlaceReviewDto(
    Guid Id,
    Guid PlaceId,
    Guid? CategoryId,
    string? CategoryName,
    VisibilityLevel Visibility,
    int Rating,
    string? Title,
    string? Body,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    UserSummaryDto Author,
    PlaceSummaryDto Place,
    IReadOnlyList<PhotoDto> Photos);

public sealed record DishReviewDto(
    Guid Id,
    Guid PlaceId,
    Guid? PlaceReviewId,
    Guid? CategoryId,
    string? CategoryName,
    VisibilityLevel Visibility,
    string DishName,
    int Rating,
    string? Body,
    decimal? PriceAmount,
    string? CurrencyCode,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    UserSummaryDto Author,
    PlaceSummaryDto Place,
    IReadOnlyList<PhotoDto> Photos);

public sealed class CreatePlaceReviewRequest
{
    [Required]
    public Guid PlaceId { get; init; }

    [Required]
    public Guid CategoryId { get; init; }

    [Required]
    [DefinedEnum]
    public VisibilityLevel Visibility { get; init; }

    [Range(1, 5)]
    public int Rating { get; init; }

    [StringLength(160)]
    public string? Title { get; init; }

    [StringLength(4000)]
    public string? Body { get; init; }
}

public sealed class UpdatePlaceReviewRequest
{
    [Required]
    public Guid CategoryId { get; init; }

    [Required]
    [DefinedEnum]
    public VisibilityLevel Visibility { get; init; }

    [Range(1, 5)]
    public int Rating { get; init; }

    [StringLength(160)]
    public string? Title { get; init; }

    [StringLength(4000)]
    public string? Body { get; init; }
}

public sealed class CreateDishReviewRequest
{
    [Required]
    public Guid PlaceId { get; init; }

    [Required]
    public Guid CategoryId { get; init; }

    public Guid? PlaceReviewId { get; init; }

    [Required]
    [DefinedEnum]
    public VisibilityLevel Visibility { get; init; }

    [Required]
    [StringLength(200)]
    public string DishName { get; init; } = null!;

    [Range(1, 5)]
    public int Rating { get; init; }

    [StringLength(4000)]
    public string? Body { get; init; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal? PriceAmount { get; init; }

    [StringLength(3)]
    public string? CurrencyCode { get; init; }
}

public sealed class UpdateDishReviewRequest
{
    [Required]
    public Guid CategoryId { get; init; }

    [Required]
    [DefinedEnum]
    public VisibilityLevel Visibility { get; init; }

    [Required]
    [StringLength(200)]
    public string DishName { get; init; } = null!;

    [Range(1, 5)]
    public int Rating { get; init; }

    [StringLength(4000)]
    public string? Body { get; init; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal? PriceAmount { get; init; }

    [StringLength(3)]
    public string? CurrencyCode { get; init; }
}

public sealed class UploadPhotoRequest
{
    [FromForm(Name = "file")]
    [Required]
    public IFormFile File { get; init; } = null!;

    [FromForm(Name = "placeReviewId")]
    public Guid? PlaceReviewId { get; init; }

    [FromForm(Name = "dishReviewId")]
    public Guid? DishReviewId { get; init; }
}
