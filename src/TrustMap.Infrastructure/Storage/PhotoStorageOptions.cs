namespace TrustMap.Infrastructure.Storage;

public sealed class PhotoStorageOptions
{
    public const string SectionName = "PhotoStorage";

    public string Provider { get; set; } = "local";

    public string RootPath { get; set; } = "storage/photos";

    public string RequestPath { get; set; } = "/uploads";
}
