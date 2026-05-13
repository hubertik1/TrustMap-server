namespace TrustMap.Infrastructure.Storage;

public sealed record StoredFileResult(
    Stream Stream,
    string ContentType,
    long Length);
