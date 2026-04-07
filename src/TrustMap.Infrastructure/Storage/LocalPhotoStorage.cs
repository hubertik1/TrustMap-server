using Microsoft.Extensions.Options;

namespace TrustMap.Infrastructure.Storage;

public sealed class LocalPhotoStorage(IOptions<PhotoStorageOptions> options) : IPhotoStorage
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/heic",
        "image/webp"
    ];

    private readonly PhotoStorageOptions _options = options.Value;

    public async Task<StoredPhoto> SaveAsync(
        Stream stream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (!AllowedContentTypes.Contains(contentType))
        {
            throw new InvalidOperationException("Unsupported image content type.");
        }

        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = contentType switch
            {
                "image/png" => ".png",
                "image/webp" => ".webp",
                "image/heic" => ".heic",
                _ => ".jpg"
            };
        }

        var folderSegment = DateTime.UtcNow.ToString("yyyy/MM");
        var storageKey = Path.Combine(folderSegment, $"{Guid.CreateVersion7():N}{extension}").Replace("\\", "/");
        var absolutePath = Path.Combine(_options.RootPath, storageKey);
        var directory = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(directory);

        await using var targetStream = File.Create(absolutePath);
        await stream.CopyToAsync(targetStream, cancellationToken);

        var fileInfo = new FileInfo(absolutePath);
        return new StoredPhoto(
            StorageProvider: _options.Provider,
            StorageKey: storageKey,
            PublicUrl: $"{_options.RequestPath.TrimEnd('/')}/{storageKey}",
            ContentType: contentType,
            SizeBytes: fileInfo.Length,
            Width: null,
            Height: null);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        var absolutePath = Path.Combine(_options.RootPath, storageKey);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }
}
