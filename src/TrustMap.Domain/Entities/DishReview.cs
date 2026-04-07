using TrustMap.Domain.Enums;

namespace TrustMap.Domain.Entities;

public sealed class DishReview
{
    private DishReview()
    {
    }

    public DishReview(
        Guid id,
        Guid placeId,
        Guid authorUserId,
        Guid? placeReviewId,
        VisibilityLevel visibility,
        string dishName,
        int rating,
        string? body,
        decimal? priceAmount,
        string? currencyCode)
    {
        Id = id;
        PlaceId = placeId;
        AuthorUserId = authorUserId;
        PlaceReviewId = placeReviewId;
        CreatedAt = DateTime.UtcNow;
        Update(visibility, dishName, rating, body, priceAmount, currencyCode);
    }

    public Guid Id { get; private set; }

    public Guid PlaceId { get; private set; }

    public Guid AuthorUserId { get; private set; }

    public Guid? PlaceReviewId { get; private set; }

    public VisibilityLevel Visibility { get; private set; }

    public string DishName { get; private set; } = null!;

    public int Rating { get; private set; }

    public string Body { get; private set; } = null!;

    public decimal? PriceAmount { get; private set; }

    public string? CurrencyCode { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public Place Place { get; private set; } = null!;

    public User AuthorUser { get; private set; } = null!;

    public PlaceReview? PlaceReview { get; private set; }

    public ICollection<Photo> Photos { get; } = new List<Photo>();

    public void Update(
        VisibilityLevel visibility,
        string dishName,
        int rating,
        string? body,
        decimal? priceAmount,
        string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(dishName))
        {
            throw new InvalidOperationException("Dish name cannot be empty.");
        }

        if (rating is < 1 or > 5)
        {
            throw new InvalidOperationException("Rating must be between 1 and 5.");
        }

        Visibility = visibility;
        DishName = dishName.Trim();
        Rating = rating;
        Body = string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();
        PriceAmount = priceAmount;
        CurrencyCode = string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant();
        UpdatedAt = DateTime.UtcNow;
    }
}
