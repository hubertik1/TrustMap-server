namespace TrustMap.Domain.Entities;

public sealed class UserSavedCategory
{
    private UserSavedCategory()
    {
    }

    public UserSavedCategory(Guid userId, Guid categoryId)
    {
        UserId = userId;
        CategoryId = categoryId;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid UserId { get; private set; }

    public Guid CategoryId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public User User { get; private set; } = null!;

    public Category Category { get; private set; } = null!;
}
