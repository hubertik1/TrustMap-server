using TrustMap.Domain.Common;
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

    public bool HasChanges(
        VisibilityLevel visibility,
        string dishName,
        int rating,
        string? body,
        decimal? priceAmount,
        string? currencyCode)
    {
        EnumGuard.EnsureDefined(visibility, nameof(visibility));
        EnsureValidDishName(dishName);
        EnsureValidRating(rating);

        return Visibility != visibility
            || DishName != NormalizeDishName(dishName)
            || Rating != rating
            || Body != NormalizeBody(body)
            || PriceAmount != priceAmount
            || CurrencyCode != NormalizeCurrencyCode(currencyCode);
    }

    public bool Update(
        VisibilityLevel visibility,
        string dishName,
        int rating,
        string? body,
        decimal? priceAmount,
        string? currencyCode)
    {
        EnumGuard.EnsureDefined(visibility, nameof(visibility));
        EnsureValidDishName(dishName);
        EnsureValidRating(rating);

        var normalizedDishName = NormalizeDishName(dishName);
        var normalizedBody = NormalizeBody(body);
        var normalizedCurrencyCode = NormalizeCurrencyCode(currencyCode);

        if (Visibility == visibility
            && DishName == normalizedDishName
            && Rating == rating
            && Body == normalizedBody
            && PriceAmount == priceAmount
            && CurrencyCode == normalizedCurrencyCode)
        {
            return false;
        }

        Visibility = visibility;
        DishName = normalizedDishName;
        Rating = rating;
        Body = normalizedBody;
        PriceAmount = priceAmount;
        CurrencyCode = normalizedCurrencyCode;
        UpdatedAt = DateTime.UtcNow;

        return true;
    }

    public void Touch()
    {
        UpdatedAt = DateTime.UtcNow;
    }

    private static void EnsureValidDishName(string dishName)
    {
        if (string.IsNullOrWhiteSpace(dishName))
        {
            throw new InvalidOperationException("Dish name cannot be empty.");
        }
    }

    private static void EnsureValidRating(int rating)
    {
        if (rating is < 1 or > 5)
        {
            throw new InvalidOperationException("Rating must be between 1 and 5.");
        }
    }

    private static string NormalizeDishName(string dishName) => dishName.Trim();

    private static string NormalizeBody(string? body) =>
        string.IsNullOrWhiteSpace(body) ? string.Empty : body.Trim();

    private static string? NormalizeCurrencyCode(string? currencyCode) =>
        string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode.Trim().ToUpperInvariant();
}
