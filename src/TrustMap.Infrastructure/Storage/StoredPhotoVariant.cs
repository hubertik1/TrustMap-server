namespace TrustMap.Infrastructure.Storage;

public sealed record StoredPhotoVariant(
    string StorageKey,
    string PublicUrl);
