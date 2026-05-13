using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TrustMap.Domain.Common;
using TrustMap.Domain.Enums;

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
        CreatedAt = DateTime.UtcNow;
        Update(name, address, city, countryCode, latitude, longitude, provider, providerPlaceId);
        CreatedByUserId = IsCustomPin
            ? createdByUserId ?? throw new InvalidOperationException("Custom places must have a creator.")
            : null;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string NormalizedName { get; private set; } = null!;

    public string Address { get; private set; } = null!;

    public string NormalizedAddress { get; private set; } = null!;

    public string? City { get; private set; }

    public string? CountryCode { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public string IdentityFingerprint { get; private set; } = null!;

    public string? Provider { get; private set; }

    public string? ProviderPlaceId { get; private set; }

    public PlaceSourceType SourceType { get; private set; }

    public bool IsPubliclyListed { get; private set; }

    public string? CustomDisplayName { get; private set; }

    public string? NormalizedCustomDisplayName { get; private set; }

    public Guid? CustomDisplayNameSetByUserId { get; private set; }

    public DateTime? CustomDisplayNameUpdatedAt { get; private set; }

    public Guid? CreatedByUserId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    public User? CreatedByUser { get; private set; }

    public User? CustomDisplayNameSetByUser { get; private set; }

    public ICollection<PlaceReview> PlaceReviews { get; } = new List<PlaceReview>();

    public ICollection<DishReview> DishReviews { get; } = new List<DishReview>();

    public ICollection<Photo> Photos { get; } = new List<Photo>();

    public ICollection<PlaceCategoryAssignment> CategoryAssignments { get; } = new List<PlaceCategoryAssignment>();

    public bool IsCustomPin => SourceType == PlaceSourceType.CustomPin;

    public bool IsPublicCatalogPlace => IsPubliclyListed || IsPublicCatalogSource(SourceType);

    public string DisplayName => ResolveDisplayName(SourceType, Name, Address, CustomDisplayName);

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
        NormalizedAddress = TextNormalizer.NormalizeForLookup(Address);
        City = string.IsNullOrWhiteSpace(city) ? null : city.Trim();
        CountryCode = string.IsNullOrWhiteSpace(countryCode) ? null : countryCode.Trim().ToUpperInvariant();
        Latitude = latitude;
        Longitude = longitude;
        SourceType = ResolveSourceType(provider, providerPlaceId);
        IsPubliclyListed = IsPublicCatalogSource(SourceType);
        Provider = SourceType == PlaceSourceType.ProviderVenue
            ? provider!.Trim().ToLowerInvariant()
            : null;
        ProviderPlaceId = SourceType == PlaceSourceType.ProviderVenue
            ? providerPlaceId!.Trim()
            : null;
        IdentityFingerprint = BuildFingerprint(Name, Address, City, CountryCode, latitude, longitude);
        UpdatedAt = DateTime.UtcNow;
    }

    public bool CanEditCustomDisplayName(Guid userId) =>
        IsCustomPin
        && CreatedByUserId.HasValue
        && CreatedByUserId.Value == userId;

    public void SetCustomDisplayName(Guid userId, string customDisplayName)
    {
        if (!IsCustomPin)
        {
            throw new InvalidOperationException("Only custom map pins can be renamed.");
        }

        if (!CanEditCustomDisplayName(userId))
        {
            throw new InvalidOperationException("Only the original creator can rename this custom place.");
        }

        if (string.IsNullOrWhiteSpace(customDisplayName))
        {
            throw new InvalidOperationException("Custom place name cannot be empty.");
        }

        CustomDisplayName = customDisplayName.Trim();
        NormalizedCustomDisplayName = TextNormalizer.NormalizeForLookup(CustomDisplayName);
        CustomDisplayNameSetByUserId = userId;
        CustomDisplayNameUpdatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public static PlaceSourceType ResolveSourceType(string? provider, string? providerPlaceId)
    {
        return !string.IsNullOrWhiteSpace(provider)
               && !string.IsNullOrWhiteSpace(providerPlaceId)
               && !string.Equals(provider.Trim(), "manual", StringComparison.OrdinalIgnoreCase)
            ? PlaceSourceType.ProviderVenue
            : PlaceSourceType.CustomPin;
    }

    public static bool IsPublicCatalogSource(PlaceSourceType sourceType) =>
        sourceType is PlaceSourceType.ProviderVenue or PlaceSourceType.ApprovedPublic;

    public static string ResolveDisplayName(
        PlaceSourceType sourceType,
        string name,
        string address,
        string? customDisplayName)
    {
        if (!string.IsNullOrWhiteSpace(customDisplayName))
        {
            return customDisplayName.Trim();
        }

        return sourceType == PlaceSourceType.CustomPin
            ? address.Trim()
            : name.Trim();
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
