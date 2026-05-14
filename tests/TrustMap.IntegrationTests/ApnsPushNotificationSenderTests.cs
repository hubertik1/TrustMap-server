using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TrustMap.Api.Services.PushNotifications;

namespace TrustMap.IntegrationTests;

public sealed class ApnsPushNotificationSenderTests
{
    [Fact]
    public async Task SendAsync_UsesProductionEndpointForProductionToken()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK));
        var sender = CreateSender(handler);

        var result = await sender.SendAsync(CreateRequest(environment: "production"), CancellationToken.None);

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.push.apple.com/3/device/00abff", request.RequestUri.ToString());
        Assert.Equal(HttpVersion.Version20, request.Version);
    }

    [Fact]
    public async Task SendAsync_UsesSandboxEndpointForDevelopmentToken()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK));
        var sender = CreateSender(handler);

        var result = await sender.SendAsync(CreateRequest(environment: "development"), CancellationToken.None);

        Assert.True(result.Success);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://api.sandbox.push.apple.com/3/device/00abff", request.RequestUri.ToString());
        Assert.Equal(HttpVersion.Version20, request.Version);
    }

    [Fact]
    public async Task SendAsync_IncludesRequiredHeadersAndAlertPayload()
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse(HttpStatusCode.OK));
        var sender = CreateSender(handler);

        await sender.SendAsync(CreateRequest(badgeCount: 7), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("bearer", request.AuthorizationScheme);
        Assert.False(string.IsNullOrWhiteSpace(request.AuthorizationParameter));
        Assert.Equal("com.hubertik.TrustMap", request.Headers["apns-topic"].Single());
        Assert.Equal("alert", request.Headers["apns-push-type"].Single());
        Assert.Equal("10", request.Headers["apns-priority"].Single());

        using var payload = JsonDocument.Parse(request.Body);
        var root = payload.RootElement;
        var aps = root.GetProperty("aps");
        var alert = aps.GetProperty("alert");
        Assert.Equal("TrustMap", alert.GetProperty("title").GetString());
        Assert.Equal("@alice added a place review", alert.GetProperty("body").GetString());
        Assert.Equal("default", aps.GetProperty("sound").GetString());
        Assert.Equal(7, aps.GetProperty("badge").GetInt32());
        Assert.Equal("notification-1", root.GetProperty("notificationId").GetString());
        Assert.Equal("PlaceReviewAdded", root.GetProperty("type").GetString());
        Assert.Equal("place-1", root.GetProperty("placeId").GetString());
    }

    [Theory]
    [InlineData(200, null, true, false, null)]
    [InlineData(400, "BadDeviceToken", false, true, 400)]
    [InlineData(410, "Unregistered", false, true, 410)]
    [InlineData(403, "InvalidProviderToken", false, false, 403)]
    [InlineData(429, "TooManyRequests", false, false, 429)]
    [InlineData(500, "InternalServerError", false, false, 500)]
    public async Task SendAsync_MapsApnsResponses(
        int statusCode,
        string? reason,
        bool expectedSuccess,
        bool expectedInvalidToken,
        int? expectedStatusCode)
    {
        var handler = new RecordingHttpMessageHandler(_ => JsonResponse((HttpStatusCode)statusCode, reason));
        var sender = CreateSender(handler);

        var result = await sender.SendAsync(CreateRequest(), CancellationToken.None);

        Assert.Equal(expectedSuccess, result.Success);
        Assert.Equal(expectedInvalidToken, result.ShouldInvalidateDeviceToken);
        Assert.Equal(reason, result.ProviderReason);
        Assert.Equal(expectedStatusCode, result.StatusCode);
    }

    private static ApnsPushNotificationSender CreateSender(RecordingHttpMessageHandler handler) =>
        new(
            new RecordingHttpClientFactory(handler),
            Options.Create(new PushNotificationOptions
            {
                Provider = PushNotificationProviders.Apns,
                Apns = new ApnsPushNotificationOptions
                {
                    Enabled = true,
                    TeamId = "TEAMID1234",
                    KeyId = "KEYID12345",
                    BundleId = "com.hubertik.TrustMap",
                    PrivateKeyBase64 = CreatePrivateKeyBase64(),
                    RequestTimeoutSeconds = 10
                }
            }),
            new FakeHostEnvironment(),
            TimeProvider.System);

    private static PushNotificationRequest CreateRequest(string environment = "production", int? badgeCount = null) =>
        new(
            "00abff",
            environment,
            "TrustMap",
            "@alice added a place review",
            new Dictionary<string, string>
            {
                ["notificationId"] = "notification-1",
                ["type"] = "PlaceReviewAdded",
                ["placeId"] = "place-1"
            },
            badgeCount);

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string? reason = null)
    {
        var body = reason is null ? "{}" : $$"""{ "reason": "{{reason}}" }""";
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static string CreatePrivateKeyBase64()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(ecdsa.ExportPkcs8PrivateKeyPem()));
    }

    private sealed class RecordingHttpClientFactory(RecordingHttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RecordingHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var header in request.Headers)
            {
                headers[header.Key] = header.Value.ToArray();
            }

            Requests.Add(new RecordedRequest(
                request.RequestUri!,
                request.Version,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                headers,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            return responseFactory(request);
        }
    }

    private sealed record RecordedRequest(
        Uri RequestUri,
        Version Version,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        IReadOnlyDictionary<string, string[]> Headers,
        string Body);

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "TrustMap.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
