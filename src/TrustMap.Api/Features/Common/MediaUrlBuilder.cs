namespace TrustMap.Api.Features.Common;

public static class MediaUrlBuilder
{
    public static string PhotoUrl(Guid photoId) =>
        $"/media/photos/{photoId:D}";

    public static string? PhotoVariantUrl(Guid photoId, string? storageKey, string variantName) =>
        string.IsNullOrWhiteSpace(storageKey)
            ? null
            : $"/media/photos/{photoId:D}/variants/{variantName}";

    public static string? AvatarUrl(Guid userId, string? storedAvatarUrl) =>
        string.IsNullOrWhiteSpace(storedAvatarUrl)
            ? null
            : $"/media/users/{userId:D}/avatar";
}
