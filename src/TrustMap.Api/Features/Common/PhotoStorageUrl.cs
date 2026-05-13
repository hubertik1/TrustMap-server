namespace TrustMap.Api.Features.Common;

public static class PhotoStorageUrl
{
    public static bool TryResolveStorageKey(
        string? storedUrl,
        string requestPath,
        out string storageKey)
    {
        storageKey = string.Empty;

        if (string.IsNullOrWhiteSpace(storedUrl))
        {
            return false;
        }

        requestPath = NormalizeRequestPath(requestPath);
        var path = storedUrl.Trim();

        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
        {
            path = absoluteUri.AbsolutePath;
        }

        if (!string.Equals(path, requestPath, StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith($"{requestPath}/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        storageKey = path[requestPath.Length..].TrimStart('/');
        return !string.IsNullOrWhiteSpace(storageKey);
    }

    private static string NormalizeRequestPath(string requestPath)
    {
        requestPath = requestPath.Trim().TrimEnd('/');
        return string.IsNullOrWhiteSpace(requestPath) ? "/uploads" : requestPath;
    }
}
