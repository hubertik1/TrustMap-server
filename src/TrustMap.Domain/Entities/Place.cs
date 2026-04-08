using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrustMap.Domain.Common;

namespace TrustMap.Domain.Entities;

public sealed class Place
{
    private Place()
    {
    }

    public Place(
        Guid id,
        string name,
        string address,
        string? city,
        string? countryCode,
        double latitude,
        double longitude,
        string? provider,
        string? providerPlaceId,
        Guid? createdByUserId)
    {
        Id = id;
        CreatedByUserId = createdByUserId;
        CreatedAt = DateTime.UtcNow;
        Update(name, address, city, countryCode, latitude, longitude, provider, providerPlaceId);
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string NormalizedName { get; private set; } = null!;

    public string Address { get; private set; } = null!;

    public string? City { get; private set; }

    public string? CountryCode { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public string IdentityFingerprint { get; private set; } = null!;

    public string? Provider { get; private set; }

    public string? ProviderPlaceId { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public User? CreatedByUser { get; private set; }

    public ICollection<PlaceReview> PlaceReviews { get; } = new List<PlaceReview>();

    public ICollection<DishReview> DishReviews { get; } = new List<DishReview>();

    public ICollection<Photo> Photos { get; } = new List<Photo>();

    public ICollection<PlaceCategoryAssignment> CategoryAssignments { get; } = new List<PlaceCategoryAssignment>();

    public void Update(
        string name,
        string address,
        string? city,
        string? countryCode,
        double latitude,
        double longitude,
        string? provider,
        string? providerPlaceId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Place name cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException("Place address cannot be empty.");
        }

        Name = name.Trim();
        NormalizedName = TextNormalizer.NormalizeForLookup(Name);
        Address = address.Trim();
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        CountryCode = string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant();
        Latitude = latitude;
        Longitude = longitude;
        Provider = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim().ToLowerInvariant();
        ProviderPlaceId = string.IsNullOrWhiteSpace(providerPlaceId) ? null : providerPlaceId.Trim();
        IdentityFingerprint = BuildFingerprint(Name, Address, City, CountryCode, latitude, longitude);
        UpdatedAt = DateTime.UtcNow;
    }

    public static string BuildFingerprint(
        string name,
        string address,
        string? city,
        string? countryCode,
        double latitude,
        double longitude)
    {
        var normalized = string.Join(
            "|",
            TextNormalizer.NormalizeForLookup(name),
            TextNormalizer.NormalizeForLookup(address),
            TextNormalizer.NormalizeForLookup(city),
            TextNormalizer.NormalizeForLookup(countryCode),
            Math.Round(latitude, 6).ToString("F6", CultureInfo.InvariantCulture),
            Math.Round(longitude, 6).ToString("F6", CultureInfo.InvariantCulture));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexStringLower(hash);
    }
}
