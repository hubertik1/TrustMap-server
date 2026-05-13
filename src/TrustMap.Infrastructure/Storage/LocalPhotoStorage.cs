using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace TrustMap.Infrastructure.Storage;

public sealed class LocalPhotoStorage : IPhotoStorage
{
    private const int MediumMaxDimension = 1_280;
    private const int ThumbnailMaxDimension = 320;
    private const int CopyBufferSize = 81920;

    private static readonly HashSet<string> AllowedInputFormatNames =
    [
        JpegFormat.Instance.Name,
        PngFormat.Instance.Name,
        WebpFormat.Instance.Name
    ];

    private readonly PhotoStorageOptions _options;
    private readonly ILogger<LocalPhotoStorage> _logger;
    private readonly string _rootPath;
    private readonly string _rootPathWithSeparator;

    public LocalPhotoStorage(
        IOptions<PhotoStorageOptions> options,
        ILogger<LocalPhotoStorage> logger)
    {
        _options = options.Value;
        _logger = logger;
        _rootPath = Path.GetFullPath(_options.RootPath);
        _rootPathWithSeparator = EnsureTrailingSeparator(_rootPath);
    }

    public async Task<StoredPhoto> SaveAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var maxUploadBytes = GetMaxUploadBytes();
        if (stream.CanSeek && stream.Length > maxUploadBytes)
        {
            throw new PhotoStorageException(
                $"Uploaded image must be {maxUploadBytes} bytes or smaller.",
                "photo_too_large");
        }

        var inputBytes = await ReadUploadBytesAsync(stream, maxUploadBytes, cancellationToken);
        await using var imageStream = new MemoryStream(inputBytes, writable: false);

        using var image = await DecodeImageOrThrowAsync(imageStream, cancellationToken);
        EnsureAllowedInputFormat(image);

        image.Mutate(context => context.AutoOrient());
        StripMetadata(image);

        var outputFormat = ResolveOutputFormat();
        var folderSegment = DateTime.UtcNow.ToString("yyyy/MM");
        var fileId = Guid.CreateVersion7().ToString("N");
        var writtenStorageKeys = new List<string>();

        try
        {
            var originalStorageKey = BuildStorageKey(folderSegment, fileId, outputFormat.Extension);
            var originalBytes = await EncodeAsync(image, outputFormat.Encoder, cancellationToken);
            await SaveBytesAsync(originalStorageKey, originalBytes, cancellationToken);
            writtenStorageKeys.Add(originalStorageKey);

            var medium = await SaveVariantIfNeededAsync(
                image,
                folderSegment,
                fileId,
                "medium",
                MediumMaxDimension,
                outputFormat,
                writtenStorageKeys,
                cancellationToken);
            var thumbnail = await SaveVariantIfNeededAsync(
                image,
                folderSegment,
                fileId,
                "thumb",
                ThumbnailMaxDimension,
                outputFormat,
                writtenStorageKeys,
                cancellationToken);

            return new StoredPhoto(
                StorageProvider: _options.Provider,
                StorageKey: originalStorageKey,
                PublicUrl: BuildPublicUrl(originalStorageKey),
                ContentType: outputFormat.ContentType,
                SizeBytes: originalBytes.LongLength,
                Width: image.Width,
                Height: image.Height,
                Medium: medium,
                Thumbnail: thumbnail);
        }
        catch
        {
            foreach (var storageKey in writtenStorageKeys)
            {
                TryDeleteResolvedFile(storageKey);
            }

            throw;
        }
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var absolutePath = ResolveStoragePathOrThrow(storageKey);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    private static async Task<byte[]> ReadUploadBytesAsync(
        Stream stream,
        long maxUploadBytes,
        CancellationToken cancellationToken)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[CopyBufferSize];

        while (true)
        {
            var bytesRead = await stream.ReadAsync(chunk, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (buffer.Length + bytesRead > maxUploadBytes)
            {
                throw new PhotoStorageException(
                    $"Uploaded image must be {maxUploadBytes} bytes or smaller.",
                    "photo_too_large");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, bytesRead), cancellationToken);
        }

        if (buffer.Length == 0)
        {
            throw new PhotoStorageException("Attach a non-empty image file.", "photo_empty");
        }

