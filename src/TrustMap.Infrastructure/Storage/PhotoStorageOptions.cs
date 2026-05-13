namespace TrustMap.Infrastructure.Storage;

public sealed class PhotoStorageOptions
{
    public const string SectionName = "PhotoStorage";

    public const long DefaultMaxUploadBytes = 8_388_608;

    public string Provider { get; set; } = "local";

    public string RootPath { get; set; } = "storage/photos";

    public string RequestPath { get; set; } = "/uploads";

    public long MaxUploadBytes { get; set; } = DefaultMaxUploadBytes;

    public string OutputFormat { get; set; } = "jpeg";

    public int JpegQuality { get; set; } = 85;
}
