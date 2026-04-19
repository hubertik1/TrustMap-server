using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace TrustMap.Infrastructure.Storage;

public sealed class LocalPhotoStorage(IOptions<PhotoStorageOptions> options) : IPhotoStorage
{
    private const int MediumMaxDimension = 1_280;
    private const int ThumbnailMaxDimension = 320;

    private static readonly HashSet<string> AllowedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/heic",
        "image/webp"
    ];

    private static readonly JpegEncoder MediumEncoder = new()
    {
        Quality = 78
    };

    private static readonly JpegEncoder ThumbnailEncoder = new()
    {
        Quality = 70
    };

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

        await using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        var originalBytes = buffer.ToArray();

        var extension = ResolveExtension(fileName, contentType);
        var folderSegment = DateTime.UtcNow.ToString("yyyy/MM");
        var fileId = Guid.CreateVersion7().ToString("N");

        var originalStorageKey = BuildStorageKey(folderSegment, fileId, extension);
        await SaveBytesAsync(originalStorageKey, originalBytes, cancellationToken);

        var width = default(int?);
        var height = default(int?);
        StoredPhotoVariant? medium = null;
        StoredPhotoVariant? thumbnail = null;

        try
        {
            await using var imageStream = new MemoryStream(originalBytes, writable: false);
            using var image = await Image.LoadAsync(imageStream, cancellationToken);
            image.Mutate(context => context.AutoOrient());

            width = image.Width;
            height = image.Height;
            medium = await SaveVariantIfNeededAsync(
                image,
                folderSegment,
                fileId,
                "medium",
                MediumMaxDimension,
                MediumEncoder,
                cancellationToken);
            thumbnail = await SaveVariantIfNeededAsync(
                image,
                folderSegment,
                fileId,
                "thumb",
                ThumbnailMaxDimension,
                ThumbnailEncoder,
                cancellationToken);
        }
        catch
        {
            // Keep the upload usable even when a variant can't be generated.
        }

        return new StoredPhoto(
            StorageProvider: _options.Provider,
            StorageKey: originalStorageKey,
            PublicUrl: BuildPublicUrl(originalStorageKey),
            ContentType: contentType,
            SizeBytes: originalBytes.LongLength,
            Width: width,
            Height: height,
            Medium: medium,
            Thumbnail: thumbnail);
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

    private string ResolveExtension(string fileName, string contentType)
    {
        var extension = Path.GetExtension(fileName);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension;
        }

        return contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            _ => ".jpg"
        };
    }

    private async Task<StoredPhotoVariant?> SaveVariantIfNeededAsync(
        Image image,
        string folderSegment,
        string fileId,
        string suffix,
        int maxDimension,
        JpegEncoder encoder,
        CancellationToken cancellationToken)
    {
        if (image.Width <= maxDimension && image.Height <= maxDimension)
        {
            return null;
        }

        using var variant = image.Clone(context => context.Resize(new ResizeOptions
        {
            Mode = ResizeMode.Max,
            Size = new Size(maxDimension, maxDimension)
        }));

        await using var output = new MemoryStream();
        await variant.SaveAsJpegAsync(output, encoder, cancellationToken);
        var bytes = output.ToArray();

        var storageKey = BuildStorageKey(folderSegment, $"{fileId}-{suffix}", ".jpg");
        await SaveBytesAsync(storageKey, bytes, cancellationToken);

        return new StoredPhotoVariant(
            StorageKey: storageKey,
            PublicUrl: BuildPublicUrl(storageKey));
    }

    private async Task SaveBytesAsync(string storageKey, byte[] bytes, CancellationToken cancellationToken)
    {
        var absolutePath = Path.Combine(_options.RootPath, storageKey);
        var directory = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(absolutePath, bytes, cancellationToken);
    }

    private string BuildStorageKey(string folderSegment, string fileId, string extension) =>
        Path.Combine(folderSegment, $"{fileId}{extension}").Replace("\\", "/");

    private string BuildPublicUrl(string storageKey) =>
        $"{_options.RequestPath.TrimEnd('/')}/{storageKey}";
}
