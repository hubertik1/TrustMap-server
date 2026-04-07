using System.ComponentModel.DataAnnotations;
using TrustMap.Api.Common;

namespace TrustMap.Api.Features.Users;

public sealed record UserSummaryDto(
    Guid Id,
    string Handle,
    string DisplayName,
    string? AvatarUrl);

public sealed record UserProfileDto(
    Guid Id,
    string Handle,
    string DisplayName,
    string? Bio,
    string? AvatarUrl,
    RelationshipStatus RelationshipStatus,
    int FriendCount,
    int VisiblePlaceReviewCount,
    int VisibleDishReviewCount,
    bool IsMe);

public sealed record UserSearchResultDto(
    UserSummaryDto User,
    RelationshipStatus RelationshipStatus);

public sealed class UpdateMeRequest
{
    [Required]
    [StringLength(32, MinimumLength = 3)]
    public string Handle { get; init; } = null!;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; init; } = null!;

    [StringLength(500)]
    public string? Bio { get; init; }

    [StringLength(500)]
    public string? AvatarUrl { get; init; }
}
