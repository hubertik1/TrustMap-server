namespace TrustMap.Api.Services.PushNotifications;

public interface IPushNotificationSender
{
    Task SendAsync(PushNotificationRequest notification, CancellationToken cancellationToken);
}

public sealed record PushNotificationRequest(
    string DeviceToken,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data,
    int? BadgeCount = null);
