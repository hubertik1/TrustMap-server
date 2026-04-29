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

    public bool HasChanges(VisibilityLevel visibility, int rating, string? title, string? body)
    {
        EnsureValidRating(rating);

        return Visibility != visibility
            || Rating != rating
            || Title != NormalizeTitle(title)
            || Body != NormalizeBody(body);
    }

    public bool Update(VisibilityLevel visibility, int rating, string? title, string? body)
    {
        EnsureValidRating(rating);

        var normalizedTitle = NormalizeTitle(title);
        var normalizedBody = NormalizeBody(body);

        if (Visibility == visibility
            && Rating == rating
            && Title == normalizedTitle
            && Body == normalizedBody)
        {
            return false;
        }

        Visibility = visibility;
        Rating = rating;
        Title = normalizedTitle;
        Body = normalizedBody;
        UpdatedAt = DateTime.UtcNow;

        return true;
    }

    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }

    private static void EnsureValidRating(int rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new InvalidOperationException("Rating must be between 1 and 5.");
        }
    }

    private static string? NormalizeTitle(string? title) =>
        string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    private static string NormalizeBody(string? body) =>
        string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
}
