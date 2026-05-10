namespace TrustMap.Api.Services.PushNotifications;

public sealed class NoOpPushNotificationSender(ILogger<NoOpPushNotificationSender> logger) : IPushNotificationSender
{
    private int _hasLoggedDisabledMessage;

    public Task SendAsync(PushNotificationRequest notification, CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _hasLoggedDisabledMessage, 1) == 0)
        {
            logger.LogInformation("Push notification sending is disabled because APNs credentials are not configured.");
        }

        return Task.CompletedTask;
    }
}
