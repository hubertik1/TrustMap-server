using TrustMap.Domain.Enums;

namespace TrustMap.Domain.Entities;

public sealed class UserCategoryMembership
{
    private UserCategoryMembership()
    {
    }

    public UserCategoryMembership(Guid userId, Guid categoryId, CategoryMembershipSource source, bool isVisible = true)
    {
        UserId = userId;
        CategoryId = categoryId;
        Source = source;
        IsVisible = isVisible;
        AddedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
        HiddenAt = isVisible ? null : DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }

    public Guid CategoryId { get; private set; }

    public CategoryMembershipSource Source { get; private set; }

    public bool IsVisible { get; private set; }

    public DateTime AddedAt { get; private set; }

    public DateTime? HiddenAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public User User { get; private set; } = null!;

    public Category Category { get; private set; } = null!;

    public void Show()
    {
        if (IsVisible)
        {
            return;
        }

        IsVisible = true;
        HiddenAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Hide()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        HiddenAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
