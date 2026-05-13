using System.Net.Http.Headers;
using System.Net.Http.Json;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Categories;
using TrustMap.Api.Features.Friends;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Reviews;
using TrustMap.Api.Services;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;

namespace TrustMap.IntegrationTests.Infrastructure;

internal sealed class TestApiClient(HttpClient httpClient)
{
    public static readonly Guid DefaultCategoryId = CategoryAssignmentService.RestaurantsCategoryId;
    public static readonly Guid AlternateDefaultCategoryId = Guid.Parse("11111111-1111-1111-1111-111111111104");

    public HttpClient HttpClient { get; } = httpClient;

    public async Task<AuthSessionDto> SignInAsync(string subject, string displayName)
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/auth/apple",
            new AppleAuthRequest
            {
                IdentityToken = $"apple:{subject}",
                DisplayName = displayName,
                PreferredHandle = subject
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonDefaults.Options))!;
    }

    public void Authenticate(string accessToken)
    {
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<Guid> CreatePlaceAsync(string suffix, double latitude = 52.2297, double longitude = 21.0122)
    {
        var place = await CreatePlaceSummaryAsync(suffix, latitude, longitude);
        return place.Id;
    }

    public async Task<PlaceSummaryDto> CreatePlaceSummaryAsync(
        string suffix,
        double latitude = 52.2297,
        double longitude = 21.0122,
        string? provider = null,
        string? providerPlaceId = null)
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/places",
            new CreatePlaceRequest
            {
                Name = $"Place {suffix}",
                Address = $"Address {suffix}",
                City = "Warsaw",
                CountryCode = "PL",
                Latitude = latitude,
                Longitude = longitude,
                Provider = provider,
                ProviderPlaceId = providerPlaceId
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<PlaceSummaryDto>(JsonDefaults.Options))!;
    }

    public async Task<PlaceSummaryDto> UpdateCustomPlaceDisplayNameAsync(Guid placeId, string displayName)
    {
        var response = await HttpClient.PatchAsJsonAsync(
            $"/places/{placeId}/custom-display-name",
            new UpdateCustomPlaceDisplayNameRequest
            {
                DisplayName = displayName
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<PlaceSummaryDto>(JsonDefaults.Options))!;
    }

    public async Task<Guid> CreatePlaceReviewAsync(
        Guid placeId,
        Domain.Enums.VisibilityLevel visibility,
        int rating,
        string body,
        Guid? categoryId = null)
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/place-reviews",
            new CreatePlaceReviewRequest
            {
                PlaceId = placeId,
                CategoryId = categoryId ?? DefaultCategoryId,
                Visibility = visibility,
                Rating = rating,
                Body = body,
                Title = "Review"
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        var review = (await response.Content.ReadFromJsonAsync<PlaceReviewDto>(JsonDefaults.Options))!;
        return review.Id;
    }

    public async Task<Guid> CreateDishReviewAsync(
        Guid placeId,
        Guid? placeReviewId,
        Domain.Enums.VisibilityLevel visibility,
        string dishName,
        int rating,
        Guid? categoryId = null,
        string? body = null)
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/dish-reviews",
            new CreateDishReviewRequest
            {
                PlaceId = placeId,
                CategoryId = categoryId ?? DefaultCategoryId,
                PlaceReviewId = placeReviewId,
                Visibility = visibility,
                DishName = dishName,
                Rating = rating,
                Body = body ?? $"Body for {dishName}"
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        var review = (await response.Content.ReadFromJsonAsync<DishReviewDto>(JsonDefaults.Options))!;
        return review.Id;
    }

    public async Task<CategoryDto> CreateCategoryAsync(string name, string? iconName = null)
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/categories",
            new CreateCategoryRequest
            {
                Name = name,
                IconName = iconName
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(JsonDefaults.Options))!;
    }

    public async Task<CategoryDto> AdoptCategoryAsync(Guid categoryId)
    {
        var response = await HttpClient.PostAsync($"/categories/{categoryId}/adopt", content: null);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(JsonDefaults.Options))!;
    }

    public async Task<CategoryDto> HideCategoryAsync(Guid categoryId)
    {
        var response = await HttpClient.PostAsync($"/categories/{categoryId}/hide", content: null);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(JsonDefaults.Options))!;
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid categoryId, string name, string? iconName = null)
    {
        var response = await HttpClient.PatchAsJsonAsync(
            $"/categories/{categoryId}",
            new UpdateCategoryRequest
            {
                Name = name,
                IconName = iconName
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CategoryDto>(JsonDefaults.Options))!;
    }

    public async Task<IReadOnlyList<CategoryDto>> FetchMyCategoriesAsync()
    {
        var response = await HttpClient.GetAsync("/categories/my");
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<CategoryDto>>(JsonDefaults.Options))!;
    }

    public async Task<IReadOnlyList<CategoryDto>> FetchFriendCategoriesAsync()
    {
        var response = await HttpClient.GetAsync("/categories/friends");
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<CategoryDto>>(JsonDefaults.Options))!;
    }

    public async Task<IReadOnlyList<CategoryDto>> FetchHiddenCategoriesAsync()
    {
        var response = await HttpClient.GetAsync("/categories/hidden");
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<CategoryDto>>(JsonDefaults.Options))!;
    }

    public async Task<FriendRequestDto> SendFriendRequestAsync(Guid receiverUserId)
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/friend-requests",
            new CreateFriendRequestRequest
            {
                ReceiverUserId = receiverUserId
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<FriendRequestDto>(JsonDefaults.Options))!;
    }

    public async Task<UserProfileDto> UpdateMeAsync(
        string handle,
        string displayName,
        string? bio = null)
    {
        var response = await HttpClient.PatchAsJsonAsync(
            "/me",
            new UpdateMeRequest
            {
                Handle = handle,
                DisplayName = displayName,
                Bio = bio
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options))!;
    }

    public async Task<UserProfileDto> ReplaceAvatarAsync(
        byte[] imageBytes,
        string fileName = "avatar.jpg",
        string contentType = "image/jpeg")
    {
        using var multipart = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(imageBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        multipart.Add(fileContent, "avatar", fileName);

        var response = await HttpClient.PutAsync("/me/avatar", multipart);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options))!;
    }

    public async Task<UserProfileDto> RemoveAvatarAsync()
    {
        var response = await HttpClient.DeleteAsync("/me/avatar");
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options))!;
    }

    public async Task<UserProfileDto> UpdateMePrivacyAsync(
        VisibilityLevel reviewVisibility = VisibilityLevel.Friends,
        VisibilityLevel friendListVisibility = VisibilityLevel.Friends,
        VisibilityLevel profileVisibility = VisibilityLevel.Public,
        VisibilityLevel profilePictureVisibility = VisibilityLevel.Public)
    {
        var response = await HttpClient.PatchAsJsonAsync(
            "/me/privacy",
            new UpdateMePrivacyRequest
            {
                ReviewVisibility = reviewVisibility,
                FriendListVisibility = friendListVisibility,
                ProfileVisibility = profileVisibility,
                ProfilePictureVisibility = profilePictureVisibility
            },
            JsonDefaults.Options);

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<UserProfileDto>(JsonDefaults.Options))!;
    }

    public async Task<IReadOnlyList<UserFriendDto>> FetchUserFriendsAsync(Guid userId)
    {
        var response = await HttpClient.GetAsync($"/users/{userId}/friends");
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<UserFriendDto>>(JsonDefaults.Options))!;
    }

    public async Task<IReadOnlyList<UserSearchResultDto>> SearchUsersAsync(string query, int take = 20)
    {
        var response = await HttpClient.GetAsync(
            $"/users/search?q={Uri.EscapeDataString(query)}&take={take}");

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<IReadOnlyList<UserSearchResultDto>>(JsonDefaults.Options))!;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var content = await response.Content.ReadAsStringAsync();
        throw new HttpRequestException($"Response status code {(int)response.StatusCode} ({response.StatusCode}) with body: {content}");
    }
}
