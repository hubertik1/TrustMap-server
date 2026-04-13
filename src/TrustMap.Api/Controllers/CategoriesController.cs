using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Categories;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("categories")]
public sealed class CategoriesController(
    TrustMapDbContext dbContext,
    SocialGraphService socialGraphService) : ControllerBase
{
    [HttpGet("my")]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetMyCategories(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var categories = await QueryAvailableCategories(currentUserId)
            .ToListAsync(cancellationToken);

        var placeCountsByCategoryId = await dbContext.PlaceCategoryAssignments
            .AsNoTracking()
            .Where(x => x.AssignedByUserId == currentUserId)
            .GroupBy(x => x.CategoryId)
            .Select(x => new
            {
                CategoryId = x.Key,
                PlaceCount = x.Select(assignment => assignment.PlaceId).Distinct().Count()
            })
            .ToDictionaryAsync(x => x.CategoryId, x => x.PlaceCount, cancellationToken);

        var orderedCategories = categories
            .OrderBy(x => x.Id == CategoryAssignmentService.RestaurantsCategoryId ? 0 : 1)
            .ThenByDescending(x => placeCountsByCategoryId.GetValueOrDefault(x.Id))
            .ThenBy(x => x.Name)
            .ToArray();

        return Ok(orderedCategories.Select(x => x.ToDto(currentUserId)).ToArray());
    }

    [HttpGet("friends")]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> GetFriendsCategories(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);
        var unavailableNames = await QueryAvailableCategories(currentUserId)
            .Select(x => x.NormalizedName)
            .ToListAsync(cancellationToken);

        var categories = await dbContext.Categories
            .AsNoTracking()
            .Include(x => x.OwnerUser)
            .Where(x =>
                !x.IsDefault
                && x.OwnerUserId.HasValue
                && friendIds.Contains(x.OwnerUserId.Value)
                && !unavailableNames.Contains(x.NormalizedName))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var deduplicated = categories
            .GroupBy(x => x.NormalizedName)
            .Select(x => x.OrderBy(category => category.Name).First())
            .OrderBy(x => x.Name)
            .ToArray();

        return Ok(deduplicated.Select(x => x.ToDto(currentUserId)).ToArray());
    }

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> Create(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var normalizedName = Domain.Common.TextNormalizer.NormalizeForLookup(request.Name);

        var existing = await QueryAvailableCategories(currentUserId)
            .FirstOrDefaultAsync(x => x.NormalizedName == normalizedName, cancellationToken);

        if (existing is not null)
        {
            return Ok(existing.ToDto(currentUserId));
        }

        var category = new Category(Guid.CreateVersion7(), request.Name, currentUserId, isDefault: false, request.IconName);
        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);

        var created = await dbContext.Categories
            .AsNoTracking()
            .Include(x => x.OwnerUser)
            .SingleAsync(x => x.Id == category.Id, cancellationToken);

        return Created($"/categories/{created.Id}", created.ToDto(currentUserId));
    }

    [HttpPost("{id:guid}/adopt")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> Adopt(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);

        var category = await dbContext.Categories
            .Include(x => x.OwnerUser)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Category not found.", "category_not_found");

        if (category.IsDefault || !category.OwnerUserId.HasValue || !friendIds.Contains(category.OwnerUserId.Value))
        {
            throw ApiException.Forbidden("You can add only categories created by your friends.", "category_forbidden");
        }

        var existing = await QueryAvailableCategories(currentUserId)
            .FirstOrDefaultAsync(x => x.NormalizedName == category.NormalizedName, cancellationToken);

        if (existing is not null)
        {
            return Ok(existing.ToDto(currentUserId));
        }

        dbContext.UserSavedCategories.Add(new UserSavedCategory(currentUserId, category.Id));
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(category.ToDto(currentUserId));
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CategoryDto>> Update(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var category = await dbContext.Categories
            .Include(x => x.OwnerUser)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Category not found.", "category_not_found");

        if (category.IsDefault || category.OwnerUserId != currentUserId)
        {
            throw ApiException.Forbidden("Only your custom categories can be edited.", "category_forbidden");
        }

        var normalizedName = Domain.Common.TextNormalizer.NormalizeForLookup(request.Name);
        var duplicateExists = await QueryAvailableCategories(currentUserId)
            .AnyAsync(x => x.Id != id && x.NormalizedName == normalizedName, cancellationToken);

        if (duplicateExists)
        {
            throw ApiException.Conflict("A category with this name already exists in your list.", "category_name_taken");
        }

        category.Rename(request.Name, request.IconName);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(category.ToDto(currentUserId));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var category = await dbContext.Categories
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Category not found.", "category_not_found");

        if (category.IsDefault)
        {
            throw ApiException.Forbidden("Default categories cannot be deleted.", "category_forbidden");
        }

        if (category.OwnerUserId == currentUserId)
        {
            dbContext.Categories.Remove(category);
            await dbContext.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        var savedCategory = await dbContext.UserSavedCategories
            .SingleOrDefaultAsync(x => x.UserId == currentUserId && x.CategoryId == id, cancellationToken);

        if (savedCategory is null)
        {
            throw ApiException.Forbidden("Only your categories can be removed from this list.", "category_forbidden");
        }

        dbContext.UserSavedCategories.Remove(savedCategory);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private IQueryable<Category> QueryAvailableCategories(Guid currentUserId)
    {
        var savedCategoryIds = dbContext.UserSavedCategories
            .Where(x => x.UserId == currentUserId)
            .Select(x => x.CategoryId);

        return dbContext.Categories
            .AsNoTracking()
            .Include(x => x.OwnerUser)
            .Where(x =>
                x.IsDefault
                || x.OwnerUserId == currentUserId
                || savedCategoryIds.Contains(x.Id));
    }
}

internal static class CategoryDtoMapper
{
    public static CategoryDto ToDto(this Category category, Guid currentUserId) =>
        new(
            category.Id,
            category.Name,
            category.IconName,
            category.IsDefault,
            category.OwnerUserId == currentUserId,
            category.OwnerUser is null ? null : category.OwnerUser.ToSummaryDto(),
            category.CreatedAt);
}
