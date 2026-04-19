namespace TrustMap.Infrastructure.Storage;

public sealed record StoredPhoto(
    string StorageProvider,
    string StorageKey,
    string PublicUrl,
    string ContentType,
    long SizeBytes,
    int? Width,
    int? Height,
    StoredPhotoVariant? Medium,
    StoredPhotoVariant? Thumbnail);
