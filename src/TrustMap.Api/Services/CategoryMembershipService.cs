using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Domain.Common;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class CategoryMembershipService(
    TrustMapDbContext dbContext,
    SocialGraphService socialGraphService)
{
    public async Task EnsureDefaultMembershipsAsync(Guid currentUserId, CancellationToken cancellationToken)
    {
        var defaultCategoryIds = await dbContext.Categories
            .AsNoTracking()
            .Where(x => x.IsDefault && !x.IsDeleted)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        if (defaultCategoryIds.Count == 0)
        {
            return;
        }

        var existingCategoryIds = await dbContext.UserCategoryMemberships
            .AsNoTracking()
            .Where(x => x.UserId == currentUserId && defaultCategoryIds.Contains(x.CategoryId))
            .Select(x => x.CategoryId)
            .ToListAsync(cancellationToken);

        var missingCategoryIds = defaultCategoryIds
            .Except(existingCategoryIds)
            .ToArray();

        if (missingCategoryIds.Length == 0)
        {
            return;
        }

        foreach (var categoryId in missingCategoryIds)
        {
            dbContext.UserCategoryMemberships.Add(
                new UserCategoryMembership(currentUserId, categoryId, CategoryMembershipSource.Default));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CategoryMembershipView>> GetMyCategoriesAsync(Guid currentUserId, CancellationToken cancellationToken)
    {
        await EnsureDefaultMembershipsAsync(currentUserId, cancellationToken);

        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);

        var memberships = await dbContext.UserCategoryMemberships
            .AsNoTracking()
            .Include(x => x.Category)
                .ThenInclude(x => x.OwnerUser)
            .Where(x =>
                x.UserId == currentUserId
                && x.IsVisible
                && !x.Category.IsDeleted)
            .ToListAsync(cancellationToken);

        return memberships
            .Where(x =>
                x.Category.IsDefault
                || x.Category.OwnerUserId == currentUserId
                || (x.Category.OwnerUserId.HasValue && friendIds.Contains(x.Category.OwnerUserId.Value)))
            .OrderBy(x => x.Category.IsDefault ? 0 : 1)
            .ThenBy(x => x.Category.Id == CategoryAssignmentService.RestaurantsCategoryId ? 0 : 1)
            .ThenBy(x => x.Category.Name)
            .Select(x => new CategoryMembershipView(x.Category, x))
            .ToArray();
    }

    public async Task<IReadOnlyList<CategoryMembershipView>> GetFriendCategoriesAsync(Guid currentUserId, CancellationToken cancellationToken)
    {
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);
        if (friendIds.Count == 0)
        {
            return [];
        }

        var friendIdList = friendIds.ToList();

        var categories = await dbContext.Categories
            .AsNoTracking()
            .Include(x => x.OwnerUser)
            .Where(x =>
                !x.IsDefault
                && !x.IsDeleted
                && x.OwnerUserId.HasValue
                && x.OwnerUserId != currentUserId
                && friendIdList.Contains(x.OwnerUserId.Value))
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        if (categories.Count == 0)
        {
            return [];
        }

        var categoryIds = categories.Select(x => x.Id).ToList();
        var memberships = await dbContext.UserCategoryMemberships
            .AsNoTracking()
            .Where(x => x.UserId == currentUserId && categoryIds.Contains(x.CategoryId))
            .ToDictionaryAsync(x => x.CategoryId, cancellationToken);

        return categories
            .Select(category => new CategoryMembershipView(category, memberships.GetValueOrDefault(category.Id)))
            .Where(x => x.Membership is null)
            .OrderBy(x => x.Category.Name)
            .ThenBy(x => x.Category.OwnerUser?.DisplayName)
            .ToArray();
    }

    public async Task<IReadOnlyList<CategoryMembershipView>> GetHiddenCategoriesAsync(Guid currentUserId, CancellationToken cancellationToken)
    {
        await EnsureDefaultMembershipsAsync(currentUserId, cancellationToken);

        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);

        var memberships = await dbContext.UserCategoryMemberships
            .AsNoTracking()
            .Include(x => x.Category)
                .ThenInclude(x => x.OwnerUser)
            .Where(x =>
                x.UserId == currentUserId
                && !x.IsVisible
                && !x.Category.IsDeleted)
            .ToListAsync(cancellationToken);

        return memberships
            .Where(x =>
                x.Category.IsDefault
                || x.Category.OwnerUserId == currentUserId
                || (x.Category.OwnerUserId.HasValue && friendIds.Contains(x.Category.OwnerUserId.Value)))
            .OrderBy(x => x.Category.IsDefault ? 0 : 1)
            .ThenBy(x => x.Category.Id == CategoryAssignmentService.RestaurantsCategoryId ? 0 : 1)
            .ThenBy(x => x.Category.Name)
            .Select(x => new CategoryMembershipView(x.Category, x))
            .ToArray();
    }

    public async Task<CategoryMembershipView> CreateCustomCategoryAsync(
        Guid currentUserId,
        string name,
        string? iconName,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeCategoryName(name);
        await EnsureNoActiveNameConflictAsync(currentUserId, normalizedName, excludeCategoryId: null, cancellationToken);

        var category = new Category(
            Guid.CreateVersion7(),
            name,
            currentUserId,
            isDefault: false,
            iconName);
        var membership = new UserCategoryMembership(
            currentUserId,
            category.Id,
            CategoryMembershipSource.OwnCustom);

        dbContext.Categories.Add(category);
        dbContext.UserCategoryMemberships.Add(membership);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await EnsureNoActiveNameConflictAsync(currentUserId, normalizedName, excludeCategoryId: null, cancellationToken);
            throw;
        }

        await dbContext.Entry(category).Reference(x => x.OwnerUser).LoadAsync(cancellationToken);
        return new CategoryMembershipView(category, membership);
    }

    public async Task<CategoryMembershipView> AdoptCategoryAsync(Guid currentUserId, Guid categoryId, CancellationToken cancellationToken)
    {
        await EnsureDefaultMembershipsAsync(currentUserId, cancellationToken);

        var category = await LoadCategoryAsync(categoryId, cancellationToken);
        if (category.IsDeleted)
        {
            throw CategoryNotFound();
        }

        if (category.IsDefault)
        {
            throw ApiException.BadRequest("Default categories are already available to every user.", "category_not_adoptable");
        }

        if (!category.OwnerUserId.HasValue || category.OwnerUserId == currentUserId)
        {
            throw ApiException.BadRequest("Only categories created by friends can be adopted.", "category_not_adoptable");
        }

        await EnsureFriendAccessAsync(currentUserId, category.OwnerUserId.Value, cancellationToken);

        var membership = await dbContext.UserCategoryMemberships
            .SingleOrDefaultAsync(
                x => x.UserId == currentUserId && x.CategoryId == categoryId,
                cancellationToken);

        if (membership is null)
        {
            membership = new UserCategoryMembership(
                currentUserId,
                categoryId,
                CategoryMembershipSource.FriendCustomAdded);
            dbContext.UserCategoryMemberships.Add(membership);
        }
        else
        {
            membership.Show();
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            membership = await dbContext.UserCategoryMemberships
                .SingleAsync(
                    x => x.UserId == currentUserId && x.CategoryId == categoryId,
                    cancellationToken);
            membership.Show();
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new CategoryMembershipView(category, membership);
    }

    public async Task<CategoryMembershipView> HideCategoryAsync(Guid currentUserId, Guid categoryId, CancellationToken cancellationToken)
    {
        await EnsureDefaultMembershipsAsync(currentUserId, cancellationToken);

        var membership = await LoadMembershipAsync(currentUserId, categoryId, cancellationToken)
            ?? throw CategoryNotFound();

        if (membership.Category.IsDeleted)
        {
            throw CategoryNotFound();
        }

        if (membership.Category.OwnerUserId == currentUserId)
        {
            throw ApiException.Forbidden("Your own custom categories cannot currently be hidden.", "category_hide_forbidden");
        }

        membership.Hide();
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CategoryMembershipView(membership.Category, membership);
    }

    public async Task<CategoryMembershipView> UnhideCategoryAsync(Guid currentUserId, Guid categoryId, CancellationToken cancellationToken)
    {
        await EnsureDefaultMembershipsAsync(currentUserId, cancellationToken);

        var membership = await LoadMembershipAsync(currentUserId, categoryId, cancellationToken)
            ?? throw ApiException.BadRequest("Category is not hidden for the current user.", "category_not_hidden");

        if (membership.Category.IsDeleted)
        {
            throw CategoryNotFound();
        }

        if (!membership.Category.IsDefault
            && membership.Category.OwnerUserId.HasValue
            && membership.Category.OwnerUserId != currentUserId)
        {
            await EnsureFriendAccessAsync(currentUserId, membership.Category.OwnerUserId.Value, cancellationToken);
        }

        membership.Show();
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CategoryMembershipView(membership.Category, membership);
    }

    public async Task<CategoryMembershipView> UpdateCategoryAsync(
        Guid currentUserId,
        Guid categoryId,
        string name,
        string? iconName,
        CancellationToken cancellationToken)
    {
        var category = await LoadCategoryAsync(categoryId, cancellationToken);
        if (category.IsDeleted)
        {
            throw CategoryNotFound();
        }

        if (category.IsDefault)
        {
            throw ApiException.Forbidden("Default categories cannot be edited.", "category_update_forbidden");
        }

        if (category.OwnerUserId != currentUserId)
        {
            throw ApiException.Forbidden("Only the owner can edit this category.", "category_update_forbidden");
        }

        var normalizedName = NormalizeCategoryName(name);
        await EnsureNoActiveNameConflictAsync(currentUserId, normalizedName, categoryId, cancellationToken);

        try
        {
            category.Rename(name, iconName);
        }
        catch (InvalidOperationException exception)
        {
            throw ApiException.BadRequest(exception.Message, "category_invalid");
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var membership = await dbContext.UserCategoryMemberships
            .SingleAsync(
                x => x.UserId == currentUserId && x.CategoryId == categoryId,
                cancellationToken);

        return new CategoryMembershipView(category, membership);
    }

    public async Task DeleteCategoryAsync(Guid currentUserId, Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await LoadCategoryAsync(categoryId, cancellationToken);
        if (category.IsDeleted)
        {
            return;
        }

        if (category.IsDefault)
        {
            throw ApiException.Forbidden("Default categories cannot be deleted.", "category_delete_forbidden");
        }

        if (category.OwnerUserId != currentUserId)
        {
            throw ApiException.Forbidden("Only the owner can delete this category.", "category_delete_forbidden");
        }

        category.SoftDelete();
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Category> GetSelectableCategoryAsync(Guid currentUserId, Guid categoryId, CancellationToken cancellationToken)
    {
        await EnsureDefaultMembershipsAsync(currentUserId, cancellationToken);

        var category = await dbContext.Categories
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == categoryId, cancellationToken);

        if (category is null || category.IsDeleted)
        {
            throw CategoryNotFound();
        }

        var membership = await dbContext.UserCategoryMemberships
            .AsNoTracking()
            .Include(x => x.Category)
                .ThenInclude(x => x.OwnerUser)
            .SingleOrDefaultAsync(
                x => x.UserId == currentUserId && x.CategoryId == categoryId,
                cancellationToken);

        if (membership is null || !membership.IsVisible)
        {
            throw ApiException.Forbidden("Category is not active for the current user.", "category_forbidden");
        }

        if (!membership.Category.IsDefault
            && membership.Category.OwnerUserId != currentUserId)
        {
            if (!membership.Category.OwnerUserId.HasValue)
            {
                throw ApiException.Forbidden("Category is not available for the current user.", "category_forbidden");
            }

            await EnsureFriendAccessAsync(currentUserId, membership.Category.OwnerUserId.Value, cancellationToken);
        }

        return membership.Category;
    }

    private async Task<Category> LoadCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        return await dbContext.Categories
            .Include(x => x.OwnerUser)
            .SingleOrDefaultAsync(x => x.Id == categoryId, cancellationToken)
            ?? throw CategoryNotFound();
    }

    private Task<UserCategoryMembership?> LoadMembershipAsync(Guid currentUserId, Guid categoryId, CancellationToken cancellationToken)
    {
        return dbContext.UserCategoryMemberships
            .Include(x => x.Category)
                .ThenInclude(x => x.OwnerUser)
            .SingleOrDefaultAsync(
                x => x.UserId == currentUserId && x.CategoryId == categoryId,
                cancellationToken);
    }

    private async Task EnsureFriendAccessAsync(Guid currentUserId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);
        if (friendIds.Contains(ownerUserId))
        {
            return;
        }

        throw ApiException.Forbidden("Category is not available for the current user.", "category_forbidden");
    }

    private async Task EnsureNoActiveNameConflictAsync(
        Guid currentUserId,
        string normalizedName,
        Guid? excludeCategoryId,
        CancellationToken cancellationToken)
    {
        var conflictingCategory = await dbContext.Categories
            .AsNoTracking()
            .Where(x =>
                !x.IsDeleted
                && x.NormalizedName == normalizedName
                && (!excludeCategoryId.HasValue || x.Id != excludeCategoryId.Value)
                && (x.IsDefault || x.OwnerUserId == currentUserId))
            .OrderByDescending(x => x.IsDefault)
            .FirstOrDefaultAsync(cancellationToken);

        if (conflictingCategory is null)
        {
            return;
        }

        var message = conflictingCategory.IsDefault
            ? "A default category with this name already exists."
            : "You already have a custom category with this name.";
        throw ApiException.Conflict(message, "category_name_conflict");
    }

    private static string NormalizeCategoryName(string name)
    {
        var normalizedName = TextNormalizer.NormalizeForLookup(name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw ApiException.BadRequest("Category name cannot be empty.", "category_invalid");
        }

        return normalizedName;
    }

    private static ApiException CategoryNotFound() =>
        ApiException.NotFound("Category not found.", "category_not_found");
}

public sealed record CategoryMembershipView(
    Category Category,
    UserCategoryMembership? Membership);
