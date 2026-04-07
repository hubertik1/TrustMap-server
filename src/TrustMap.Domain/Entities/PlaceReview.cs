using TrustMap.Domain.Enums;

namespace TrustMap.Domain.Entities;

public sealed class PlaceReview
{
    private PlaceReview()
    {
    }

    public PlaceReview(
        Guid id,
        Guid placeId,
        Guid authorUserId,
        VisibilityLevel visibility,
        int rating,
        string? title,
        string? body)
    {
        Id = id;
        PlaceId = placeId;
        AuthorUserId = authorUserId;
        CreatedAt = DateTime.UtcNow;
        Update(visibility, rating, title, body);
    }

    public Guid Id { get; private set; }

    public Guid PlaceId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public VisibilityLevel Visibility { get; private set; }

    public int Rating { get; private set; }

    public string? Title { get; private set; }

    public string Body { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public Place Place { get; private set; } = null!;

    public User AuthorUser { get; private set; } = null!;

    public ICollection<DishReview> DishReviews { get; } = new List<DishReview>();

    public ICollection<Photo> Photos { get; } = new List<Photo>();

    public void Update(VisibilityLevel visibility, int rating, string? title, string? body)
    {
        if (rating is < 1 or > 5)
        {
            throw new InvalidOperationException("Rating must be between 1 and 5.");
        }

        Visibility = visibility;
        Rating = rating;
        Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim();
        Body = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
