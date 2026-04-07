namespace TrustMap.Infrastructure.Storage;

public interface IPhotoStorage
{
    Task<StoredPhoto> SaveAsync(
        Stream stream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}
