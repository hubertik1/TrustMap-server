using TrustMap.Domain.Common;

namespace TrustMap.Domain.Entities;

public sealed class Category
{
    private Category()
    {
    }

    public Category(Guid id, string name, Guid? ownerUserId, bool isDefault, string? iconName = null)
    {
        if (isDefault && ownerUserId.HasValue)
        {
            throw new InvalidOperationException("Default categories cannot have an owner.");
        }

        Id = id;
        OwnerUserId = ownerUserId;
        IsDefault = isDefault;
        CreatedAt = DateTime.UtcNow;
        Rename(name, iconName);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string NormalizedName { get; private set; } = null!;

    public string? IconName { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public bool IsDefault { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public User? OwnerUser { get; private set; }

    public ICollection<PlaceCategoryAssignment> PlaceAssignments { get; } = new List<PlaceCategoryAssignment>();

    public ICollection<UserCategoryMembership> Memberships { get; } = new List<UserCategoryMembership>();

    public void Rename(string name, string? iconName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Category name cannot be empty.");
        }

        Name = name.Trim();
        NormalizedName = TextNormalizer.NormalizeForLookup(Name);
        IconName = string.IsNullOrWhiteSpace(iconName) ? null : iconName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void SoftDelete()
    {
        if (IsDefault)
        {
            throw new InvalidOperationException("Default categories cannot be deleted.");
        }

        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        UpdatedAt = DateTime.UtcNow;
    }
}
