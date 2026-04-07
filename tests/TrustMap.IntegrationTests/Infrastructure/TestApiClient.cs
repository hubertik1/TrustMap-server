using System.Net.Http.Headers;
using System.Net.Http.Json;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.Features.Friends;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Features.Reviews;

namespace TrustMap.IntegrationTests.Infrastructure;

internal sealed class TestApiClient(HttpClient httpClient)
{
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
            });

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<AuthSessionDto>(JsonDefaults.Options))!;
    }

    public void Authenticate(string accessToken)
    {
        HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
    }

    public async Task<Guid> CreatePlaceAsync(string suffix, double latitude = 52.2297, double longitude = 21.0122)
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
                Longitude = longitude
            });

        await EnsureSuccessAsync(response);
        var place = (await response.Content.ReadFromJsonAsync<PlaceSummaryDto>(JsonDefaults.Options))!;
        return place.Id;
    }

    public async Task<Guid> CreatePlaceReviewAsync(Guid placeId, Domain.Enums.VisibilityLevel visibility, int rating, string body)
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/place-reviews",
            new CreatePlaceReviewRequest
            {
                PlaceId = placeId,
                Visibility = visibility,
                Rating = rating,
                Body = body,
                Title = "Review"
            });

        await EnsureSuccessAsync(response);
        var review = (await response.Content.ReadFromJsonAsync<PlaceReviewDto>(JsonDefaults.Options))!;
        return review.Id;
    }

    public async Task<Guid> CreateDishReviewAsync(
        Guid placeId,
        Guid? placeReviewId,
        Domain.Enums.VisibilityLevel visibility,
        string dishName,
        int rating)
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/dish-reviews",
            new CreateDishReviewRequest
            {
                PlaceId = placeId,
                PlaceReviewId = placeReviewId,
                Visibility = visibility,
                DishName = dishName,
                Rating = rating,
                Body = $"Body for {dishName}"
            });

        await EnsureSuccessAsync(response);
        var review = (await response.Content.ReadFromJsonAsync<DishReviewDto>(JsonDefaults.Options))!;
        return review.Id;
    }

    public async Task<FriendRequestDto> SendFriendRequestAsync(Guid receiverUserId)
    {
        var response = await HttpClient.PostAsJsonAsync(
            "/friend-requests",
            new CreateFriendRequestRequest
            {
                ReceiverUserId = receiverUserId
            });

        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<FriendRequestDto>(JsonDefaults.Options))!;
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
