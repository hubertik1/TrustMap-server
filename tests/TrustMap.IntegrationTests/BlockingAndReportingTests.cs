using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Services.Moderation;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class BlockingAndReportingTests
{
    private const string AdminPassword = "a-separate-test-administrator-password";

    [Fact]
    public async Task FileReports_WritePrivateEmailWithoutSmtp_AndExpireWithStoredEvidence()
    {
        await using var factory = ReportingFactory();
        await factory.InitializeAsync();
        var options = factory.Services.GetRequiredService<IOptions<ModerationOptions>>().Value;
        options.EmailDirectory = Path.Combine(factory.PhotoRootPath, "moderation-mails");
        Assert.Equal("file", options.EmailProvider);
        options.Smtp.Host = "";
        var (author, _) = await SignIn(factory, "file-author");
        var (viewer, viewerSession) = await SignIn(factory, "file-viewer");
        await MakeFriends(author, viewer, viewerSession.User.Id);
        var place = await author.CreatePlaceAsync("file");
        var review = await author.CreatePlaceReviewAsync(place, VisibilityLevel.Friends, 3, "Saved without SMTP");
        await Upload(author, review);
        var id = await Report(viewer, review);
        using var scope = factory.Services.CreateScope();
        var delivery = scope.ServiceProvider.GetRequiredService<ReportDelivery>();
        await delivery.DeliverPendingAsync(default);
        var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var report = await db.ContentReports.Include(x => x.Photos).SingleAsync(x => x.Id == id);
        Assert.NotNull(report.EmailProcessedAtUtc);
        var directory = Path.Combine(options.EmailDirectory, id.ToString("N"));
        var file = Assert.Single(Directory.GetFiles(directory, "*.eml"));
        var mime = await File.ReadAllTextAsync(file);
        Assert.Contains("To: hubert.mar@icloud.com", mime);
        Assert.Contains("multipart/", mime);
        Assert.Contains("review.json", mime);
        Assert.Contains("Content-ID: <photo-", mime);
        Assert.Contains("image/jpeg", mime);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(directory));
        }
        await delivery.DeliverPendingAsync(default);
        Assert.Single(Directory.GetFiles(directory, "*.eml"));
        report.EmailProcessedAtUtc = DateTime.UtcNow.AddDays(-options.EvidenceRetentionDays - 1);
        // Removal/ban can resolve another reporter's report before its mail has
        // been prepared. This evidence must not be kept forever either.
        var resolved = new ContentReport
        {
            Id = Guid.CreateVersion7(), ReporterUserId = Guid.NewGuid(), AuthorUserId = report.AuthorUserId,
            ReviewId = review, ReviewType = "placeReview", SnapshotJson = report.SnapshotJson,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-40), NextDeliveryAtUtc = DateTime.UtcNow,
            ResolvedAtUtc = DateTime.UtcNow.AddDays(-31), Resolution = "remove"
        };
        resolved.Photos.Add(new ReportPhoto
        {
            Id = Guid.CreateVersion7(), ContentReportId = resolved.Id,
            SourcePhotoId = report.Photos[0].SourcePhotoId, PreviewJpeg = report.Photos[0].PreviewJpeg
        });
        db.ContentReports.Add(resolved);
        await db.SaveChangesAsync();
        await delivery.DeliverPendingAsync(default);
        Assert.False(Directory.Exists(directory));
        Assert.Empty(await db.ContentReports.ToListAsync());
        Assert.Empty(await db.Set<ReportPhoto>().ToListAsync());
        Assert.Equal(HttpStatusCode.OK, (await viewer.HttpClient.GetAsync($"/place-reviews/{review}")).StatusCode);
    }

    [Fact]
    public async Task Block_HidesAllContentAndMediaInBothDirections_AndUnblockDoesNotRestoreFriendship()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();
        var (alice, aliceSession) = await SignIn(factory, "block-alice");
        var (bob, bobSession) = await SignIn(factory, "block-bob");
        var request = await alice.SendFriendRequestAsync(bobSession.User.Id);
        (await bob.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
        var place = await alice.CreatePlaceAsync("block", 52.2, 21.0);
        var review = await alice.CreatePlaceReviewAsync(place, VisibilityLevel.Public, 5, "hidden-body");
        var photo = await Upload(alice, review);
        var avatar = await alice.ReplaceAvatarAsync(TestImages.CreateJpegBytes());
        Guid dish;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            dish = Guid.CreateVersion7();
            db.DishReviews.Add(new DishReview(dish, place, aliceSession.User.Id, null, VisibilityLevel.Public, "hidden-dish", 4, "secret", 12, "PLN"));
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await bob.HttpClient.GetAsync($"/place-reviews/{review}")).StatusCode);
        (await bob.HttpClient.PutAsync($"/me/blocks/{aliceSession.User.Id}", null)).EnsureSuccessStatusCode();
        (await bob.HttpClient.PutAsync($"/me/blocks/{aliceSession.User.Id}", null)).EnsureSuccessStatusCode();
        foreach (var path in new[] { $"/users/{aliceSession.User.Id}", $"/place-reviews/{review}", $"/dish-reviews/{dish}", photo.Url, photo.MediumUrl!, photo.ThumbnailUrl!, $"/media/users/{aliceSession.User.Id}/avatar" })
            Assert.Equal(HttpStatusCode.NotFound, (await bob.HttpClient.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.HttpClient.GetAsync($"/users/{bobSession.User.Id}")).StatusCode);
        Assert.Empty(await bob.SearchUsersAsync("block-alice"));
        Assert.Equal("[]", await bob.HttpClient.GetStringAsync("/friends"));
        Assert.Equal("[]", await alice.HttpClient.GetStringAsync("/friends"));
        Assert.Equal("[]", await bob.HttpClient.GetStringAsync($"/place-reviews?placeId={place}"));
        Assert.Equal("[]", await bob.HttpClient.GetStringAsync($"/dish-reviews?placeId={place}"));
        Assert.DoesNotContain("hidden-body", await bob.HttpClient.GetStringAsync("/feed"));
        Assert.DoesNotContain(aliceSession.User.Id.ToString(), await bob.HttpClient.GetStringAsync("/notifications"));
        Assert.DoesNotContain(aliceSession.User.Id.ToString(), await bob.HttpClient.GetStringAsync("/map/pins?north=53&south=51&east=22&west=20"));
        Assert.DoesNotContain(aliceSession.User.Id.ToString(), await bob.HttpClient.GetStringAsync("/map/places?north=53&south=51&east=22&west=20"));
        var blocks = await bob.HttpClient.GetStringAsync("/me/blocks");
        Assert.Contains(aliceSession.User.Id.ToString(), blocks);
        Assert.Contains(aliceSession.User.DisplayName, blocks);
        Assert.DoesNotContain("avatarUrl", blocks);
        Assert.Equal(HttpStatusCode.NotFound, (await alice.HttpClient.PostAsJsonAsync("/friend-requests", new { receiverUserId = bobSession.User.Id })).StatusCode);
        (await bob.HttpClient.DeleteAsync($"/me/blocks/{aliceSession.User.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await bob.HttpClient.GetAsync($"/users/{aliceSession.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.HttpClient.GetAsync($"/place-reviews/{review}")).StatusCode);
        Assert.Equal("[]", await bob.HttpClient.GetStringAsync("/friends"));
        await MakeFriends(alice, bob, bobSession.User.Id);
        Assert.Equal(HttpStatusCode.OK, (await bob.HttpClient.GetAsync(photo.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.HttpClient.PutAsync($"/me/blocks/{bobSession.User.Id}", null)).StatusCode);
    }

    [Fact]
    public async Task ReverseBlocks_AreIndependent_AndPendingInvitesCannotBeAccepted()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();
        var (alice, a) = await SignIn(factory, "reverse-a");
        var (bob, b) = await SignIn(factory, "reverse-b");
        var request = await alice.SendFriendRequestAsync(b.User.Id);
        (await bob.HttpClient.PutAsync($"/me/blocks/{a.User.Id}", null)).EnsureSuccessStatusCode();
        (await alice.HttpClient.PutAsync($"/me/blocks/{b.User.Id}", null)).EnsureSuccessStatusCode();
        (await bob.HttpClient.DeleteAsync($"/me/blocks/{a.User.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await bob.HttpClient.GetAsync($"/users/{a.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", null)).StatusCode);
        (await alice.HttpClient.DeleteAsync($"/me/blocks/{b.User.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await bob.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", null)).StatusCode);
        Assert.Equal("[]", await bob.HttpClient.GetStringAsync("/friend-requests/incoming"));
    }

    [Fact]
    public async Task Report_SnapshotsReviewAndPhotos_Deduplicates_AndRetriesDelivery()
    {
        var sender = new RecordingSender { FailNext = true };
        await using var factory = ReportingFactory(sender);
        await factory.InitializeAsync();
        var (author, a) = await SignIn(factory, "report-author");
        var (viewer, viewerSession) = await SignIn(factory, "report-viewer");
        await MakeFriends(author, viewer, viewerSession.User.Id);
        var place = await author.CreatePlaceAsync("report");
        var review = await author.CreatePlaceReviewAsync(place, VisibilityLevel.Public, 3, "<script>alert('unsafe')</script>");
        var photo = await Upload(author, review);
        var id = await Report(viewer, review);
        Assert.Equal(id, await Report(viewer, review));
        (await author.HttpClient.DeleteAsync($"/place-reviews/{review}")).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        var stored = await db.ContentReports.Include(x => x.Photos).SingleAsync();
        Assert.Equal(a.User.Id, stored.AuthorUserId);
        Assert.Contains("unsafe", stored.SnapshotJson);
        Assert.Equal(photo.Id, Assert.Single(stored.Photos).SourcePhotoId);
        Assert.NotEmpty(stored.Photos[0].PreviewJpeg);
        var delivery = scope.ServiceProvider.GetRequiredService<ReportDelivery>();
        await delivery.DeliverPendingAsync(default);
        Assert.Null(stored.EmailProcessedAtUtc);
        Assert.Equal(1, stored.DeliveryAttempts);
        stored.NextDeliveryAtUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        await delivery.DeliverPendingAsync(default);
        Assert.NotNull(stored.EmailProcessedAtUtc);
        Assert.Equal(2, stored.DeliveryAttempts);
        Assert.Single(sender.Delivered);
        var mailSender = new ReportEmailSender(scope.ServiceProvider.GetRequiredService<IOptions<ModerationOptions>>(), scope.ServiceProvider.GetRequiredService<ModerationLinks>());
        using var mail = mailSender.BuildMessage(stored);
        Assert.Equal("hubert.mar@icloud.com", Assert.Single(mail.To).Address);
        Assert.Single(mail.Attachments);
        var html = mail.AlternateViews.Single(x => x.ContentType.MediaType == "text/html");
        Assert.Single(html.LinkedResources);
        using var reader = new StreamReader(html.ContentStream);
        var body = await reader.ReadToEndAsync();
        Assert.Contains("/moderation/remove?token=", body);
        Assert.Contains("/moderation/ban?token=", body);
        Assert.DoesNotContain("<script>", body);
        Assert.DoesNotContain("Bearer", body);
    }

    [Fact]
    public async Task Reports_RejectOwnPrivateBlockedAndUnknownContent_AndEnforceDailyLimit()
    {
        await using var factory = ReportingFactory(new RecordingSender(), 1);
        await factory.InitializeAsync();
        var (author, a) = await SignIn(factory, "validation-author");
        var (viewer, viewerSession) = await SignIn(factory, "validation-viewer");
        await MakeFriends(author, viewer, viewerSession.User.Id);
        var place = await author.CreatePlaceAsync("validation");
        var review = await author.CreatePlaceReviewAsync(place, VisibilityLevel.Private, 2, "private");
        Assert.Equal(HttpStatusCode.NotFound, (await PostReport(viewer, review)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostReport(author, review)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostReport(viewer, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PostReport(viewer, review, "user")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            (await db.PlaceReviews.SingleAsync(x => x.Id == review)).Update(VisibilityLevel.Public, 2, "public", "public");
            await db.SaveChangesAsync();
        }
        (await viewer.HttpClient.PutAsync($"/me/blocks/{a.User.Id}", null)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await PostReport(viewer, review)).StatusCode);
        (await viewer.HttpClient.DeleteAsync($"/me/blocks/{a.User.Id}")).EnsureSuccessStatusCode();
        await MakeFriends(author, viewer, viewerSession.User.Id);
        await Report(viewer, review);
        var secondPlace = await author.CreatePlaceAsync("validation2");
        var second = await author.CreatePlaceReviewAsync(secondPlace, VisibilityLevel.Public, 4, "second");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PostReport(viewer, second)).StatusCode);
    }

    [Theory]
    [InlineData("placeReview")]
    [InlineData("dishReview")]
    public async Task Remove_RequiresExplicitPostAndCsrf_HidesReviewAndPhotos_AndCannotReplay(string reviewType)
    {
        await using var factory = ReportingFactory(new RecordingSender());
        await factory.InitializeAsync();
        var (author, a) = await SignIn(factory, "remove-author");
        var (viewer, viewerSession) = await SignIn(factory, "remove-viewer");
        await MakeFriends(author, viewer, viewerSession.User.Id);
        var place = await author.CreatePlaceAsync("remove");
        var review = await author.CreatePlaceReviewAsync(place, VisibilityLevel.Public, 2, "remove-me");
        var photo = await Upload(author, review);
        if (reviewType == "dishReview")
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            review = Guid.CreateVersion7();
            db.DishReviews.Add(new DishReview(review, place, a.User.Id, null, VisibilityLevel.Public, "dish", 2, "remove-dish", 5, "PLN"));
            await db.SaveChangesAsync();
            photo = await Upload(author, review, "dishReviewId");
        }
        var id = await Report(viewer, review, reviewType);
        var link = Link(factory, id, "remove");
        using var browser = factory.CreateClient();
        var page = await browser.GetAsync(link);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("no-store", page.Headers.CacheControl!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await viewer.HttpClient.GetAsync($"/{(reviewType == "placeReview" ? "place-reviews" : "dish-reviews")}/{review}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsync("/moderation/remove", new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = Token(link) }))).StatusCode);
        var fields = await ConfirmationFields(browser, link);
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/moderation/remove", new FormUrlEncodedContent(fields))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.HttpClient.GetAsync(photo.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await author.HttpClient.GetAsync($"/{(reviewType == "placeReview" ? "place-reviews" : "dish-reviews")}/{review}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.PostAsync("/moderation/remove", new FormUrlEncodedContent(fields))).StatusCode);
        // A token issued for removal cannot be repurposed into a ban token.
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/moderation/ban?token=" + Uri.EscapeDataString(Token(link)))).StatusCode);
        (await author.HttpClient.DeleteAsync("/me")).EnsureSuccessStatusCode();
        using var verifyScope = factory.Services.CreateScope();
        Assert.Empty(await verifyScope.ServiceProvider.GetRequiredService<TrustMapDbContext>().Photos.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Ban_RequiresAdministratorPassword_RevokesSessionsAndHidesAllAuthorContent()
    {
        await using var factory = ReportingFactory(new RecordingSender());
        await factory.InitializeAsync();
        var (author, a) = await SignIn(factory, "ban-author");
        var (viewer, viewerSession) = await SignIn(factory, "ban-viewer");
        await MakeFriends(author, viewer, viewerSession.User.Id);
        var place = await author.CreatePlaceAsync("ban");
        var review = await author.CreatePlaceReviewAsync(place, VisibilityLevel.Public, 1, "ban-me");
        var photo = await Upload(author, review);
        var id = await Report(viewer, review);
        using var browser = factory.CreateClient();
        var fields = await ConfirmationFields(browser, Link(factory, id, "ban"));
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.PostAsync("/moderation/ban", new FormUrlEncodedContent(fields))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await author.HttpClient.GetAsync("/me")).StatusCode);
        fields["adminPassword"] = AdminPassword;
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/moderation/ban", new FormUrlEncodedContent(fields))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.HttpClient.GetAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.HttpClient.GetAsync($"/users/{a.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.HttpClient.GetAsync(photo.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await author.HttpClient.PostAsJsonAsync("/auth/apple", new { identityToken = "apple:ban-author" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        Assert.Empty(await db.RefreshTokens.Where(x => x.UserId == a.User.Id).ToListAsync());
        Assert.Empty(await db.DeviceTokens.Where(x => x.UserId == a.User.Id).ToListAsync());
    }

    [Fact]
    public async Task Ignore_PreservesContent_AndInvalidExpiredLinksAreRejected()
    {
        await using var factory = ReportingFactory(new RecordingSender());
        await factory.InitializeAsync();
        var (author, _) = await SignIn(factory, "ignore-author");
        var (viewer, viewerSession) = await SignIn(factory, "ignore-viewer");
        await MakeFriends(author, viewer, viewerSession.User.Id);
        var place = await author.CreatePlaceAsync("ignore");
        var review = await author.CreatePlaceReviewAsync(place, VisibilityLevel.Public, 5, "keep");
        var id = await Report(viewer, review);
        using var browser = factory.CreateClient();
        var fields = await ConfirmationFields(browser, Link(factory, id, "ignore"));
        Assert.Equal(HttpStatusCode.OK, (await browser.PostAsync("/moderation/ignore", new FormUrlEncodedContent(fields))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.HttpClient.GetAsync($"/place-reviews/{review}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/moderation/remove?token=invalid")).StatusCode);
        var expired = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("TrustMap.Moderation.v1", "remove").ToTimeLimitedDataProtector()
            .Protect(id.ToString("D"), DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/moderation/remove?token=" + Uri.EscapeDataString(expired))).StatusCode);
    }

    [Fact]
    public async Task UnconfiguredReporting_IsExplicitlyUnavailable()
    {
        await using var factory = new TrustMapWebApplicationFactory(configurationOverrides: new Dictionary<string, string?> { ["Moderation:Enabled"] = "false" });
        await factory.InitializeAsync();
        var (client, _) = await SignIn(factory, "unconfigured");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await PostReport(client, Guid.NewGuid())).StatusCode);
    }

    private static TrustMapWebApplicationFactory ReportingFactory(RecordingSender? sender = null, int maxReports = 20) => new(
        services =>
        {
            if (sender == null) return;
            services.RemoveAll<IReportEmailSender>();
            services.AddSingleton<IReportEmailSender>(sender);
        },
        new Dictionary<string, string?>
        {
            ["Moderation:Enabled"] = "true", ["Moderation:PublicBaseUrl"] = "https://moderation.test",
            ["Moderation:AdminPasswordSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AdminPassword))),
            ["Moderation:Smtp:Host"] = "smtp.test", ["Moderation:Smtp:SenderEmail"] = "reports@trustmap.test",
            ["Moderation:MaxReportsPerUserPerDay"] = maxReports.ToString()
        });

    private static async Task MakeFriends(TestApiClient author, TestApiClient viewer, Guid viewerId)
    {
        var request = await author.SendFriendRequestAsync(viewerId);
        (await viewer.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", null)).EnsureSuccessStatusCode();
    }

    private static async Task<(TestApiClient, AuthSessionDto)> SignIn(TrustMapWebApplicationFactory factory, string subject)
    {
        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync(subject, subject);
        client.Authenticate(session.AccessToken);
        return (client, session);
    }

    private static Task<HttpResponseMessage> PostReport(TestApiClient client, Guid id, string type = "placeReview") =>
        client.HttpClient.PostAsJsonAsync("/reports", new { reviewId = id, reviewType = type });

    private static async Task<Guid> Report(TestApiClient client, Guid review, string type = "placeReview")
    {
        var response = await PostReport(client, review, type);
        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<PhotoDto> Upload(TestApiClient client, Guid review, string field = "placeReviewId")
    {
        using var content = new MultipartFormDataContent();
        var image = new ByteArrayContent(TestImages.CreateJpegBytes());
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(image, "file", "photo.jpg");
        content.Add(new StringContent(review.ToString()), field);
        var response = await client.HttpClient.PostAsync("/photos/upload", content);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PhotoDto>(JsonDefaults.Options))!;
    }

    private static string Link(TrustMapWebApplicationFactory factory, Guid id, string action) =>
        new Uri(factory.Services.GetRequiredService<ModerationLinks>().Create(id, action)).PathAndQuery;

    private static string Token(string link) => Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..]);

    private static async Task<Dictionary<string, string>> ConfirmationFields(HttpClient browser, string link)
    {
        var response = await browser.GetAsync(link);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        return Regex.Matches(html, "<input type=\"hidden\" name=\"([^\"]+)\" value=\"([^\"]+)\">")
            .ToDictionary(m => WebUtility.HtmlDecode(m.Groups[1].Value), m => WebUtility.HtmlDecode(m.Groups[2].Value));
    }

    private sealed class RecordingSender : IReportEmailSender
    {
        public bool FailNext { get; set; }
        public List<Guid> Delivered { get; } = [];
        public Task SendAsync(ContentReport report, CancellationToken cancellationToken)
        {
            if (FailNext) { FailNext = false; throw new IOException("Simulated SMTP outage"); }
            Delivered.Add(report.Id);
            return Task.CompletedTask;
        }
    }
}
