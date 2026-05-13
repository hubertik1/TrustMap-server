using System.Net;
using System.Net.Http.Json;
using System.Text;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Friends;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class PrivacyFlowTests
{
    [Fact]
    public async Task PatchMePrivacy_UpdatesPrivacySettings_AndNonPrivateReviews()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "privacy-update-alice", "Alice");
        var placeId = await alice.Client.CreatePlaceAsync("privacy-update");
        var privatePlaceId = await alice.Client.CreatePlaceAsync("privacy-update-private");
        var publicPlaceReviewId = await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 5, "Public place");
        var privatePlaceReviewId = await alice.Client.CreatePlaceReviewAsync(privatePlaceId, VisibilityLevel.Private, 4, "Private place");
        var friendsDishReviewId = await alice.Client.CreateDishReviewAsync(placeId, null, VisibilityLevel.Friends, "Pierogi", 5);
        var privateDishReviewId = await alice.Client.CreateDishReviewAsync(placeId, null, VisibilityLevel.Private, "Secret soup", 3);

        var updated = await alice.Client.UpdateMePrivacyAsync(
            reviewVisibility: VisibilityLevel.FriendsOfFriends,
            friendListVisibility: VisibilityLevel.Friends,
            profileVisibility: VisibilityLevel.FriendsOfFriends,
            profilePictureVisibility: VisibilityLevel.Friends);

        Assert.Equal(VisibilityLevel.FriendsOfFriends, updated.ReviewVisibility);
        Assert.Equal(VisibilityLevel.Friends, updated.FriendListVisibility);
        Assert.Equal(VisibilityLevel.FriendsOfFriends, updated.ProfileVisibility);
        Assert.Equal(VisibilityLevel.Friends, updated.ProfilePictureVisibility);

        var placeReviews = await FetchPlaceReviewsAsync(alice.Client, alice.Session.User.Id);
        var dishReviews = await FetchDishReviewsAsync(alice.Client, alice.Session.User.Id);

        Assert.Equal(VisibilityLevel.FriendsOfFriends, Assert.Single(placeReviews, x => x.Id == publicPlaceReviewId).Visibility);
        Assert.Equal(VisibilityLevel.Private, Assert.Single(placeReviews, x => x.Id == privatePlaceReviewId).Visibility);
        Assert.Equal(VisibilityLevel.FriendsOfFriends, Assert.Single(dishReviews, x => x.Id == friendsDishReviewId).Visibility);
        Assert.Equal(VisibilityLevel.Private, Assert.Single(dishReviews, x => x.Id == privateDishReviewId).Visibility);
    }

    [Fact]
    public async Task PatchMePrivacy_RejectsPrivateValuesWhereNotAllowed()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "privacy-invalid-alice", "Alice");

        var privateReview = await PatchPrivacyRawAsync(alice.Client, VisibilityLevel.Private, VisibilityLevel.Public, VisibilityLevel.Public, VisibilityLevel.Public);
        var privateProfile = await PatchPrivacyRawAsync(alice.Client, VisibilityLevel.Friends, VisibilityLevel.Public, VisibilityLevel.Private, VisibilityLevel.Public);
        var privateAvatar = await PatchPrivacyRawAsync(alice.Client, VisibilityLevel.Friends, VisibilityLevel.Public, VisibilityLevel.Public, VisibilityLevel.Private);

        Assert.Equal(HttpStatusCode.BadRequest, privateReview.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, privateProfile.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, privateAvatar.StatusCode);
    }

    [Fact]
    public async Task PatchMePrivacy_AcceptsStringEnumPayloadFromIos()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "privacy-ios-json-alice", "Alice");
        using var content = new StringContent(
            """
            {
              "reviewVisibility": "FriendsOfFriends",
              "friendListVisibility": "Private",
              "profileVisibility": "Friends",
              "profilePictureVisibility": "FriendsOfFriends"
            }
            """,
            Encoding.UTF8,
            "application/json");

        var response = await alice.Client.HttpClient.PatchAsync("/me/privacy", content);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var updated = await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options);
        Assert.NotNull(updated);
        Assert.Equal(VisibilityLevel.FriendsOfFriends, updated!.ReviewVisibility);
        Assert.Equal(VisibilityLevel.Private, updated.FriendListVisibility);
        Assert.Equal(VisibilityLevel.Friends, updated.ProfileVisibility);
        Assert.Equal(VisibilityLevel.FriendsOfFriends, updated.ProfilePictureVisibility);
    }

    [Fact]
    public async Task PatchMePrivacy_ResponseSerializesPrivacyEnumsAsStrings()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "privacy-response-json-alice", "Alice");
        await alice.Client.UpdateMePrivacyAsync(
            reviewVisibility: VisibilityLevel.FriendsOfFriends,
            friendListVisibility: VisibilityLevel.Private,
            profileVisibility: VisibilityLevel.Friends,
            profilePictureVisibility: VisibilityLevel.FriendsOfFriends);

        var response = await alice.Client.HttpClient.GetAsync("/me");
        var json = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, json);
        Assert.Contains("\"reviewVisibility\":\"FriendsOfFriends\"", json);
        Assert.Contains("\"friendListVisibility\":\"Private\"", json);
        Assert.Contains("\"profileVisibility\":\"Friends\"", json);
        Assert.Contains("\"profilePictureVisibility\":\"FriendsOfFriends\"", json);
        Assert.DoesNotContain("\"reviewVisibility\":4", json);
    }

    [Fact]
    public async Task FriendsOfFriendsReviewVisibility_AllowsMutualFriend_AndBlocksUnrelatedUser()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "fof-review-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "fof-review-bob", "Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "fof-review-charlie", "Charlie");
        var dana = await CreateAuthenticatedClientAsync(factory, "fof-review-dana", "Dana");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);
        await MakeFriendsAsync(bob.Client, charlie.Client, charlie.Session.User.Id);

        var placeId = await alice.Client.CreatePlaceAsync("fof-review");
        await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.FriendsOfFriends, 5, "Mutual can see");

        var charlieReviews = await FetchPlaceReviewsForPlaceAsync(charlie.Client, placeId);
        var danaReviews = await FetchPlaceReviewsForPlaceAsync(dana.Client, placeId);

        Assert.Contains(charlieReviews, x => x.Body == "Mutual can see");
        Assert.DoesNotContain(danaReviews, x => x.Body == "Mutual can see");
    }

    [Fact]
    public async Task GetUser_WithFriendsReviewVisibility_BlocksUnrelatedUserWithoutLeakingCounts()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "review-profile-friends-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "review-profile-friends-bob", "Bob");

        var placeId = await alice.Client.CreatePlaceAsync("review-profile-friends");
        await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Public, 5, "Public place hidden by profile review privacy");
        await alice.Client.CreateDishReviewAsync(placeId, null, VisibilityLevel.Public, "Hidden dish", 5);

        var profile = await FetchProfileAsync(bob.Client, alice.Session.User.Id);

        Assert.False(profile.CanViewReviews);
        Assert.Equal(0, profile.VisiblePlaceReviewCount);
        Assert.Equal(0, profile.VisibleDishReviewCount);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await bob.Client.HttpClient.GetAsync($"/place-reviews?authorUserId={alice.Session.User.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await bob.Client.HttpClient.GetAsync($"/dish-reviews?authorUserId={alice.Session.User.Id}")).StatusCode);
    }

    [Fact]
    public async Task GetUser_WithFriendsReviewVisibility_AllowsDirectFriend()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "review-profile-direct-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "review-profile-direct-bob", "Bob");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);
        var placeId = await alice.Client.CreatePlaceAsync("review-profile-direct");
        await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.Friends, 5, "Friends can see");
        await alice.Client.CreateDishReviewAsync(placeId, null, VisibilityLevel.Friends, "Friends dish", 5);

        var profile = await FetchProfileAsync(bob.Client, alice.Session.User.Id);

        Assert.True(profile.CanViewReviews);
        Assert.Equal(1, profile.VisiblePlaceReviewCount);
        Assert.Equal(1, profile.VisibleDishReviewCount);
    }

    [Fact]
    public async Task GetUser_WithFriendsOfFriendsReviewVisibility_AllowsMutualFriendAndBlocksUnrelatedUser()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "review-profile-fof-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "review-profile-fof-bob", "Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "review-profile-fof-charlie", "Charlie");
        var dana = await CreateAuthenticatedClientAsync(factory, "review-profile-fof-dana", "Dana");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);
        await MakeFriendsAsync(bob.Client, charlie.Client, charlie.Session.User.Id);
        await alice.Client.UpdateMePrivacyAsync(reviewVisibility: VisibilityLevel.FriendsOfFriends);

        var placeId = await alice.Client.CreatePlaceAsync("review-profile-fof");
        await alice.Client.CreatePlaceReviewAsync(placeId, VisibilityLevel.FriendsOfFriends, 5, "Mutual can see in profile");
        await alice.Client.CreateDishReviewAsync(placeId, null, VisibilityLevel.FriendsOfFriends, "Mutual dish", 5);

        var charlieProfile = await FetchProfileAsync(charlie.Client, alice.Session.User.Id);
        var danaProfile = await FetchProfileAsync(dana.Client, alice.Session.User.Id);

        Assert.True(charlieProfile.CanViewReviews);
        Assert.Equal(1, charlieProfile.VisiblePlaceReviewCount);
        Assert.Equal(1, charlieProfile.VisibleDishReviewCount);
        Assert.False(danaProfile.CanViewReviews);
        Assert.Equal(0, danaProfile.VisiblePlaceReviewCount);
        Assert.Equal(0, danaProfile.VisibleDishReviewCount);
    }

    [Fact]
    public async Task GetUser_WhenProfileHidden_ReturnsLimitedDto()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "private-profile-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "private-profile-bob", "Bob");

        await alice.Client.UpdateMeAsync("private-profile-alice", "Alice Private", bio: "Hidden bio");
        await alice.Client.CreatePlaceReviewAsync(await alice.Client.CreatePlaceAsync("private-profile"), VisibilityLevel.Public, 5, "Hidden count");
        await alice.Client.UpdateMePrivacyAsync(
            reviewVisibility: VisibilityLevel.Public,
            profileVisibility: VisibilityLevel.Friends);

        var response = await bob.Client.HttpClient.GetAsync($"/users/{alice.Session.User.Id}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options);

        Assert.NotNull(profile);
        Assert.False(profile!.CanViewProfile);
        Assert.False(profile.CanViewFriends);
        Assert.False(profile.CanViewReviews);
        Assert.Null(profile.Bio);
        Assert.Equal(0, profile.FriendCount);
        Assert.Equal(0, profile.VisiblePlaceReviewCount);
        Assert.Equal(0, profile.VisibleDishReviewCount);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await bob.Client.HttpClient.GetAsync($"/place-reviews?authorUserId={alice.Session.User.Id}")).StatusCode);
    }

    [Fact]
    public async Task ProfilePictureHidden_FiltersProfileSearchAndRequestAvatars()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "hidden-avatar-alice", "Hidden Avatar Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "hidden-avatar-bob", "Bob");

        await alice.Client.UpdateMeAsync("hidden-avatar-alice", "Hidden Avatar Alice");
        await alice.Client.ReplaceAvatarAsync(
            TestImages.CreateJpegBytes(),
            fileName: "alice-private.jpg");
        await alice.Client.UpdateMePrivacyAsync(profilePictureVisibility: VisibilityLevel.Friends);

        var profile = await bob.Client.HttpClient.GetFromJsonAsync<UserProfileDto>(
            $"/users/{alice.Session.User.Id}",
            JsonDefaults.Options);
        var search = await bob.Client.SearchUsersAsync("Hidden Avatar Alice");
        await alice.Client.SendFriendRequestAsync(bob.Session.User.Id);
        var incoming = await bob.Client.HttpClient.GetFromJsonAsync<IReadOnlyList<FriendRequestDto>>(
            "/friend-requests/incoming",
            JsonDefaults.Options);

        Assert.NotNull(profile);
        Assert.False(profile!.CanViewProfilePicture);
        Assert.Null(profile.AvatarUrl);
        Assert.Contains(search, x => x.User.Id == alice.Session.User.Id && x.User.AvatarUrl is null);
        Assert.NotNull(incoming);
        Assert.Contains(incoming!, x => x.Sender.Id == alice.Session.User.Id && x.Sender.AvatarUrl is null);
    }

    [Fact]
    public async Task GetUserFriends_RespectsFriendsAndFriendsOfFriendsVisibility()
    {
        await using var factory = new TrustMapWebApplicationFactory();
        await factory.InitializeAsync();

        var alice = await CreateAuthenticatedClientAsync(factory, "friends-privacy-alice", "Alice");
        var bob = await CreateAuthenticatedClientAsync(factory, "friends-privacy-bob", "Bob");
        var charlie = await CreateAuthenticatedClientAsync(factory, "friends-privacy-charlie", "Charlie");
        var dana = await CreateAuthenticatedClientAsync(factory, "friends-privacy-dana", "Dana");

        await MakeFriendsAsync(alice.Client, bob.Client, bob.Session.User.Id);
        await MakeFriendsAsync(bob.Client, charlie.Client, charlie.Session.User.Id);

        await alice.Client.UpdateMePrivacyAsync(friendListVisibility: VisibilityLevel.Friends);
        Assert.Equal(HttpStatusCode.Forbidden, (await charlie.Client.HttpClient.GetAsync($"/users/{alice.Session.User.Id}/friends")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.Client.HttpClient.GetAsync($"/users/{alice.Session.User.Id}/friends")).StatusCode);

        await alice.Client.UpdateMePrivacyAsync(friendListVisibility: VisibilityLevel.FriendsOfFriends);
        Assert.Equal(HttpStatusCode.OK, (await charlie.Client.HttpClient.GetAsync($"/users/{alice.Session.User.Id}/friends")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await dana.Client.HttpClient.GetAsync($"/users/{alice.Session.User.Id}/friends")).StatusCode);
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

    private static async Task MakeFriendsAsync(TestApiClient requester, TestApiClient receiver, Guid receiverUserId)
    {
        var request = await requester.SendFriendRequestAsync(receiverUserId);
        var response = await receiver.HttpClient.PostAsync($"/friend-requests/{request.Id}/accept", content: null);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<UserProfileDto> FetchProfileAsync(TestApiClient client, Guid userId)
    {
        var response = await client.HttpClient.GetAsync($"/users/{userId}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options))!;
    }

    private static Task<HttpResponseMessage> PatchPrivacyRawAsync(
        TestApiClient client,
        VisibilityLevel reviewVisibility,
        VisibilityLevel friendListVisibility,
        VisibilityLevel profileVisibility,
        VisibilityLevel profilePictureVisibility) =>
        client.HttpClient.PatchAsJsonAsync(
            "/me/privacy",
            new UpdateMePrivacyRequest
            {
                ReviewVisibility = reviewVisibility,
                FriendListVisibility = friendListVisibility,
                ProfileVisibility = profileVisibility,
                ProfilePictureVisibility = profilePictureVisibility
            });

    private static async Task<IReadOnlyList<PlaceReviewDto>> FetchPlaceReviewsAsync(TestApiClient client, Guid authorUserId)
    {
        var response = await client.HttpClient.GetAsync($"/place-reviews?authorUserId={authorUserId}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PlaceReviewDto>>(JsonDefaults.Options))!;
    }

    private static async Task<IReadOnlyList<PlaceReviewDto>> FetchPlaceReviewsForPlaceAsync(TestApiClient client, Guid placeId)
    {
        var response = await client.HttpClient.GetAsync($"/place-reviews?placeId={placeId}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<PlaceReviewDto>>(JsonDefaults.Options))!;
    }

    private static async Task<IReadOnlyList<DishReviewDto>> FetchDishReviewsAsync(TestApiClient client, Guid authorUserId)
    {
        var response = await client.HttpClient.GetAsync($"/dish-reviews?authorUserId={authorUserId}");
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<DishReviewDto>>(JsonDefaults.Options))!;
    }
}
