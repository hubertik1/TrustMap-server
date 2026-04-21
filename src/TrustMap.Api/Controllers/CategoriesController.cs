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
        return Ok(categories.Select(x => x.ToDto(currentUserId)).ToArray());
    }

    [HttpGet("friends")]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<CategoryDto>> GetFriendsCategories(CancellationToken cancellationToken)
    {
        _ = socialGraphService;
        _ = cancellationToken;
        return Ok(Array.Empty<CategoryDto>());
    }

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public ActionResult<CategoryDto> Create(
        [FromBody] CreateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;
        throw RestaurantsOnlyCategoryException();
    }

    [HttpPost("{id:guid}/adopt")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public ActionResult<CategoryDto> Adopt(Guid id, CancellationToken cancellationToken)
    {
        _ = id;
        _ = cancellationToken;
        throw RestaurantsOnlyCategoryException();
    }

    [HttpPatch("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    public ActionResult<CategoryDto> Update(
        Guid id,
        [FromBody] UpdateCategoryRequest request,
        CancellationToken cancellationToken)
    {
        _ = id;
        _ = request;
        _ = cancellationToken;
        throw RestaurantsOnlyCategoryException();
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Delete(Guid id, CancellationToken cancellationToken)
    {
        _ = id;
        _ = cancellationToken;
        throw RestaurantsOnlyCategoryException();
    }

    private IQueryable<Category> QueryAvailableCategories(Guid currentUserId)
    {
        _ = currentUserId;
        return dbContext.Categories
            .AsNoTracking()
            .Include(x => x.OwnerUser)
            .Where(x => x.IsDefault)
            .OrderBy(x => x.Id == CategoryAssignmentService.RestaurantsCategoryId ? 0 : 1)
            .ThenBy(x => x.Name);
    }

    private static ApiException RestaurantsOnlyCategoryException() =>
        ApiException.Forbidden(
            $"TrustMap currently supports only the {CategoryAssignmentService.RestaurantsCategoryName} category.",
            "category_locked");
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
