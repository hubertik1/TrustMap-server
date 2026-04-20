using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class CategoryAssignmentService(TrustMapDbContext dbContext)
{
    public static readonly Guid RestaurantsCategoryId = Guid.Parse("D53A109F-9617-4A0A-B95A-5AD277A30764");
    public const string RestaurantsCategoryName = "Restaurants";

    public async Task<Category> GetAccessibleCategoryAsync(Guid currentUserId, Guid categoryId, CancellationToken cancellationToken)
    {
        if (categoryId != RestaurantsCategoryId)
        {
            throw ApiException.BadRequest(
                $"TrustMap currently supports only the {RestaurantsCategoryName} category.",
                "category_not_supported");
        }

        var category = await dbContext.Categories
            .Include(x => x.OwnerUser)
            .SingleOrDefaultAsync(x => x.Id == categoryId, cancellationToken)
            ?? throw ApiException.NotFound("Category not found.", "category_not_found");

        if (category.IsDefault)
        {
            return category;
        }

        throw ApiException.Forbidden("Select the default Restaurants category.", "category_forbidden");
    }

    public async Task UpsertPlaceAssignmentAsync(
        Guid currentUserId,
        Guid placeId,
        Guid categoryId,
        CancellationToken cancellationToken)
    {
        await GetAccessibleCategoryAsync(currentUserId, categoryId, cancellationToken);

        var assignment = await dbContext.PlaceCategoryAssignments.SingleOrDefaultAsync(
            x => x.PlaceId == placeId && x.AssignedByUserId == currentUserId,
            cancellationToken);

        if (assignment is null)
        {
            dbContext.PlaceCategoryAssignments.Add(
                new PlaceCategoryAssignment(Guid.CreateVersion7(), placeId, RestaurantsCategoryId, currentUserId));
            return;
        }

        assignment.UpdateCategory(RestaurantsCategoryId);
    }

    public async Task RemoveAssignmentIfUnusedAsync(Guid currentUserId, Guid placeId, CancellationToken cancellationToken)
    {
        var hasAnyReviews = await dbContext.PlaceReviews.AnyAsync(
                x => x.PlaceId == placeId && x.AuthorUserId == currentUserId,
                cancellationToken)
            || await dbContext.DishReviews.AnyAsync(
                x => x.PlaceId == placeId && x.AuthorUserId == currentUserId,
                cancellationToken);

        if (hasAnyReviews)
        {
            return;
        }

        var assignment = await dbContext.PlaceCategoryAssignments.SingleOrDefaultAsync(
            x => x.PlaceId == placeId && x.AssignedByUserId == currentUserId,
            cancellationToken);

        if (assignment is not null)
        {
            dbContext.PlaceCategoryAssignments.Remove(assignment);
        }
    }
}
