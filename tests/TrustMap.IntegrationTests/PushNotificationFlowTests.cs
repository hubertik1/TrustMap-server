using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TrustMap.Api.Features.Notifications;
using TrustMap.Api.Services.PushNotifications;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class PushNotificationFlowTests
{
    [Fact]
    public async Task PlaceReviewByDirectFriend_SendsPushToActiveDeviceToken()
    {
        var sender = new RecordingPushNotificationSender();
        await using var factory = CreateFactoryWithSender(sender);
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());
        var charlieClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("push-place-alice", "Alice Push Place");
        var bob = await bobClient.SignInAsync("push-place-bob", "Bob Push Place");
        var charlie = await charlieClient.SignInAsync("push-place-charlie", "Charlie Push Place");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        charlieClient.Authenticate(charlie.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);
        await CreateFriendshipAsync(factory, bob.User.Id, charlie.User.Id);
        await RegisterTokenAsync(bobClient, "00abff");
        await RegisterTokenAsync(charlieClient, "00ccff");

        var place = await aliceClient.CreatePlaceSummaryAsync("push-place-review");
        var reviewId = await aliceClient.CreatePlaceReviewAsync(place.Id, VisibilityLevel.Friends, 5, "Great");

        var request = Assert.Single(sender.Requests);
        Assert.Equal("00abff", request.DeviceToken);
        Assert.Equal("development", request.Environment);
        Assert.Equal("TrustMap", request.Title);
        Assert.Equal("@push-place-alice added a place review", request.Body);
        Assert.Equal(NotificationType.PlaceReviewAdded.ToString(), request.Data["type"]);
        Assert.Equal(place.Id.ToString("D"), request.Data["placeId"]);
        Assert.Equal(reviewId.ToString("D"), request.Data["placeReviewId"]);
        Assert.True(request.BadgeCount > 0);
    }

    [Fact]
    public async Task PrivatePlaceReview_DoesNotSendPush()
    {
        var sender = new RecordingPushNotificationSender();
        await using var factory = CreateFactoryWithSender(sender);
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("push-private-alice", "Alice Push Private");
        var bob = await bobClient.SignInAsync("push-private-bob", "Bob Push Private");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);
        await RegisterTokenAsync(bobClient, "00abff");

        var placeId = await aliceClient.CreatePlaceAsync("push-private-place-review");
        await aliceClient.CreatePlaceReviewAsync(placeId, VisibilityLevel.Private, 5, "Private");

        Assert.Empty(sender.Requests);
    }

    [Fact]
    public async Task DishReviewByDirectFriend_SendsPushToActiveDeviceToken()
    {
        var sender = new RecordingPushNotificationSender();
        await using var factory = CreateFactoryWithSender(sender);
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("push-dish-alice", "Alice Push Dish");
        var bob = await bobClient.SignInAsync("push-dish-bob", "Bob Push Dish");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);
        await RegisterTokenAsync(bobClient, "00abff");

        var place = await aliceClient.CreatePlaceSummaryAsync("push-dish-review");
        var parentReviewId = await aliceClient.CreatePlaceReviewAsync(place.Id, VisibilityLevel.Private, 4, "Private parent");
        var dishReviewId = await aliceClient.CreateDishReviewAsync(
            place.Id,
            parentReviewId,
            VisibilityLevel.Friends,
            "Pierogi",
            5);

        var request = Assert.Single(sender.Requests);
        Assert.Equal("00abff", request.DeviceToken);
        Assert.Equal("@push-dish-alice added a dish review", request.Body);
        Assert.Equal(NotificationType.DishReviewAdded.ToString(), request.Data["type"]);
        Assert.Equal(place.Id.ToString("D"), request.Data["placeId"]);
        Assert.Equal(dishReviewId.ToString("D"), request.Data["dishReviewId"]);
    }

    [Fact]
    public async Task FriendRequest_SendsPushToReceiverActiveDeviceToken()
    {
        var sender = new RecordingPushNotificationSender();
        await using var factory = CreateFactoryWithSender(sender);
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("push-request-alice", "Alice Push Request");
        var bob = await bobClient.SignInAsync("push-request-bob", "Bob Push Request");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await RegisterTokenAsync(bobClient, "00abff", environment: "production");

        var friendRequest = await aliceClient.SendFriendRequestAsync(bob.User.Id);

        var request = Assert.Single(sender.Requests);
        Assert.Equal("00abff", request.DeviceToken);
        Assert.Equal("production", request.Environment);
        Assert.Equal("@push-request-alice sent you a friend request", request.Body);
        Assert.Equal(NotificationType.FriendRequestSent.ToString(), request.Data["type"]);
        Assert.Equal(friendRequest.Id.ToString("D"), request.Data["friendRequestId"]);
    }

    [Fact]
    public async Task PushSendingFailure_DoesNotFailFriendRequestCreation()
    {
        await using var factory = CreateFactoryWithSender(new ThrowingPushNotificationSender());
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("push-request-failure-alice", "Alice Push Failure");
        var bob = await bobClient.SignInAsync("push-request-failure-bob", "Bob Push Failure");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await RegisterTokenAsync(bobClient, "00abff");

        var friendRequest = await aliceClient.SendFriendRequestAsync(bob.User.Id);

        var notifications = await FetchNotificationsAsync(bobClient);
        var notification = Assert.Single(notifications.Items);
        Assert.Equal(friendRequest.Id, notification.FriendRequestId);
        Assert.Equal(NotificationType.FriendRequestSent, notification.Type);
    }

    [Fact]
    public async Task InvalidDeviceTokenResult_RevokesTokenAndSkipsFuturePushes()
    {
        var sender = new RecordingPushNotificationSender
        {
            ResultFactory = _ => PushNotificationSendResult.InvalidDeviceToken("BadDeviceToken", 400)
        };
        await using var factory = CreateFactoryWithSender(sender);
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("push-invalid-alice", "Alice Push Invalid");
        var bob = await bobClient.SignInAsync("push-invalid-bob", "Bob Push Invalid");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);
        await RegisterTokenAsync(bobClient, "00abff");

        var firstPlaceId = await aliceClient.CreatePlaceAsync("push-invalid-first");
        await aliceClient.CreatePlaceReviewAsync(firstPlaceId, VisibilityLevel.Friends, 5, "First");

        Assert.Single(sender.Requests);
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var token = await dbContext.DeviceTokens.AsNoTracking().SingleAsync();
            Assert.NotNull(token.RevokedAt);
        }

        sender.ResultFactory = _ => PushNotificationSendResult.Successful();
        var secondPlaceId = await aliceClient.CreatePlaceAsync("push-invalid-second");
        await aliceClient.CreatePlaceReviewAsync(secondPlaceId, VisibilityLevel.Friends, 5, "Second");

        Assert.Single(sender.Requests);
    }

    [Fact]
    public async Task PushFailureLogs_DoNotContainRawDeviceTokenOrPrivateKey()
    {
        const string rawDeviceToken = "deadbeefcafebabe";
        const string privateKey = "very-sensitive-apns-private-key";
        var logProvider = new ListLoggerProvider();
        var sender = new RecordingPushNotificationSender
        {
            ResultFactory = _ => PushNotificationSendResult.Failed("InvalidProviderToken", 403)
        };
        await using var factory = new TrustMapWebApplicationFactory(
            services =>
            {
                services.RemoveAll<IPushNotificationSender>();
                services.AddSingleton(sender);
                services.AddSingleton<IPushNotificationSender>(sp => sp.GetRequiredService<RecordingPushNotificationSender>());
                services.AddLogging(logging => logging.AddProvider(logProvider));
            },
            configurationOverrides: new Dictionary<string, string?>
            {
                ["PushNotifications:Apns:PrivateKeyPem"] = privateKey
            });
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("push-log-alice", "Alice Push Log");
        var bob = await bobClient.SignInAsync("push-log-bob", "Bob Push Log");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);
        await CreateFriendshipAsync(factory, alice.User.Id, bob.User.Id);
        await RegisterTokenAsync(bobClient, rawDeviceToken);

        var placeId = await aliceClient.CreatePlaceAsync("push-log-place");
        await aliceClient.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 5, "Logs");

        var logText = string.Join(Environment.NewLine, logProvider.Entries);
        Assert.DoesNotContain(rawDeviceToken, logText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(privateKey, logText, StringComparison.Ordinal);
    }

    private static TrustMapWebApplicationFactory CreateFactoryWithSender<TSender>(TSender sender)
        where TSender : class, IPushNotificationSender =>
        new(services =>
        {
            services.RemoveAll<IPushNotificationSender>();
            services.AddSingleton(sender);
            services.AddSingleton<IPushNotificationSender>(sp => sp.GetRequiredService<TSender>());
        });

    private static async Task RegisterTokenAsync(
        TestApiClient client,
        string token,
        string environment = "development")
    {
        var response = await client.HttpClient.PostAsJsonAsync(
            "/me/device-tokens",
            new
            {
                token,
                platform = "ios",
                environment
            });
        response.EnsureSuccessStatusCode();
    }

    private static async Task CreateFriendshipAsync(
        TrustMapWebApplicationFactory factory,
        Guid firstUserId,
        Guid secondUserId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        dbContext.Friendships.Add(new Friendship(Guid.CreateVersion7(), firstUserId, secondUserId));
        await dbContext.SaveChangesAsync();
    }

    private static async Task<NotificationsResponseDto> FetchNotificationsAsync(TestApiClient client)
    {
        var response = await client.HttpClient.GetAsync("/notifications");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<NotificationsResponseDto>(JsonDefaults.Options))!;
    }

    private sealed class RecordingPushNotificationSender : IPushNotificationSender
    {
        private readonly object _syncRoot = new();
        private readonly List<PushNotificationRequest> _requests = [];

        public Func<PushNotificationRequest, PushNotificationSendResult> ResultFactory { get; set; } =
            _ => PushNotificationSendResult.Successful();

        public IReadOnlyList<PushNotificationRequest> Requests
        {
            get
            {
                lock (_syncRoot)
                {
                    return _requests.ToArray();
                }
            }
        }

        public Task<PushNotificationSendResult> SendAsync(
            PushNotificationRequest notification,
            CancellationToken cancellationToken)
        {
            lock (_syncRoot)
            {
                _requests.Add(notification);
            }

            return Task.FromResult(ResultFactory(notification));
        }
    }

    private sealed class ThrowingPushNotificationSender : IPushNotificationSender
    {
        public Task<PushNotificationSendResult> SendAsync(
            PushNotificationRequest notification,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated APNs failure.");
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public IReadOnlyCollection<string> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new ListLogger(categoryName, _entries);

        public void Dispose()
        {
        }
    }

    private sealed class ListLogger(string categoryName, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue($"{logLevel}: {categoryName}: {formatter(state, exception)} {exception}");
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