        return buffer.ToArray();
    }

    private static async Task<Image> DecodeImageOrThrowAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await Image.LoadAsync(stream, cancellationToken);
        }
        catch (UnknownImageFormatException exception)
        {
            throw new PhotoStorageException(
                "Uploaded file is not a supported image. Upload a JPEG, PNG, or WebP image.",
                "photo_invalid_image",
                exception);
        }
        catch (InvalidImageContentException exception)
        {
            throw new PhotoStorageException(
                "Uploaded image is corrupted or invalid.",
                "photo_invalid_image",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new PhotoStorageException(
                "Uploaded file is not a supported image. Upload a JPEG, PNG, or WebP image.",
                "photo_invalid_image",
                exception);
        }
    }

    private static void EnsureAllowedInputFormat(Image image)
    {
        var decodedFormat = image.Metadata.DecodedImageFormat;
        if (decodedFormat is null || !AllowedInputFormatNames.Contains(decodedFormat.Name))
        {
            throw new PhotoStorageException(
                "Uploaded image format is not supported. Upload a JPEG, PNG, or WebP image.",
                "photo_unsupported_format");
        }
    }

    private static void StripMetadata(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.CicpProfile = null;

        foreach (var frame in image.Frames)
        {
            frame.Metadata.ExifProfile = null;
            frame.Metadata.XmpProfile = null;
            frame.Metadata.IptcProfile = null;
            frame.Metadata.IccProfile = null;
            frame.Metadata.CicpProfile = null;
        }
    }

    private OutputImageFormat ResolveOutputFormat()
    {
        var normalized = (_options.OutputFormat ?? string.Empty).Trim().ToLowerInvariant();
        var quality = Math.Clamp(_options.JpegQuality, 1, 100);

        return normalized switch
        {
            "" or "jpg" or "jpeg" => new(
                Extension: ".jpg",
                ContentType: "image/jpeg",
                Encoder: new JpegEncoder
                {
                    Quality = quality,
                    SkipMetadata = true
                }),
            "webp" => new(
                Extension: ".webp",
                ContentType: "image/webp",
                Encoder: new WebpEncoder
                {
                    Quality = quality,
                    SkipMetadata = true
                }),
            _ => throw new InvalidOperationException("PhotoStorage:OutputFormat must be 'jpeg' or 'webp'.")
        };
    }

    private static async Task<byte[]> EncodeAsync(
        Image image,
        IImageEncoder encoder,
        CancellationToken cancellationToken)
    {
        await using var output = new MemoryStream();
        await image.SaveAsync(output, encoder, cancellationToken);
        return output.ToArray();
    }

    private async Task<StoredPhotoVariant?> SaveVariantIfNeededAsync(
        Image image,
        string folderSegment,
        string fileId,
        string suffix,
        int maxDimension,
        OutputImageFormat outputFormat,
        ICollection<string> writtenStorageKeys,
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
        StripMetadata(variant);

        var bytes = await EncodeAsync(variant, outputFormat.Encoder, cancellationToken);
        var storageKey = BuildStorageKey(folderSegment, $"{fileId}-{suffix}", outputFormat.Extension);
        await SaveBytesAsync(storageKey, bytes, cancellationToken);
        writtenStorageKeys.Add(storageKey);

        return new StoredPhotoVariant(
            StorageKey: storageKey,
            PublicUrl: BuildPublicUrl(storageKey));
    }

    private async Task SaveBytesAsync(string storageKey, byte[] bytes, CancellationToken cancellationToken)
    {
        var absolutePath = ResolveStoragePathOrThrow(storageKey);
        var directory = Path.GetDirectoryName(absolutePath)!;
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(absolutePath)}.{Guid.CreateVersion7():N}.tmp");

        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);
            File.Move(temporaryPath, absolutePath, overwrite: false);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    private string ResolveStoragePathOrThrow(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey != storageKey.Trim())
        {
            throw InvalidStorageKey();
        }

        if (storageKey.Contains('\\')
            || storageKey.Contains("..", StringComparison.Ordinal)
            || storageKey.Contains(':')
            || storageKey.StartsWith("/", StringComparison.Ordinal)
            || Path.IsPathRooted(storageKey))
        {
            throw InvalidStorageKey();
        }

        var segments = storageKey.Split('/');
        if (segments.Any(segment => string.IsNullOrWhiteSpace(segment) || segment == "."))
        {
            throw InvalidStorageKey();
        }

        var relativePath = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var absolutePath = Path.GetFullPath(Path.Combine(_rootPath, relativePath));
        if (!absolutePath.StartsWith(_rootPathWithSeparator, StringComparison.Ordinal))
        {
            throw InvalidStorageKey();
        }

        return absolutePath;
    }

    private static PhotoStorageException InvalidStorageKey() =>
        new("Storage key is invalid.", "photo_invalid_storage_key");

    private void TryDeleteResolvedFile(string storageKey)
    {
        try
        {
            TryDeleteFile(ResolveStoragePathOrThrow(storageKey));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to clean up partially written photo blob {StorageKey}.", storageKey);
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private long GetMaxUploadBytes()
    {
        if (_options.MaxUploadBytes <= 0)
        {
            return PhotoStorageOptions.DefaultMaxUploadBytes;
        }

        return _options.MaxUploadBytes;
    }

    private static string BuildStorageKey(string folderSegment, string fileId, string extension) =>
        $"{folderSegment}/{fileId}{extension}";

    private string BuildPublicUrl(string storageKey) =>
        $"{_options.RequestPath.TrimEnd('/')}/{storageKey}";

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : $"{path}{Path.DirectorySeparatorChar}";

    private sealed record OutputImageFormat(
        string Extension,
        string ContentType,
        IImageEncoder Encoder);
}
