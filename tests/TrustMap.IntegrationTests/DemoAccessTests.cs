using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Services;
using TrustMap.Api.Services.Moderation;
using TrustMap.Api.Features.Reviews;
using Microsoft.Extensions.Options;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class DemoAccessTests
{
    private const string Password = "integration-test-demo-password";

    private static TrustMapWebApplicationFactory EnabledFactory()
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        return new TrustMapWebApplicationFactory(configurationOverrides: new Dictionary<string, string?>
        {
            ["DemoAccess:Enabled"] = "true",
            ["DemoAccess:Username"] = "appreview",
            ["DemoAccess:AccountId"] = Guid.NewGuid().ToString(),
            ["DemoAccess:PasswordSalt"] = Convert.ToBase64String(salt),
            ["DemoAccess:PasswordHash"] = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(
                Password, salt, DemoAccessOptions.PasswordIterations, HashAlgorithmName.SHA256, 32))
        });
    }

    private static async Task<AuthSessionDto> SignInAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/auth/demo", new { username = "appreview", password = Password });
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonDefaults.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return session;
    }

    [Fact]
    public async Task DisabledOrInvalidCredentialsCannotProvisionAccounts()
    {
        await using (var disabled = new TrustMapWebApplicationFactory())
        {
            await disabled.InitializeAsync();
            var response = await disabled.CreateClient().PostAsJsonAsync("/auth/demo",
                new { username = "appreview", password = Password });
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        await using var factory = EnabledFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        foreach (var credentials in new[] { new { username = "appreview", password = "wrong" },
                     new { username = "someone-else", password = Password } })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/demo", credentials)).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TrustMapDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task DemoHasRealContentPhotosRequestsAndRefreshableSession()
    {
        await using var factory = EnabledFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        var session = await SignInAsync(client);
        Assert.Equal("Alex · Demo", session.User.DisplayName);

        Assert.Equal(3, (await client.GetFromJsonAsync<JsonArray>("/friends"))!.Count);
        Assert.Single((await client.GetFromJsonAsync<JsonArray>("/friend-requests/incoming"))!);
        Assert.Single((await client.GetFromJsonAsync<JsonArray>("/friend-requests/outgoing"))!);
        Assert.NotEmpty((await client.GetFromJsonAsync<JsonObject>("/feed"))!["items"]!.AsArray());
        var places = (await client.GetFromJsonAsync<JsonArray>("/places"))!;
        Assert.Equal(6, places.Count);
        Assert.All(places, place => Assert.EndsWith(" · Demo", place!["displayName"]!.GetValue<string>()));
        Assert.Contains(places, place => place!["displayName"]!.GetValue<string>() == "Sunday Table · Demo");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            Assert.Equal(7, await db.Users.CountAsync());
            Assert.Equal(6, await db.Places.CountAsync());
            Assert.Equal(7, await db.PlaceReviews.CountAsync());
            Assert.Equal(6, await db.DishReviews.CountAsync());
            Assert.Empty(await db.AppleIdentities.ToListAsync());
            var photo = await db.Photos.SingleAsync();
            var image = await client.GetAsync($"/media/photos/{photo.Id}");
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.StartsWith("image/", image.Content.Headers.ContentType!.MediaType!);
            var mia = await db.Users.SingleAsync(x => x.DisplayName == "Mia · Demo");
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/media/users/{mia.Id}/avatar")).StatusCode);
        }

        var again = await SignInAsync(client);
        Assert.Equal(session.User.Id, again.User.Id);
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(7, await scope.ServiceProvider.GetRequiredService<TrustMapDbContext>().Users.CountAsync());

        var refreshed = await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = again.RefreshToken });
        refreshed.EnsureSuccessStatusCode();
        var replacement = (await refreshed.Content.ReadFromJsonAsync<AuthSessionDto>(JsonDefaults.Options))!;
        Assert.NotEqual(again.RefreshToken, replacement.RefreshToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", replacement.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/auth/logout", new { refreshToken = replacement.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/auth/refresh", new { refreshToken = replacement.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task ReportingBlockingAndEditsUseNormalRulesAndSurviveNextLogin()
    {
        await using var factory = EnabledFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        var session = await SignInAsync(client);
        Guid authorId, reviewId, ownReviewId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var review = await db.PlaceReviews.FirstAsync(x => x.AuthorUserId != session.User.Id);
            authorId = review.AuthorUserId;
            reviewId = review.Id;
            ownReviewId = await db.PlaceReviews.Where(x => x.AuthorUserId == session.User.Id).Select(x => x.Id).FirstAsync();
        }
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/reports",
            new { reviewType = "placeReview", reviewId })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsync($"/me/blocks/{authorId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/place-reviews/{ownReviewId}")).StatusCode);
        await SignInAsync(client);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/place-reviews/{reviewId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/place-reviews/{ownReviewId}")).StatusCode);
        using var finalScope = factory.Services.CreateScope();
        Assert.Single(await finalScope.ServiceProvider.GetRequiredService<TrustMapDbContext>().ContentReports.ToListAsync());
    }

    [Fact]
    public async Task OtherUsersCannotReadFriendsOnlyDemoReviews()
    {
        await using var factory = EnabledFactory();
        await factory.InitializeAsync();
        await SignInAsync(factory.CreateClient());
        Guid reviewId;
        using (var scope = factory.Services.CreateScope())
            reviewId = await scope.ServiceProvider.GetRequiredService<TrustMapDbContext>()
                .PlaceReviews.Select(x => x.Id).FirstAsync();
        var normal = new TestApiClient(factory.CreateClient());
        var normalSession = await normal.SignInAsync("normal-user", "Normal user");
        normal.Authenticate(normalSession.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await normal.HttpClient.GetAsync($"/place-reviews/{reviewId}")).StatusCode);
    }

    [Fact]
    public async Task DemoReviewReportsReachModerationWithPhotoEvidence_AndBlockHidesEverySurface()
    {
        await using var factory = EnabledFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        await SignInAsync(client);
        var dishes = (await client.GetFromJsonAsync<DishReviewDto[]>("/dish-reviews", JsonDefaults.Options))!;
        var dish = Assert.Single(dishes, x => x.Photos.Count > 0);
        var photo = Assert.Single(dish.Photos);
        var reportResponse = await client.PostAsJsonAsync("/reports", new { reviewType = "dishReview", reviewId = dish.Id });
        Assert.Equal(HttpStatusCode.Accepted, reportResponse.StatusCode);
        var reportId = (await reportResponse.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<Guid>();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ReportDelivery>().DeliverPendingAsync(default);
            var db = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
            var report = await db.ContentReports.Include(x => x.Photos).SingleAsync(x => x.Id == reportId);
            Assert.NotNull(report.EmailProcessedAtUtc);
            Assert.Single(report.Photos);
            Assert.NotEmpty(report.Photos[0].PreviewJpeg);
            var options = scope.ServiceProvider.GetRequiredService<IOptions<ModerationOptions>>().Value;
            var mail = Assert.Single(Directory.GetFiles(Path.Combine(options.EmailDirectory, reportId.ToString("N")), "*.eml"));
            var mime = await File.ReadAllTextAsync(mail);
            Assert.Contains("review.json", mime);
            Assert.Contains("image/jpeg", mime);
        }

        var author = dish.Author.Id;
        (await client.PutAsync($"/me/blocks/{author}", null)).EnsureSuccessStatusCode();
        foreach (var path in new[] { $"/users/{author}", $"/dish-reviews/{dish.Id}", photo.Url,
                     photo.MediumUrl!, photo.ThumbnailUrl!, $"/media/users/{author}/avatar" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/feed", "/map/pins?north=52&south=51&east=0&west=-1", "/notifications" })
            Assert.DoesNotContain(author.ToString(), await client.GetStringAsync(path), StringComparison.OrdinalIgnoreCase);

        Assert.Single((await client.GetFromJsonAsync<JsonArray>("/me/blocks"))!);
        (await client.DeleteAsync($"/me/blocks/{author}")).EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/me/blocks"))!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/users/{author}")).StatusCode);
        // Removing a block does not silently restore friendship or friend-only reviews.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/dish-reviews/{dish.Id}")).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<JsonArray>("/friends"))!.Count);
    }

    [Fact]
    public async Task DemoCanEditReviewsUploadAndDeletePhotos_AndCreateThenDeleteContent()
    {
        await using var factory = EnabledFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        var session = await SignInAsync(client);
        var ownReviews = (await client.GetFromJsonAsync<PlaceReviewDto[]>(
            $"/place-reviews?authorUserId={session.User.Id}", JsonDefaults.Options))!;
        var own = ownReviews.First(x => x.CategoryId == CategoryAssignmentService.RestaurantsCategoryId);
        var update = await client.PatchAsJsonAsync($"/place-reviews/{own.Id}", new {
            categoryId = own.CategoryId, visibility = "Friends", rating = 3,
            title = "Edited during demo QA", body = "Saved through the normal review endpoint."
        });
        update.EnsureSuccessStatusCode();
        await SignInAsync(client);
        var edited = (await client.GetFromJsonAsync<PlaceReviewDto>($"/place-reviews/{own.Id}", JsonDefaults.Options))!;
        Assert.Equal(3, edited.Rating);
        Assert.Equal("Edited during demo QA", edited.Title);

        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent(own.Id.ToString()), "placeReviewId");
        var bytes = new ByteArrayContent(TestImages.CreateJpegBytes());
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        upload.Add(bytes, "file", "qa-photo.jpg");
        var uploaded = await client.PostAsync("/photos/upload", upload);
        uploaded.EnsureSuccessStatusCode();
        var photo = (await uploaded.Content.ReadFromJsonAsync<PhotoDto>(JsonDefaults.Options))!;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(photo.Url)).StatusCode);
        (await client.DeleteAsync($"/photos/{photo.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(photo.Url)).StatusCode);

        var ownDish = (await client.GetFromJsonAsync<DishReviewDto[]>(
            $"/dish-reviews?authorUserId={session.User.Id}", JsonDefaults.Options))!.First();
        (await client.PatchAsJsonAsync($"/dish-reviews/{ownDish.Id}", new {
            categoryId = CategoryAssignmentService.RestaurantsCategoryId, visibility = "Friends",
            dishName = "Updated demo dish", rating = 4, body = "Edited", priceAmount = 15.25m, currencyCode = "GBP"
        })).EnsureSuccessStatusCode();
        var updatedDish = (await client.GetFromJsonAsync<DishReviewDto>($"/dish-reviews/{ownDish.Id}", JsonDefaults.Options))!;
        Assert.Equal(15.25m, updatedDish.PriceAmount);
        Assert.Equal("Updated demo dish", updatedDish.DishName);
        (await client.DeleteAsync($"/dish-reviews/{ownDish.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/dish-reviews/{ownDish.Id}")).StatusCode);

        var helper = new TestApiClient(client);
        var newPlace = await helper.CreatePlaceAsync("demo-qa-new");
        var newReview = await helper.CreatePlaceReviewAsync(newPlace, VisibilityLevel.Friends, 5, "New demo review");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/place-reviews/{newReview}")).StatusCode);
        (await client.DeleteAsync($"/place-reviews/{newReview}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/place-reviews/{newReview}")).StatusCode);
    }

    [Fact]
    public async Task DemoFriendRequestsAndNotificationsAreActionable()
    {
        await using var factory = EnabledFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        await SignInAsync(client);
        var incoming = (await client.GetFromJsonAsync<JsonArray>("/friend-requests/incoming"))!.Single()!;
        (await client.PostAsync($"/friend-requests/{incoming["id"]!.GetValue<string>()}/accept", null)).EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/friend-requests/incoming"))!);
        Assert.Equal(4, (await client.GetFromJsonAsync<JsonArray>("/friends"))!.Count);
        var outgoing = (await client.GetFromJsonAsync<JsonArray>("/friend-requests/outgoing"))!.Single()!;
        (await client.PostAsync($"/friend-requests/{outgoing["id"]!.GetValue<string>()}/cancel", null)).EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<JsonArray>("/friend-requests/outgoing"))!);
        var results = (await client.GetFromJsonAsync<JsonArray>("/users/search?q=Zoe"))!;
        var zoe = Assert.Single(results)!;
        var sent = await client.PostAsJsonAsync("/friend-requests", new { receiverUserId = zoe["user"]!["id"]!.GetValue<Guid>() });
        Assert.Equal(HttpStatusCode.Created, sent.StatusCode);
        Assert.Single((await client.GetFromJsonAsync<JsonArray>("/friend-requests/outgoing"))!);
        var unread = (await client.GetFromJsonAsync<JsonObject>("/notifications/unread-count"))!["count"]!.GetValue<int>();
        Assert.True(unread > 0);
        (await client.PatchAsync("/notifications/read-all", null)).EnsureSuccessStatusCode();
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonObject>("/notifications/unread-count"))!["count"]!.GetValue<int>());
    }

    [Fact]
    public async Task DeletedDemoAccountIsNotRecreatedAndItsTokensAreRevoked()
    {
        await using var factory = EnabledFactory();
        await factory.InitializeAsync();
        var client = factory.CreateClient();
        var session = await SignInAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/auth/demo",
            new { username = "appreview", password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/auth/refresh",
            new { refreshToken = session.RefreshToken })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<TrustMapDbContext>().Users
            .IgnoreQueryFilters().SingleAsync(x => x.Id == session.User.Id);
        Assert.Equal(UserAccountStatus.Deleted, user.Status);
    }
}
