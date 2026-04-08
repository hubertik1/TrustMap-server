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
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.OwnerUserId == currentUserId ? 0 : 1)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Ok(categories.Select(x => x.ToDto(currentUserId)).ToArray());
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
