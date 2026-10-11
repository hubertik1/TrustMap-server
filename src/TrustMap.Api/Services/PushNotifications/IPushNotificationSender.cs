namespace TrustMap.Api.Services.PushNotifications;

public interface IPushNotificationSender
{
    Task<PushNotificationSendResult> SendAsync(PushNotificationRequest notification, CancellationToken cancellationToken);
}

public sealed record PushNotificationRequest(
    string DeviceToken,
    string Environment,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data,
    int? BadgeCount = null,
    string? BodyLocalizationKey = null,
    IReadOnlyList<string>? BodyLocalizationArguments = null);

public sealed record PushNotificationSendResult(
    bool Success,
    bool ShouldInvalidateDeviceToken,
    string? ProviderReason = null,
    int? StatusCode = null)
{
    public static PushNotificationSendResult Successful() => new(true, false);

    public static PushNotificationSendResult InvalidDeviceToken(string? reason, int? statusCode) =>
        new(false, true, reason, statusCode);

    public static PushNotificationSendResult Failed(string? reason = null, int? statusCode = null) =>
        new(false, false, reason, statusCode);
}
