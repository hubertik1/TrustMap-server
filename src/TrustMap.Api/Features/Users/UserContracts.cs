using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TrustMap.Api.Common;
using TrustMap.Domain.Enums;

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
    bool IsMe,
    VisibilityLevel ReviewVisibility,
    VisibilityLevel FriendListVisibility,
    VisibilityLevel ProfileVisibility,
    VisibilityLevel ProfilePictureVisibility,
    bool CanViewProfile,
    bool CanViewFriends,
    bool CanViewReviews,
    bool CanViewProfilePicture);

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
}

public sealed class UpdateMePrivacyRequest
{
    [DefinedEnum]
    public VisibilityLevel ReviewVisibility { get; init; } = VisibilityLevel.Friends;

    [DefinedEnum]
    public VisibilityLevel FriendListVisibility { get; init; }

    [DefinedEnum]
    public VisibilityLevel ProfileVisibility { get; init; } = VisibilityLevel.Public;

    [DefinedEnum]
    public VisibilityLevel ProfilePictureVisibility { get; init; }
}

public sealed class UpdateAvatarRequest
{
    [FromForm(Name = "avatar")]
    [Required]
    public IFormFile Avatar { get; init; } = null!;
}
