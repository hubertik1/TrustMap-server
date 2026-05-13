using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class EnumValidationTests
{
    [Fact]
    public async Task ReviewEndpoints_RejectUndefinedMissingAndNumericVisibilityValues()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice-enum-api", "Alice Enum");
        client.Authenticate(session.AccessToken);

        var validPlaceId = await client.CreatePlaceAsync("enum-valid");
        var validCreate = await client.HttpClient.PostAsync(
            "/place-reviews",
            Json($$"""
            {
              "placeId": "{{validPlaceId}}",
              "categoryId": "{{TestApiClient.DefaultCategoryId}}",
              "visibility": "Public",
              "rating": 5,
              "title": "Valid",
              "body": "Valid enum string"
            }
            """));
        Assert.Equal(HttpStatusCode.Created, validCreate.StatusCode);

        var invalidNumericPlaceId = await client.CreatePlaceAsync("enum-invalid-numeric");
        await AssertBadRequestWithoutInternalDetailsAsync(await client.HttpClient.PostAsync(
            "/place-reviews",
            Json($$"""
            {
              "placeId": "{{invalidNumericPlaceId}}",
              "categoryId": "{{TestApiClient.DefaultCategoryId}}",
              "visibility": 999,
              "rating": 5,
              "title": "Invalid",
              "body": "Invalid enum number"
            }
            """)));

        var missingVisibilityPlaceId = await client.CreatePlaceAsync("enum-missing");
        await AssertBadRequestWithoutInternalDetailsAsync(await client.HttpClient.PostAsync(
            "/place-reviews",
            Json($$"""
            {
              "placeId": "{{missingVisibilityPlaceId}}",
              "categoryId": "{{TestApiClient.DefaultCategoryId}}",
              "rating": 5,
              "title": "Missing",
              "body": "Missing enum"
            }
            """)));

        var unknownStringPlaceId = await client.CreatePlaceAsync("enum-unknown-string");
        await AssertBadRequestWithoutInternalDetailsAsync(await client.HttpClient.PostAsync(
            "/place-reviews",
            Json($$"""
            {
              "placeId": "{{unknownStringPlaceId}}",
              "categoryId": "{{TestApiClient.DefaultCategoryId}}",
              "visibility": "VisibleToEveryoneMaybe",
              "rating": 5,
              "title": "Unknown",
              "body": "Unknown enum string"
            }
            """)));

        var updatePlaceId = await client.CreatePlaceAsync("enum-update");
        var updateReviewId = await client.CreatePlaceReviewAsync(updatePlaceId, VisibilityLevel.Friends, 4, "Update target");
        await AssertBadRequestWithoutInternalDetailsAsync(await client.HttpClient.PatchAsync(
            $"/place-reviews/{updateReviewId}",
            Json($$"""
            {
              "categoryId": "{{TestApiClient.DefaultCategoryId}}",
              "visibility": 999,
              "rating": 4,
              "title": "Invalid update",
              "body": "Invalid update enum"
            }
            """)));

        var dishPlaceId = await client.CreatePlaceAsync("enum-dish");
        var parentReviewId = await client.CreatePlaceReviewAsync(dishPlaceId, VisibilityLevel.Public, 5, "Parent");
        await AssertBadRequestWithoutInternalDetailsAsync(await client.HttpClient.PostAsync(
            "/dish-reviews",
            Json($$"""
            {
              "placeId": "{{dishPlaceId}}",
              "categoryId": "{{TestApiClient.DefaultCategoryId}}",
              "placeReviewId": "{{parentReviewId}}",
              "visibility": 999,
              "dishName": "Pierogi",
              "rating": 5,
              "body": "Invalid dish enum"
            }
            """)));

        var dishReviewId = await client.CreateDishReviewAsync(dishPlaceId, parentReviewId, VisibilityLevel.Friends, "Pierogi", 5);
        await AssertBadRequestWithoutInternalDetailsAsync(await client.HttpClient.PatchAsync(
            $"/dish-reviews/{dishReviewId}",
            Json($$"""
            {
              "categoryId": "{{TestApiClient.DefaultCategoryId}}",
              "visibility": 999,
              "dishName": "Pierogi",
              "rating": 5,
              "body": "Invalid dish update"
            }
            """)));
    }

    [Fact]
    public void DomainEntities_RejectUndefinedEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaceReview(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            (VisibilityLevel)999,
            5,
            "Invalid",
            "Invalid"));

        var placeReview = new PlaceReview(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            VisibilityLevel.Friends,
            4,
            "Valid",
            "Valid");
        Assert.Throws<ArgumentOutOfRangeException>(() => placeReview.Update((VisibilityLevel)999, 4, "Invalid", "Invalid"));

        Assert.Throws<ArgumentOutOfRangeException>(() => new DishReview(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            (VisibilityLevel)999,
            "Pierogi",
            5,
            "Invalid",
            null,
            null));

        var user = new User(Guid.CreateVersion7(), "alice", "Alice");
        Assert.Throws<ArgumentOutOfRangeException>(() => user.UpdatePrivacySettings(
            (VisibilityLevel)999,
            VisibilityLevel.Friends,
            VisibilityLevel.Public,
            VisibilityLevel.Public));
        Assert.Throws<ArgumentOutOfRangeException>(() => user.SetStatus((UserAccountStatus)999));

        Assert.Throws<ArgumentOutOfRangeException>(() => new UserNotification(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            (NotificationType)999,
            friendRequestId: Guid.CreateVersion7()));

        Assert.Throws<ArgumentOutOfRangeException>(() => new UserCategoryMembership(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            (CategoryMembershipSource)999));

        Assert.Throws<ArgumentOutOfRangeException>(() => Place.IsPublicCatalogSource((PlaceSourceType)999));

        var friendRequest = new FriendRequest(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        var transition = typeof(FriendRequest).GetMethod("Transition", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var exception = Assert.Throws<TargetInvocationException>(() => transition.Invoke(friendRequest, [(FriendRequestStatus)999]));
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
    }

    [Fact]
    public async Task DatabaseConstraints_RejectInvalidPersistedEnumValues()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());
        var alice = await aliceClient.SignInAsync("alice-enum-db", "Alice Enum DB");
        var bob = await bobClient.SignInAsync("bob-enum-db", "Bob Enum DB");
        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        var placeId = await aliceClient.CreatePlaceAsync("enum-db");
        var placeReviewId = await aliceClient.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 5, "Place review");
        var dishReviewId = await aliceClient.CreateDishReviewAsync(placeId, placeReviewId, VisibilityLevel.Friends, "Pierogi", 5);
        var friendRequest = await aliceClient.SendFriendRequestAsync(bob.User.Id);
        var notificationId = await GetSingleNotificationIdAsync(factory);

        await AssertSqliteConstraintFailsAsync(
            factory,
            $"UPDATE place_reviews SET Visibility = 999 WHERE Id = {placeReviewId}");
        await AssertSqliteConstraintFailsAsync(
            factory,
            $"UPDATE dish_reviews SET Visibility = 999 WHERE Id = {dishReviewId}");
        await AssertSqliteConstraintFailsAsync(
            factory,
            $"UPDATE friend_requests SET Status = 999 WHERE Id = {friendRequest.Id}");
        await AssertSqliteConstraintFailsAsync(
            factory,
            $"UPDATE notifications SET Type = 999 WHERE Id = {notificationId}");
        await AssertSqliteConstraintFailsAsync(
            factory,
            $"UPDATE places SET SourceType = 'UnknownSource' WHERE Id = {placeId}");
        await AssertSqliteConstraintFailsAsync(
            factory,
            $"UPDATE users SET ReviewVisibility = 999 WHERE Id = {alice.User.Id}");
    }

    private static StringContent Json(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private static async Task AssertBadRequestWithoutInternalDetailsAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("StackTrace", content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TrustMap.", content, StringComparison.Ordinal);
        Assert.DoesNotContain("System.", content, StringComparison.Ordinal);
    }

    private static async Task AssertSqliteConstraintFailsAsync(
        TrustMapWebApplicationFactory factory,
        FormattableString sql)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();

        var exception = await Assert.ThrowsAsync<SqliteException>(() => dbContext.Database.ExecuteSqlInterpolatedAsync(sql));
        Assert.Equal(19, exception.SqliteErrorCode);
    }

    private static async Task<Guid> GetSingleNotificationIdAsync(TrustMapWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();

        return await dbContext.Notifications
            .AsNoTracking()
            .Select(x => x.Id)
            .SingleAsync();
    }
}
