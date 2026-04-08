using System.ComponentModel.DataAnnotations;
using TrustMap.Api.Features.Users;

namespace TrustMap.Api.Features.Categories;

public sealed record CategoryDto(
    Guid Id,
    string Name,
    string? IconName,
    bool IsDefault,
    bool IsOwnedByCurrentUser,
    UserSummaryDto? Owner,
    DateTime CreatedAtUtc);

public sealed class CreateCategoryRequest
{
    [Required]
    [StringLength(120)]
    public string Name { get; init; } = null!;

    [StringLength(80)]
    public string? IconName { get; init; }
}
