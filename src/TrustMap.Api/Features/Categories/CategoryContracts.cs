using System.ComponentModel.DataAnnotations;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Features.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string? IconName,
    Guid? OwnerUserId,
    bool IsDefault,
    bool IsOwnedByCurrentUser,
    UserSummaryDto? Owner,
    CategoryMembershipSource? CurrentUserSource,
    bool IsVisibleForCurrentUser,
    bool IsAdoptedByCurrentUser,
    bool CanEdit,
    bool CanDelete,
    bool CanHide,
    bool CanAdopt,
    bool CanUnhide,
    DateTime CreatedAtUtc,
    DateTime? AddedAtUtc,
    DateTime? HiddenAtUtc);

public sealed class CreateCategoryRequest
{
    [Required]
    [StringLength(120)]
    public string Name { get; init; } = null!;

    [StringLength(80)]
    public string? IconName { get; init; }
}

public sealed class UpdateCategoryRequest
{
    [Required]
    [StringLength(120)]
    public string Name { get; init; } = null!;

    [StringLength(80)]
    public string? IconName { get; init; }
}
