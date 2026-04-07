using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Features.Feed;

public enum FeedActivityType
{
    PlaceReview = 1,
    DishReview = 2
}

public sealed record FeedItemDto(
    FeedActivityType ActivityType,
    Guid ItemId,
    VisibilityLevel Visibility,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    UserSummaryDto Author,
    PlaceSummaryDto Place,
    int Rating,
    string Body,
    string? Title,
    string? DishName,
    IReadOnlyList<PhotoDto> Photos);

public sealed record FeedResponseDto(
    IReadOnlyList<FeedItemDto> Items,
    DateTime? NextCursorUtc);
