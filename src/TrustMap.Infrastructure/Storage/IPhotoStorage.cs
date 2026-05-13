namespace TrustMap.Infrastructure.Storage;

public interface IPhotoStorage
{
    Task<StoredPhoto> SaveAsync(
        Stream stream,
        CancellationToken cancellationToken);

    Task<StoredFileResult?> OpenReadAsync(
        string storageKey,
        string? contentTypeHint,
        CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
