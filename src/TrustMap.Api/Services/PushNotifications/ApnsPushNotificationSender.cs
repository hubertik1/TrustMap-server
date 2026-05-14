using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace TrustMap.Api.Services.PushNotifications;

public sealed class ApnsPushNotificationSender(
    IHttpClientFactory httpClientFactory,
    IOptions<PushNotificationOptions> optionsAccessor,
    IHostEnvironment hostEnvironment,
    TimeProvider timeProvider) : IPushNotificationSender
{
    public const string HttpClientName = "apns";

    private static readonly TimeSpan ProviderTokenRefreshInterval = TimeSpan.FromMinutes(50);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _providerTokenLock = new();
    private string? _cachedProviderToken;
    private DateTimeOffset _cachedProviderTokenIssuedAt;

    public async Task<PushNotificationSendResult> SendAsync(
        PushNotificationRequest notification,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildEndpoint(notification);
        if (endpoint is null)
        {
            return PushNotificationSendResult.Failed("UnsupportedApnsEnvironment");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent(
                JsonSerializer.Serialize(BuildPayload(notification), JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", GetProviderToken());
        request.Headers.TryAddWithoutValidation("apns-topic", optionsAccessor.Value.Apns.BundleId);
        request.Headers.TryAddWithoutValidation("apns-push-type", "alert");
        request.Headers.TryAddWithoutValidation("apns-priority", "10");

        HttpResponseMessage response;
        try
        {
            response = await httpClientFactory
                .CreateClient(HttpClientName)
                .SendAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PushNotificationSendResult.Failed("Timeout");
        }
        catch (HttpRequestException)
        {
            return PushNotificationSendResult.Failed("NetworkError");
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return PushNotificationSendResult.Successful();
            }

            var statusCode = (int)response.StatusCode;
            var reason = await ReadApnsReasonAsync(response, cancellationToken);
            return ShouldInvalidateDeviceToken(response.StatusCode, reason)
                ? PushNotificationSendResult.InvalidDeviceToken(reason, statusCode)
                : PushNotificationSendResult.Failed(reason, statusCode);
        }
    }

    private Uri? BuildEndpoint(PushNotificationRequest notification)
    {
        var host = notification.Environment.Trim().ToLowerInvariant() switch
        {
            "production" => "api.push.apple.com",
            "development" => "api.sandbox.push.apple.com",
            _ => null
        };

        return host is null
            ? null
            : new Uri($"https://{host}/3/device/{Uri.EscapeDataString(notification.DeviceToken)}");
    }

    private static Dictionary<string, object?> BuildPayload(PushNotificationRequest notification)
    {
        var aps = new Dictionary<string, object?>
        {
            ["alert"] = new Dictionary<string, string>
            {
                ["title"] = notification.Title,
                ["body"] = notification.Body
            },
            ["sound"] = "default"
        };

        if (notification.BadgeCount.HasValue)
        {
            aps["badge"] = notification.BadgeCount.Value;
        }

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["aps"] = aps
        };

        foreach (var (key, value) in notification.Data)
        {
            if (!string.Equals(key, "aps", StringComparison.Ordinal))
            {
                payload[key] = value;
            }
        }

        return payload;
    }

    private string GetProviderToken()
    {
        var now = timeProvider.GetUtcNow();
        lock (_providerTokenLock)
        {
            if (_cachedProviderToken is not null
                && now - _cachedProviderTokenIssuedAt < ProviderTokenRefreshInterval)
            {
                return _cachedProviderToken;
            }

            _cachedProviderToken = CreateProviderToken(now);
            _cachedProviderTokenIssuedAt = now;
            return _cachedProviderToken;
        }
    }

    private string CreateProviderToken(DateTimeOffset issuedAt)
    {
        var apnsOptions = optionsAccessor.Value.Apns;
        var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["alg"] = "ES256",
            ["kid"] = apnsOptions.KeyId
        });
        var claims = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["iss"] = apnsOptions.TeamId,
            ["iat"] = issuedAt.ToUnixTimeSeconds()
        });

        var signingInput = $"{Base64UrlEncode(header)}.{Base64UrlEncode(claims)}";
        using var ecdsa = CreateSigningKey(apnsOptions);
        var signature = ecdsa.SignData(
            Encoding.ASCII.GetBytes(signingInput),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return $"{signingInput}.{Base64UrlEncode(signature)}";
    }

    private ECDsa CreateSigningKey(ApnsPushNotificationOptions apnsOptions)
    {
        var privateKey = ResolvePrivateKey(apnsOptions);
        var ecdsa = ECDsa.Create();
        try
        {
            if (privateKey.Contains("BEGIN", StringComparison.Ordinal))
            {
                ecdsa.ImportFromPem(privateKey);
            }
            else
            {
                var keyBytes = Convert.FromBase64String(privateKey);
                ecdsa.ImportPkcs8PrivateKey(keyBytes, out _);
            }

            return ecdsa;
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException or FormatException)
        {
            ecdsa.Dispose();
            throw new InvalidOperationException("APNs private key could not be imported.", exception);
        }
    }

    private string ResolvePrivateKey(ApnsPushNotificationOptions apnsOptions)
    {
        if (!string.IsNullOrWhiteSpace(apnsOptions.PrivateKeyBase64))
        {
            try
            {
                var keyBytes = Convert.FromBase64String(apnsOptions.PrivateKeyBase64.Trim());
                return Encoding.UTF8.GetString(keyBytes);
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException("APNs private key could not be decoded.", exception);
            }
        }

        if (!string.IsNullOrWhiteSpace(apnsOptions.PrivateKeyPem))
        {
            return apnsOptions.PrivateKeyPem;
        }

        if (!string.IsNullOrWhiteSpace(apnsOptions.PrivateKeyPath))
        {
            var path = Path.IsPathRooted(apnsOptions.PrivateKeyPath)
                ? apnsOptions.PrivateKeyPath
                : Path.GetFullPath(apnsOptions.PrivateKeyPath, hostEnvironment.ContentRootPath);

            return File.ReadAllText(path);
        }

        throw new InvalidOperationException("APNs private key is not configured.");
    }

    private static async Task<string?> ReadApnsReasonAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.TryGetProperty("reason", out var reasonProperty)
                && reasonProperty.ValueKind == JsonValueKind.String
                ? reasonProperty.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool ShouldInvalidateDeviceToken(HttpStatusCode statusCode, string? reason) =>
        statusCode switch
        {
            HttpStatusCode.BadRequest => string.Equals(reason, "BadDeviceToken", StringComparison.Ordinal),
            HttpStatusCode.Gone => string.Equals(reason, "Unregistered", StringComparison.Ordinal),
            _ => false
        };

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
