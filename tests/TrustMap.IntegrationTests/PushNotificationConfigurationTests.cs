using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TrustMap.Api.Services.PushNotifications;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class PushNotificationConfigurationTests
{
    [Fact]
    public async Task NoOpPushNotificationSender_ReturnsSuccess()
    {
        var sender = new NoOpPushNotificationSender(NullLogger<NoOpPushNotificationSender>.Instance);

        var result = await sender.SendAsync(
            new PushNotificationRequest(
                "00abff",
                "development",
                "TrustMap",
                "Test notification",
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(result.ShouldInvalidateDeviceToken);
    }

    [Fact]
    public async Task DefaultConfiguration_RegistersNoOpSender()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var sender = factory.Services.GetRequiredService<IPushNotificationSender>();

        Assert.IsType<NoOpPushNotificationSender>(sender);
    }

    [Fact]
    public void ProductionApnsConfigurationMissingRequiredValues_FailsValidation()
    {
        var validator = new PushNotificationOptionsValidator("Production");

        var result = validator.Validate(
            name: null,
            new PushNotificationOptions
            {
                Provider = PushNotificationProviders.Apns,
                Apns = new ApnsPushNotificationOptions
                {
                    Enabled = true,
                    RequestTimeoutSeconds = 10
                }
            });

        Assert.False(result.Succeeded);
        var failures = JoinFailures(result);
        Assert.Contains("TeamId", failures);
        Assert.Contains("KeyId", failures);
        Assert.Contains("BundleId", failures);
        Assert.Contains("private key", failures);
    }

    [Fact]
    public void ProductionStartupWithApnsEnabledAndMissingRequiredValues_Fails()
    {
        using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "Z9rNd7VGn2QwP4sY8kLmC6xA1bAeF5hJ",
                ["PushNotifications:Provider"] = "apns",
                ["PushNotifications:Apns:Enabled"] = "true"
            },
            environmentName: "Production");

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        var message = exception.ToString();

        Assert.Contains("Production APNs is enabled", message);
        Assert.Contains("TeamId", message);
        Assert.Contains("KeyId", message);
        Assert.Contains("BundleId", message);
        Assert.Contains("private key", message);
    }

    [Fact]
    public void ApnsConfigurationValidation_DoesNotIncludePrivateKeyValue()
    {
        const string privateKey = "very-sensitive-private-key-value";
        var validator = new PushNotificationOptionsValidator("Production");

        var result = validator.Validate(
            name: null,
            new PushNotificationOptions
            {
                Provider = PushNotificationProviders.Apns,
                Apns = new ApnsPushNotificationOptions
                {
                    PrivateKeyPem = privateKey,
                    RequestTimeoutSeconds = 0
                }
            });

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(privateKey, JoinFailures(result), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApnsConfiguration_RegistersApnsSender()
    {
        await using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["PushNotifications:Provider"] = "apns",
                ["PushNotifications:Apns:Enabled"] = "true",
                ["PushNotifications:Apns:TeamId"] = "TEAMID1234",
                ["PushNotifications:Apns:KeyId"] = "KEYID12345",
                ["PushNotifications:Apns:BundleId"] = "com.hubertik.TrustMap",
                ["PushNotifications:Apns:PrivateKeyBase64"] = CreatePrivateKeyBase64(),
                ["PushNotifications:Apns:RequestTimeoutSeconds"] = "10"
            });
        await factory.InitializeAsync();

        var sender = factory.Services.GetRequiredService<IPushNotificationSender>();

        Assert.IsType<ApnsPushNotificationSender>(sender);
    }

    private static string CreatePrivateKeyBase64()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(ecdsa.ExportPkcs8PrivateKeyPem()));
    }

    private static string JoinFailures(Microsoft.Extensions.Options.ValidateOptionsResult result) =>
        string.Join(Environment.NewLine, result.Failures ?? []);
}
