using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;

namespace TrustMap.Api.Services.PushNotifications;

public sealed class PushNotificationOptions
{
    public const string SectionName = "PushNotifications";

    public string Provider { get; set; } = PushNotificationProviders.Noop;

    public ApnsPushNotificationOptions Apns { get; set; } = new();

    public bool UseApnsProvider =>
        string.Equals(Provider?.Trim(), PushNotificationProviders.Apns, StringComparison.OrdinalIgnoreCase)
        || Apns.Enabled;
}

public static class PushNotificationProviders
{
    public const string Noop = "noop";
    public const string Apns = "apns";
}

public sealed class ApnsPushNotificationOptions
{
    public bool Enabled { get; set; }

    public string TeamId { get; set; } = string.Empty;

    public string KeyId { get; set; } = string.Empty;

    public string BundleId { get; set; } = string.Empty;

    public string PrivateKeyBase64 { get; set; } = string.Empty;

    public string PrivateKeyPem { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public int RequestTimeoutSeconds { get; set; } = 10;
}

public sealed class PushNotificationOptionsValidator(string environmentName) : IValidateOptions<PushNotificationOptions>
{
    public ValidateOptionsResult Validate(string? name, PushNotificationOptions options)
    {
        if (name is not null && name != Options.DefaultName)
        {
            return ValidateOptionsResult.Skip;
        }

        var failures = new List<string>();
        var provider = NormalizeProvider(options.Provider);
        if (provider is not PushNotificationProviders.Noop and not PushNotificationProviders.Apns)
        {
            failures.Add("PushNotifications:Provider must be either 'noop' or 'apns'.");
        }

        if (options.Apns.RequestTimeoutSeconds <= 0)
        {
            failures.Add("PushNotifications:Apns:RequestTimeoutSeconds must be positive.");
        }

        if (options.UseApnsProvider)
        {
            AddRequiredApnsFailures(options.Apns, failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static string NormalizeProvider(string? provider)
    {
        var normalized = provider?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? PushNotificationProviders.Noop
            : normalized.ToLowerInvariant();
    }

    private void AddRequiredApnsFailures(ApnsPushNotificationOptions apns, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(apns.TeamId))
        {
            failures.Add(BuildMissingApnsSettingMessage("TeamId"));
        }

        if (string.IsNullOrWhiteSpace(apns.KeyId))
        {
            failures.Add(BuildMissingApnsSettingMessage("KeyId"));
        }

        if (string.IsNullOrWhiteSpace(apns.BundleId))
        {
            failures.Add(BuildMissingApnsSettingMessage("BundleId"));
        }

        if (string.IsNullOrWhiteSpace(apns.PrivateKeyBase64)
            && string.IsNullOrWhiteSpace(apns.PrivateKeyPem)
            && string.IsNullOrWhiteSpace(apns.PrivateKeyPath))
        {
            failures.Add(
                $"{BuildApnsEnabledPrefix()}, but an APNs private key is missing. "
                + "Set PushNotifications:Apns:PrivateKeyBase64, PushNotifications:Apns:PrivateKeyPem, or PushNotifications:Apns:PrivateKeyPath.");
        }
    }

    private string BuildMissingApnsSettingMessage(string settingName)
    {
        return $"{BuildApnsEnabledPrefix()}, but PushNotifications:Apns:{settingName} is missing.";
    }

    private string BuildApnsEnabledPrefix()
    {
        return string.Equals(environmentName, Environments.Production, StringComparison.OrdinalIgnoreCase)
            ? "Production APNs is enabled"
            : "APNs is enabled";
    }
}
