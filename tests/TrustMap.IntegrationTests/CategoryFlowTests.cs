using System.Net;
using System.Net.Http.Json;
using TrustMap.Api.Features.Categories;
using TrustMap.Api.Features.Reviews;
using TrustMap.Domain.Enums;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class CategoryFlowTests
{
    [Fact]
    public async Task DefaultCategories_AppearForEveryUser_AndCannotBeDeleted()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        var myCategories = await client.FetchMyCategoriesAsync();

        Assert.Contains(myCategories, x => x.Id == TestApiClient.DefaultCategoryId && x.IsDefault && x.IsVisibleForCurrentUser);
        Assert.Contains(myCategories, x => x.Id == TestApiClient.AlternateDefaultCategoryId && x.IsDefault && x.IsVisibleForCurrentUser);

        var deleteResponse = await client.HttpClient.DeleteAsync($"/categories/{TestApiClient.DefaultCategoryId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task DefaultCategory_CanBeHiddenAndRestored_PerUser()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        var aliceHidden = await aliceClient.HideCategoryAsync(TestApiClient.DefaultCategoryId);
        Assert.True(aliceHidden.IsDefault);
        Assert.False(aliceHidden.IsVisibleForCurrentUser);
        Assert.True(aliceHidden.CanUnhide);

        var aliceVisibleCategories = await aliceClient.FetchMyCategoriesAsync();
        Assert.DoesNotContain(aliceVisibleCategories, x => x.Id == TestApiClient.DefaultCategoryId);

        var aliceHiddenCategories = await aliceClient.FetchHiddenCategoriesAsync();
        var hiddenDefault = Assert.Single(aliceHiddenCategories, x => x.Id == TestApiClient.DefaultCategoryId);
        Assert.True(hiddenDefault.IsDefault);
        Assert.True(hiddenDefault.CanUnhide);

        var bobVisibleCategories = await bobClient.FetchMyCategoriesAsync();
        Assert.Contains(bobVisibleCategories, x => x.Id == TestApiClient.DefaultCategoryId && x.IsVisibleForCurrentUser);

        var restoreResponse = await aliceClient.HttpClient.PostAsync($"/categories/{TestApiClient.DefaultCategoryId}/unhide", content: null);
        restoreResponse.EnsureSuccessStatusCode();

        var restoredCategories = await aliceClient.FetchMyCategoriesAsync();
        Assert.Contains(restoredCategories, x => x.Id == TestApiClient.DefaultCategoryId && x.IsVisibleForCurrentUser);

        var restoredHiddenCategories = await aliceClient.FetchHiddenCategoriesAsync();
        Assert.DoesNotContain(restoredHiddenCategories, x => x.Id == TestApiClient.DefaultCategoryId);
    }

    [Fact]
    public async Task FriendCategory_AdoptHideAndReAdoptFlow_PreservesSharedIdentityAndHistory()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        var request = await aliceClient.SendFriendRequestAsync(bob.User.Id);
        var acceptResponse = await bobClient.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        acceptResponse.EnsureSuccessStatusCode();

        var sunsetSpots = await aliceClient.CreateCategoryAsync("Sunset Spots");
        Assert.True(sunsetSpots.IsOwnedByCurrentUser);
        Assert.False(sunsetSpots.IsDefault);

        var bobInitialMyCategories = await bobClient.FetchMyCategoriesAsync();
        Assert.DoesNotContain(bobInitialMyCategories, x => x.Id == sunsetSpots.Id);

        var bobFriendCategories = await bobClient.FetchFriendCategoriesAsync();
        var friendCategory = Assert.Single(bobFriendCategories, x => x.Id == sunsetSpots.Id);
        Assert.True(friendCategory.CanAdopt);
        Assert.Equal(alice.User.Id, friendCategory.OwnerUserId);

        var adopted = await bobClient.AdoptCategoryAsync(sunsetSpots.Id);
        Assert.Equal(sunsetSpots.Id, adopted.Id);
        Assert.True(adopted.IsVisibleForCurrentUser);
        Assert.True(adopted.IsAdoptedByCurrentUser);

        var placeId = await bobClient.CreatePlaceAsync("sunset");
        var reviewId = await bobClient.CreatePlaceReviewAsync(
            placeId,
            VisibilityLevel.Public,
            5,
            "Great evening spot",
            adopted.Id);

        var hidden = await bobClient.HideCategoryAsync(sunsetSpots.Id);
        Assert.False(hidden.IsVisibleForCurrentUser);

        var bobVisibleCategoriesAfterHide = await bobClient.FetchMyCategoriesAsync();
        Assert.DoesNotContain(bobVisibleCategoriesAfterHide, x => x.Id == sunsetSpots.Id);

        var bobFriendCategoriesAfterHide = await bobClient.FetchFriendCategoriesAsync();
        var availableFriendCategory = Assert.Single(bobFriendCategoriesAfterHide, x => x.Id == sunsetSpots.Id);
        Assert.True(availableFriendCategory.CanAdopt);
        Assert.False(availableFriendCategory.CanUnhide);

        var bobHiddenCategoriesAfterHide = await bobClient.FetchHiddenCategoriesAsync();
        Assert.DoesNotContain(bobHiddenCategoriesAfterHide, x => x.Id == sunsetSpots.Id);

        var reAdopted = await bobClient.AdoptCategoryAsync(sunsetSpots.Id);
        Assert.Equal(sunsetSpots.Id, reAdopted.Id);
        Assert.True(reAdopted.IsVisibleForCurrentUser);

        var reviewResponse = await bobClient.HttpClient.GetFromJsonAsync<PlaceReviewDto>($"/place-reviews/{reviewId}", JsonDefaults.Options);
        Assert.NotNull(reviewResponse);
        Assert.Equal(sunsetSpots.Id, reviewResponse.CategoryId);
        Assert.Equal("Sunset Spots", reviewResponse.CategoryName);
    }

    [Fact]
    public async Task FriendCategory_RemoveReturnsToFriendsNotHidden()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var (aliceClient, bobClient, aliceUserId, _) = await CreateFriendClientsAsync(factory);

        var quietCafes = await aliceClient.CreateCategoryAsync("Quiet Cafés");

        var bobFriendCategories = await bobClient.FetchFriendCategoriesAsync();
        var availableCategory = Assert.Single(bobFriendCategories, x => x.Id == quietCafes.Id);
        Assert.True(availableCategory.CanAdopt);
        Assert.Equal(aliceUserId, availableCategory.OwnerUserId);

        var adopted = await bobClient.AdoptCategoryAsync(quietCafes.Id);
        Assert.True(adopted.IsVisibleForCurrentUser);
        Assert.True(adopted.IsAdoptedByCurrentUser);

        var bobVisibleCategories = await bobClient.FetchMyCategoriesAsync();
        Assert.Contains(bobVisibleCategories, x => x.Id == quietCafes.Id);

        bobFriendCategories = await bobClient.FetchFriendCategoriesAsync();
        Assert.DoesNotContain(bobFriendCategories, x => x.Id == quietCafes.Id);

        var removed = await bobClient.HideCategoryAsync(quietCafes.Id);
        Assert.False(removed.IsVisibleForCurrentUser);
        Assert.True(removed.CanAdopt);
        Assert.False(removed.CanUnhide);

        bobVisibleCategories = await bobClient.FetchMyCategoriesAsync();
        Assert.DoesNotContain(bobVisibleCategories, x => x.Id == quietCafes.Id);

        bobFriendCategories = await bobClient.FetchFriendCategoriesAsync();
        var returnedCategory = Assert.Single(bobFriendCategories, x => x.Id == quietCafes.Id);
        Assert.True(returnedCategory.CanAdopt);

        var bobHiddenCategories = await bobClient.FetchHiddenCategoriesAsync();
        Assert.DoesNotContain(bobHiddenCategories, x => x.Id == quietCafes.Id);

        var reAdopted = await bobClient.AdoptCategoryAsync(quietCafes.Id);
        Assert.True(reAdopted.IsVisibleForCurrentUser);

        bobVisibleCategories = await bobClient.FetchMyCategoriesAsync();
        Assert.Contains(bobVisibleCategories, x => x.Id == quietCafes.Id);
    }

    [Fact]
    public async Task OwnerDelete_RemovesCategoryForEveryone()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var (aliceClient, bobClient, _, _) = await CreateFriendClientsAsync(factory);

        var bestRamen = await aliceClient.CreateCategoryAsync("Best Ramen");
        await bobClient.AdoptCategoryAsync(bestRamen.Id);

        var deleteResponse = await aliceClient.HttpClient.DeleteAsync($"/categories/{bestRamen.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var aliceVisibleCategories = await aliceClient.FetchMyCategoriesAsync();
        Assert.DoesNotContain(aliceVisibleCategories, x => x.Id == bestRamen.Id);

        var bobVisibleCategories = await bobClient.FetchMyCategoriesAsync();
        Assert.DoesNotContain(bobVisibleCategories, x => x.Id == bestRamen.Id);

        var bobFriendCategories = await bobClient.FetchFriendCategoriesAsync();
        Assert.DoesNotContain(bobFriendCategories, x => x.Id == bestRamen.Id);

        var bobHiddenCategories = await bobClient.FetchHiddenCategoriesAsync();
        Assert.DoesNotContain(bobHiddenCategories, x => x.Id == bestRamen.Id);

        var adoptDeletedResponse = await bobClient.HttpClient.PostAsync($"/categories/{bestRamen.Id}/adopt", content: null);
        Assert.Equal(HttpStatusCode.NotFound, adoptDeletedResponse.StatusCode);
    }

    [Fact]
    public async Task HiddenDefault_NotSelectable()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var client = new TestApiClient(factory.CreateClient());
        var session = await client.SignInAsync("alice", "Alice");
        client.Authenticate(session.AccessToken);

        await client.HideCategoryAsync(TestApiClient.DefaultCategoryId);

        var firstPlaceId = await client.CreatePlaceAsync("hidden-default-create");
        var createResponse = await client.HttpClient.PostAsJsonAsync(
            "/place-reviews",
            new CreatePlaceReviewRequest
            {
                PlaceId = firstPlaceId,
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Public,
                Rating = 4,
                Title = "Hidden default",
                Body = "Should not be selectable"
            },
            JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);

        var secondPlaceId = await client.CreatePlaceAsync("hidden-default-update");
        var reviewId = await client.CreatePlaceReviewAsync(
            secondPlaceId,
            VisibilityLevel.Public,
            5,
            "Created with an active default",
            TestApiClient.AlternateDefaultCategoryId);

        var updateResponse = await client.HttpClient.PatchAsJsonAsync(
            $"/place-reviews/{reviewId}",
            new UpdatePlaceReviewRequest
            {
                CategoryId = TestApiClient.DefaultCategoryId,
                Visibility = VisibilityLevel.Public,
                Rating = 5,
                Title = "Still hidden",
                Body = "Should not become selectable"
            },
            JsonDefaults.Options);
        Assert.Equal(HttpStatusCode.Forbidden, updateResponse.StatusCode);
    }

    [Fact]
    public async Task CustomCategory_EditDeletePermissionsAndSoftDelete_Work()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        var request = await aliceClient.SendFriendRequestAsync(bob.User.Id);
        var acceptResponse = await bobClient.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        acceptResponse.EnsureSuccessStatusCode();

        var streetFood = await aliceClient.CreateCategoryAsync("Street Food Trucks");
        await bobClient.AdoptCategoryAsync(streetFood.Id);

        var duplicateCreateResponse = await aliceClient.HttpClient.PostAsJsonAsync(
            "/categories",
            new CreateCategoryRequest
            {
                Name = "Street Food Trucks"
            });
        Assert.Equal(HttpStatusCode.Conflict, duplicateCreateResponse.StatusCode);

        var forbiddenEditResponse = await bobClient.HttpClient.PatchAsJsonAsync(
            $"/categories/{streetFood.Id}",
            new UpdateCategoryRequest
            {
                Name = "Bob Rename"
            });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenEditResponse.StatusCode);

        var forbiddenDeleteResponse = await bobClient.HttpClient.DeleteAsync($"/categories/{streetFood.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDeleteResponse.StatusCode);

        var renamed = await aliceClient.UpdateCategoryAsync(streetFood.Id, "Street Food Gems");
        Assert.Equal("Street Food Gems", renamed.Name);

        var bobCategoriesAfterRename = await bobClient.FetchMyCategoriesAsync();
        var renamedForBob = Assert.Single(bobCategoriesAfterRename, x => x.Id == streetFood.Id);
        Assert.Equal("Street Food Gems", renamedForBob.Name);

        var bobPlaceId = await bobClient.CreatePlaceAsync("food-truck");
        var bobReviewId = await bobClient.CreatePlaceReviewAsync(
            bobPlaceId,
            VisibilityLevel.Public,
            4,
            "Late-night favorite",
            streetFood.Id);

        var ownerDeleteResponse = await aliceClient.HttpClient.DeleteAsync($"/categories/{streetFood.Id}");
        Assert.Equal(HttpStatusCode.NoContent, ownerDeleteResponse.StatusCode);

        var bobVisibleCategoriesAfterDelete = await bobClient.FetchMyCategoriesAsync();
        Assert.DoesNotContain(bobVisibleCategoriesAfterDelete, x => x.Id == streetFood.Id);

        var bobFriendCategoriesAfterDelete = await bobClient.FetchFriendCategoriesAsync();
        Assert.DoesNotContain(bobFriendCategoriesAfterDelete, x => x.Id == streetFood.Id);

        var bobHiddenCategoriesAfterDelete = await bobClient.FetchHiddenCategoriesAsync();
        Assert.DoesNotContain(bobHiddenCategoriesAfterDelete, x => x.Id == streetFood.Id);

        var historicalReview = await bobClient.HttpClient.GetFromJsonAsync<PlaceReviewDto>($"/place-reviews/{bobReviewId}", JsonDefaults.Options);
        Assert.NotNull(historicalReview);
        Assert.Equal(streetFood.Id, historicalReview.CategoryId);
        Assert.Equal("Street Food Gems", historicalReview.CategoryName);

        var adoptDeletedResponse = await bobClient.HttpClient.PostAsync($"/categories/{streetFood.Id}/adopt", content: null);
        Assert.Equal(HttpStatusCode.NotFound, adoptDeletedResponse.StatusCode);
    }

    private static async Task<(TestApiClient AliceClient, TestApiClient BobClient, Guid AliceUserId, Guid BobUserId)> CreateFriendClientsAsync(
        TrustMapWebApplicationFactory factory)
    {
        var aliceClient = new TestApiClient(factory.CreateClient());
        var bobClient = new TestApiClient(factory.CreateClient());

        var alice = await aliceClient.SignInAsync("alice", "Alice");
        var bob = await bobClient.SignInAsync("bob", "Bob");

        aliceClient.Authenticate(alice.AccessToken);
        bobClient.Authenticate(bob.AccessToken);

        var request = await aliceClient.SendFriendRequestAsync(bob.User.Id);
        var acceptResponse = await bobClient.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        acceptResponse.EnsureSuccessStatusCode();

        return (aliceClient, bobClient, alice.User.Id, bob.User.Id);
    }
}
