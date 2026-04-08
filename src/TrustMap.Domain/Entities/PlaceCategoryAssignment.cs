namespace TrustMap.Domain.Entities;

public sealed class PlaceCategoryAssignment
{
    private PlaceCategoryAssignment()
    {
    }

    public PlaceCategoryAssignment(Guid id, Guid placeId, Guid categoryId, Guid assignedByUserId)
    {
        Id = id;
        PlaceId = placeId;
        CategoryId = categoryId;
        AssignedByUserId = assignedByUserId;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid PlaceId { get; private set; }

    public Guid CategoryId { get; private set; }

    public Guid AssignedByUserId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public Place Place { get; private set; } = null!;

    public Category Category { get; private set; } = null!;

    public User AssignedByUser { get; private set; } = null!;

    public void UpdateCategory(Guid categoryId)
    {
        CategoryId = categoryId;
        UpdatedAt = DateTime.UtcNow;
    }
}
