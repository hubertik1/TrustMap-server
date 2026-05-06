using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Categories;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Services;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("categories")]
public sealed class CategoriesController(
    CategoryMembershipService categoryMembershipService,
    PrivacyAccessService privacyAccessService) : ControllerBase
{
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetMyCategories(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var categories = await categoryMembershipService.GetMyCategoriesAsync(currentUserId, cancellationToken);
        var accessMap = await BuildOwnerAccessMapAsync(currentUserId, categories, cancellationToken);
        return Ok(categories.Select(x => x.ToDto(currentUserId, accessMap)).ToArray());
    }

    [HttpGet("friends")]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetFriendsCategories(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var categories = await categoryMembershipService.GetFriendCategoriesAsync(currentUserId, cancellationToken);
        var accessMap = await BuildOwnerAccessMapAsync(currentUserId, categories, cancellationToken);
        return Ok(categories.Select(x => x.ToDto(currentUserId, accessMap)).ToArray());
    }

    [HttpGet("hidden")]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetHiddenCategories(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var categories = await categoryMembershipService.GetHiddenCategoriesAsync(currentUserId, cancellationToken);
        var accessMap = await BuildOwnerAccessMapAsync(currentUserId, categories, cancellationToken);
        return Ok(categories.Select(x => x.ToDto(currentUserId, accessMap)).ToArray());
    }

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CategoryDto>> Create(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var category = await categoryMembershipService.CreateCustomCategoryAsync(
            currentUserId,
            request.Name,
            request.IconName,
            cancellationToken);

        var accessMap = await BuildOwnerAccessMapAsync(currentUserId, [category], cancellationToken);
        return Created($"/categories/{category.Category.Id}", category.ToDto(currentUserId, accessMap));
    }

    [HttpPost("{id:guid}/adopt")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> Adopt(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var category = await categoryMembershipService.AdoptCategoryAsync(currentUserId, id, cancellationToken);
        var accessMap = await BuildOwnerAccessMapAsync(currentUserId, [category], cancellationToken);
        return Ok(category.ToDto(currentUserId, accessMap));
    }

    [HttpPost("{id:guid}/hide")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> Hide(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var category = await categoryMembershipService.HideCategoryAsync(currentUserId, id, cancellationToken);
        var accessMap = await BuildOwnerAccessMapAsync(currentUserId, [category], cancellationToken);
        return Ok(category.ToDto(currentUserId, accessMap));
    }

    [HttpPost("{id:guid}/unhide")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> Unhide(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var category = await categoryMembershipService.UnhideCategoryAsync(currentUserId, id, cancellationToken);
        var accessMap = await BuildOwnerAccessMapAsync(currentUserId, [category], cancellationToken);
        return Ok(category.ToDto(currentUserId, accessMap));
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> Update(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var category = await categoryMembershipService.UpdateCategoryAsync(
            currentUserId,
            id,
            request.Name,
            request.IconName,
            cancellationToken);

        var accessMap = await BuildOwnerAccessMapAsync(currentUserId, [category], cancellationToken);
        return Ok(category.ToDto(currentUserId, accessMap));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        await categoryMembershipService.DeleteCategoryAsync(currentUserId, id, cancellationToken);
        return NoContent();
    }

    private Task<Dictionary<Guid, ViewerAccess>> BuildOwnerAccessMapAsync(
        Guid currentUserId,
        IReadOnlyCollection<CategoryMembershipView> categories,
        CancellationToken cancellationToken)
    {
        var ownerIds = categories
            .Select(x => x.Category.OwnerUserId)
            .OfType<Guid>()
            .ToArray();

        return privacyAccessService.GetAccessMapAsync(currentUserId, ownerIds, cancellationToken);
    }
}

internal static class CategoryDtoMapper
{
    public static CategoryDto ToDto(
        this CategoryMembershipView view,
        Guid currentUserId,
        IReadOnlyDictionary<Guid, ViewerAccess> ownerAccessMap)
    {
        var category = view.Category;
        var membership = view.Membership;
        var isOwnedByCurrentUser = category.OwnerUserId == currentUserId;
        var isVisibleForCurrentUser = membership?.IsVisible ?? false;

        return new CategoryDto(
            category.Id,
            category.Name,
            category.IconName,
            category.OwnerUserId,
            category.IsDefault,
            isOwnedByCurrentUser,
            category.OwnerUser is null
                ? null
                : category.OwnerUser.ToSummaryDto(
                    ownerAccessMap.GetValueOrDefault(category.OwnerUser.Id)?.CanAccess(category.OwnerUser.ProfilePictureVisibility) ?? false),
            membership?.Source,
            isVisibleForCurrentUser,
            membership?.Source == CategoryMembershipSource.FriendCustomAdded,
            !category.IsDeleted && !category.IsDefault && isOwnedByCurrentUser,
            !category.IsDeleted && !category.IsDefault && isOwnedByCurrentUser,
            !category.IsDeleted && membership?.IsVisible == true && (!isOwnedByCurrentUser || category.IsDefault),
            !category.IsDeleted && !category.IsDefault && !isOwnedByCurrentUser && !isVisibleForCurrentUser,
            !category.IsDeleted && category.IsDefault && membership?.IsVisible == false,
            category.CreatedAt,
            membership?.AddedAt,
            membership?.HiddenAt);
    }
}
