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

    [StringLength(500)]
    public string? AvatarUrl { get; init; }
}

public sealed class UpdateMePrivacyRequest
{
    public VisibilityLevel ReviewVisibility { get; init; }

    public VisibilityLevel FriendListVisibility { get; init; }

    public VisibilityLevel ProfileVisibility { get; init; }

    public VisibilityLevel ProfilePictureVisibility { get; init; }
}

public enum AvatarUpdateAction
{
    Keep = 0,
    Remove = 1,
    Replace = 2
}

public sealed class UpdateMeMultipartRequest
{
    [FromForm(Name = "handle")]
    [Required]
    [StringLength(32, MinimumLength = 3)]
    public string Handle { get; init; } = null!;

    [FromForm(Name = "displayName")]
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string DisplayName { get; init; } = null!;

    [FromForm(Name = "bio")]
    [StringLength(500)]
    public string? Bio { get; init; }

    [FromForm(Name = "avatarAction")]
    public AvatarUpdateAction AvatarAction { get; init; } = AvatarUpdateAction.Keep;

    [FromForm(Name = "avatar")]
    public IFormFile? Avatar { get; init; }
}
