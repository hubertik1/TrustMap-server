namespace TrustMap.Domain.Entities;

public sealed class Photo
{
    private Photo()
    {
    }

    public Photo(
        Guid id,
        Guid ownerUserId,
        Guid placeId,
        Guid? placeReviewId,
        Guid? dishReviewId,
        string storageProvider,
        string storageKey,
        string publicUrl,
        string? mediumStorageKey,
        string? mediumUrl,
        string? thumbnailStorageKey,
        string? thumbnailUrl,
        string fileName,
        string contentType,
        long sizeBytes,
        int? width,
        int? height)
    {
        if ((placeReviewId.HasValue && dishReviewId.HasValue) || (!placeReviewId.HasValue && !dishReviewId.HasValue))
        {
            throw new InvalidOperationException("Photo must be attached to exactly one review.");
        }

        Id = id;
        OwnerUserId = ownerUserId;
        PlaceId = placeId;
        PlaceReviewId = placeReviewId;
        DishReviewId = dishReviewId;
        StorageProvider = storageProvider.Trim();
        StorageKey = storageKey.Trim();
        PublicUrl = publicUrl.Trim();
        MediumStorageKey = mediumStorageKey?.Trim();
        MediumUrl = mediumUrl?.Trim();
        ThumbnailStorageKey = thumbnailStorageKey?.Trim();
        ThumbnailUrl = thumbnailUrl?.Trim();
        FileName = fileName.Trim();
        ContentType = contentType.Trim();
        SizeBytes = sizeBytes;
        Width = width;
        Height = height;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public Guid PlaceId { get; private set; }

    public Guid? PlaceReviewId { get; private set; }

    public Guid? DishReviewId { get; private set; }

    public string StorageProvider { get; private set; } = null!;

    public string StorageKey { get; private set; } = null!;

    public string PublicUrl { get; private set; } = null!;

    public string? MediumStorageKey { get; private set; }

    public string? MediumUrl { get; private set; }

    public string? ThumbnailStorageKey { get; private set; }

    public string? ThumbnailUrl { get; private set; }

    public string FileName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public int? Width { get; private set; }

    public int? Height { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public User OwnerUser { get; private set; } = null!;

    public Place Place { get; private set; } = null!;

    public PlaceReview? PlaceReview { get; private set; }

    public DishReview? DishReview { get; private set; }

    public IEnumerable<string> EnumerateStorageKeys()
    {
        yield return StorageKey;

        if (!string.IsNullOrWhiteSpace(MediumStorageKey))
        {
            yield return MediumStorageKey;
        }

        if (!string.IsNullOrWhiteSpace(ThumbnailStorageKey))
        {
            yield return ThumbnailStorageKey;
        }
    }
}
