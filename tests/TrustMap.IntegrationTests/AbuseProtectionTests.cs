using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Friends;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Features.Users;
using TrustMap.Api.Quotas;
using TrustMap.Api.RateLimiting;
using TrustMap.Domain.Enums;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class AbuseProtectionTests
{
    [Fact]
    public async Task AuthRateLimit_IsPerIp_AndReturnsSafe429()
    {
        await using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: WithRateLimits(
                ("RateLimits:Auth:PermitLimit", "1"),
                ("RateLimits:Auth:WindowSeconds", "60")));
        await factory.InitializeAsync();

        var firstIpClient = factory.CreateClient(IPAddress.Parse("10.10.0.1"));
        var secondIpClient = factory.CreateClient(IPAddress.Parse("10.10.0.2"));
        var secretToken = "apple:auth-secret-token";

        var first = await firstIpClient.PostAsJsonAsync("/auth/apple", new AppleAuthRequest
        {
            IdentityToken = secretToken,
            DisplayName = "Auth One",
            PreferredHandle = "auth-one"
        });
        var limited = await firstIpClient.PostAsJsonAsync("/auth/apple", new AppleAuthRequest
        {
            IdentityToken = "apple:auth-second",
            DisplayName = "Auth Two",
            PreferredHandle = "auth-two"
        });
        var otherIp = await secondIpClient.PostAsJsonAsync("/auth/apple", new AppleAuthRequest
        {
            IdentityToken = "apple:auth-other-ip",
            DisplayName = "Auth Other",
            PreferredHandle = "auth-other"
        });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherIp.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));

        var body = await limited.Content.ReadAsStringAsync();
        Assert.Contains("rate_limit_exceeded", body, StringComparison.Ordinal);
        Assert.DoesNotContain(secretToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain("10.10.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("user:", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ip:", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadSearchAndFriendRequestRateLimits_ArePerAuthenticatedUser()
    {
        await using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: WithRateLimits(
                ("RateLimits:Upload:PermitLimit", "1"),
                ("RateLimits:Upload:WindowSeconds", "3600"),
                ("RateLimits:Search:PermitLimit", "1"),
                ("RateLimits:Search:WindowSeconds", "60"),
                ("RateLimits:FriendRequests:PermitLimit", "1"),
                ("RateLimits:FriendRequests:WindowSeconds", "3600")));
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "rl-alice", "Rate Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "rl-bob", "Rate Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "rl-charlie", "Rate Charlie");
        var placeId = await alice.Client.CreatePlaceAsync("rate-limit-upload");
        var reviewId = await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 5, "Upload");

        var firstUpload = await UploadPlaceReviewPhotoAsync(alice.Client, reviewId, TestImages.CreateJpegBytes());
        var secondUpload = await UploadPlaceReviewPhotoAsync(alice.Client, reviewId, TestImages.CreateJpegBytes());
        Assert.Equal(HttpStatusCode.Created, firstUpload.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondUpload.StatusCode);

        var firstSearch = await alice.Client.HttpClient.GetAsync("/users/search?q=Rate&take=10");
        var secondSearch = await alice.Client.HttpClient.GetAsync("/users/search?q=Rate&take=10");
        Assert.Equal(HttpStatusCode.OK, firstSearch.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondSearch.StatusCode);

        var firstRequest = await alice.Client.HttpClient.PostAsJsonAsync(
            "/friend-requests",
            new CreateFriendRequestRequest { ReceiverUserId = bob.Session.User.Id });
        var secondRequest = await alice.Client.HttpClient.PostAsJsonAsync(
            "/friend-requests",
            new CreateFriendRequestRequest { ReceiverUserId = charlie.Session.User.Id });
        Assert.Equal(HttpStatusCode.Created, firstRequest.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondRequest.StatusCode);
    }

    [Fact]
    public async Task UploadQuotas_LimitDailyPhotoCount_PerUser_AndResetByPeriod()
    {
        var timeProvider = new MutableTimeProvider(new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero));
        await using var factory = new TrustMapWebApplicationFactory(
            services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(timeProvider);
            },
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Quotas:Uploads:MaxPhotosPerUserPerDay"] = "1"
            });
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "quota-upload-alice", "Quota Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "quota-upload-bob", "Quota Bob");
        var aliceReviewId = await CreatePlaceReviewAsync(alice.Client, "alice-quota-upload");
        var bobReviewId = await CreatePlaceReviewAsync(bob.Client, "bob-quota-upload");

        var first = await UploadPlaceReviewPhotoAsync(alice.Client, aliceReviewId, TestImages.CreateJpegBytes());
        var second = await UploadPlaceReviewPhotoAsync(alice.Client, aliceReviewId, TestImages.CreateJpegBytes());
        var bobFirst = await UploadPlaceReviewPhotoAsync(bob.Client, bobReviewId, TestImages.CreateJpegBytes());
        timeProvider.Advance(TimeSpan.FromDays(1));
        var nextDay = await UploadPlaceReviewPhotoAsync(alice.Client, aliceReviewId, TestImages.CreateJpegBytes());

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, bobFirst.StatusCode);
        Assert.Equal(HttpStatusCode.Created, nextDay.StatusCode);
    }

    [Fact]
    public async Task UploadQuotas_LimitDailyBytes_AndInvalidUploadDoesNotConsumeCount()
    {
        var validImage = TestImages.CreateJpegBytes();
        await using var byteFactory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Quotas:Uploads:MaxBytesPerUserPerDay"] = (validImage.Length - 1).ToString()
            });
        await byteFactory.InitializeAsync();

        var byteClient = await CreateAuthenticatedClientAsync(byteFactory, "quota-byte", "Quota Byte");
        var byteReviewId = await CreatePlaceReviewAsync(byteClient.Client, "byte-quota");
        var byteLimited = await UploadPlaceReviewPhotoAsync(byteClient.Client, byteReviewId, validImage);
        Assert.Equal(HttpStatusCode.TooManyRequests, byteLimited.StatusCode);

        await using var countFactory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Quotas:Uploads:MaxPhotosPerUserPerDay"] = "1"
            });
        await countFactory.InitializeAsync();

        var countClient = await CreateAuthenticatedClientAsync(countFactory, "quota-invalid", "Quota Invalid");
        var countReviewId = await CreatePlaceReviewAsync(countClient.Client, "invalid-does-not-consume");
        var invalid = await UploadPlaceReviewPhotoAsync(
            countClient.Client,
            countReviewId,
            [0x01, 0x02, 0x03],
            "bad.jpg",
            "image/jpeg");
        var valid = await UploadPlaceReviewPhotoAsync(countClient.Client, countReviewId, validImage);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
    }

    [Fact]
    public async Task AvatarChanges_AreLimitedByDailyQuota()
    {
        await using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Quotas:Uploads:MaxAvatarChangesPerUserPerDay"] = "1"
            });
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "quota-avatar", "Quota Avatar");
        var first = await ReplaceAvatarAsync(alice.Client, TestImages.CreateJpegBytes(width: 320, height: 320));
        var second = await ReplaceAvatarAsync(alice.Client, TestImages.CreateJpegBytes(width: 320, height: 320));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task SearchMinimumLengthAndDailyQuota_AreEnforced()
    {
        await using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Quotas:Search:MaxSearchesPerUserPerDay"] = "1",
                ["Quotas:Search:MinimumQueryLength"] = "2"
            });
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "quota-search-alice", "Quota Search Alice");
        _ = await CreateAuthenticatedClientAsync(factory, "quota-search-target", "Quota Search Target");

        var shortQuery = await alice.Client.HttpClient.GetFromJsonAsync<IReadOnlyList<UserSearchResultDto>>(
            "/users/search?q=q&take=10",
            JsonDefaults.Options);
        var first = await alice.Client.HttpClient.GetAsync("/users/search?q=Quota&take=10");
        var second = await alice.Client.HttpClient.GetAsync("/users/search?q=Search&take=10");

        Assert.NotNull(shortQuery);
        Assert.Empty(shortQuery!);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task FriendRequestQuotas_LimitOutgoingDailyPendingAndRemainPerUser()
    {
        await using var dailyFactory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Quotas:FriendRequests:MaxOutgoingPerUserPerDay"] = "1"
            });
        await dailyFactory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(dailyFactory, "quota-friend-alice", "Quota Alice");
        var bob = await CreateAuthenticatedClientAsync(dailyFactory, "quota-friend-bob", "Quota Bob");
        var charlie = await CreateAuthenticatedClientAsync(dailyFactory, "quota-friend-charlie", "Quota Charlie");
        var dana = await CreateAuthenticatedClientAsync(dailyFactory, "quota-friend-dana", "Quota Dana");

        var first = await SendFriendRequestAsync(alice.Client, bob.Session.User.Id);
        var second = await SendFriendRequestAsync(alice.Client, charlie.Session.User.Id);
        var otherUser = await SendFriendRequestAsync(dana.Client, bob.Session.User.Id);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(HttpStatusCode.Created, otherUser.StatusCode);

        await using var pendingFactory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Quotas:FriendRequests:MaxPendingOutgoingPerUser"] = "1"
            });
        await pendingFactory.InitializeAsync();

        var pendingAlice = await CreateAuthenticatedClientAsync(pendingFactory, "pending-alice", "Pending Alice");
        var pendingBob = await CreateAuthenticatedClientAsync(pendingFactory, "pending-bob", "Pending Bob");
        var pendingCharlie = await CreateAuthenticatedClientAsync(pendingFactory, "pending-charlie", "Pending Charlie");
        var pendingFirst = await SendFriendRequestAsync(pendingAlice.Client, pendingBob.Session.User.Id);
        var pendingSecond = await SendFriendRequestAsync(pendingAlice.Client, pendingCharlie.Session.User.Id);

        Assert.Equal(HttpStatusCode.Created, pendingFirst.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, pendingSecond.StatusCode);
    }

    [Fact]
    public async Task DuplicateFriendRequest_DoesNotConsumeDailyQuotaTwice()
    {
        await using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Quotas:FriendRequests:MaxOutgoingPerUserPerDay"] = "2"
            });
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "duplicate-quota-alice", "Duplicate Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "duplicate-quota-bob", "Duplicate Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "duplicate-quota-charlie", "Duplicate Charlie");

        var first = await SendFriendRequestAsync(alice.Client, bob.Session.User.Id);
        var duplicate = await SendFriendRequestAsync(alice.Client, bob.Session.User.Id);
        var secondReal = await SendFriendRequestAsync(alice.Client, charlie.Session.User.Id);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondReal.StatusCode);
    }

    [Fact]
    public async Task InvalidRateLimitAndQuotaConfiguration_FailStartup()
    {
        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var factory = new TrustMapWebApplicationFactory(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["RateLimits:Auth:PermitLimit"] = "0"
                });
            await factory.InitializeAsync();
        });

        await Assert.ThrowsAsync<OptionsValidationException>(async () =>
        {
            await using var factory = new TrustMapWebApplicationFactory(
                configurationOverrides: new Dictionary<string, string?>
                {
                    ["Quotas:Uploads:MaxPhotosPerUserPerDay"] = "0"
                });
            await factory.InitializeAsync();
        });
    }

    [Fact]
    public async Task ExplicitlyDisablingRateLimits_ByConfiguration_AllowsRequestsBeyondConfiguredLimit()
    {
        await using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["RateLimits:Enabled"] = "false",
                ["RateLimits:Auth:PermitLimit"] = "1",
                ["RateLimits:Auth:WindowSeconds"] = "60"
            });
        await factory.InitializeAsync();

        var client = factory.CreateClient(IPAddress.Parse("10.10.0.10"));
        var first = await client.PostAsJsonAsync("/auth/apple", new AppleAuthRequest
        {
            IdentityToken = "apple:disabled-one",
            DisplayName = "Disabled One",
            PreferredHandle = "disabled-one"
        });
        var second = await client.PostAsJsonAsync("/auth/apple", new AppleAuthRequest
        {
            IdentityToken = "apple:disabled-two",
            DisplayName = "Disabled Two",
            PreferredHandle = "disabled-two"
        });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public void ProductionConfiguration_DoesNotDisableRateLimitsOrQuotas()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(FindServerRoot(), "src", "TrustMap.Api"))
            .AddJsonFile("appsettings.json")
            .Build();

        var rateLimits = configuration.GetSection(TrustMapRateLimitOptions.SectionName).Get<TrustMapRateLimitOptions>();
        var quotas = configuration.GetSection(QuotaOptions.SectionName).Get<QuotaOptions>();

        Assert.NotNull(rateLimits);
        Assert.NotNull(quotas);
        Assert.True(rateLimits!.Enabled);
        Assert.True(quotas!.Enabled);
    }

    private static string FindServerRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var appsettingsPath = Path.Combine(
                directory.FullName,
                "src",
                "TrustMap.Api",
                "appsettings.json");
            if (File.Exists(appsettingsPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the TrustMap-server root.");
    }

    private static IReadOnlyDictionary<string, string?> WithRateLimits(params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["RateLimits:Enabled"] = "true",
            ["RateLimits:Global:PermitLimit"] = "1000",
            ["RateLimits:Global:WindowSeconds"] = "60",
            ["RateLimits:Auth:PermitLimit"] = "100",
            ["RateLimits:RefreshToken:PermitLimit"] = "100",
            ["RateLimits:Upload:PermitLimit"] = "100",
            ["RateLimits:Search:PermitLimit"] = "100",
            ["RateLimits:FriendRequests:PermitLimit"] = "100"
        };

        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return values;
    }

    private static async Task<(TestApiClient Client, AuthSessionDto Session)> CreateAuthenticatedClientAsync(
        TrustMapWebApplicationFactory factory,
        string subject,
        string displayName)
    {
        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync(subject, displayName);
        client.Authenticate(session.AccessToken);
        return (client, session);
    }

    private static async Task<Guid> CreatePlaceReviewAsync(TestApiClient client, string suffix)
    {
        var placeId = await client.CreatePlaceAsync(suffix);
        return await client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 5, $"Review {suffix}");
    }

    private static async Task<HttpResponseMessage> UploadPlaceReviewPhotoAsync(
        TestApiClient client,
        Guid reviewId,
        byte[] bytes,
        string fileName = "photo.jpg",
        string contentType = "image/jpeg")
    {
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, "file", fileName);
        multipart.Add(new StringContent(reviewId.ToString()), "placeReviewId");
        return await client.HttpClient.PostAsync("/photos/upload", multipart);
    }

    private static async Task<HttpResponseMessage> ReplaceAvatarAsync(TestApiClient client, byte[] bytes)
    {
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipart.Add(fileContent, "avatar", "avatar.jpg");
        return await client.HttpClient.PutAsync("/me/avatar", multipart);
    }

    private static Task<HttpResponseMessage> SendFriendRequestAsync(TestApiClient client, Guid receiverUserId) =>
        client.HttpClient.PostAsJsonAsync(
            "/friend-requests",
            new CreateFriendRequestRequest
            {
                ReceiverUserId = receiverUserId
            });

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
