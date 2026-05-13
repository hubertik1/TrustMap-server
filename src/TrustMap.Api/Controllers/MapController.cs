using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Map;
using TrustMap.Api.Features.Users;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("map")]
public sealed class MapController(
    TrustMapDbContext dbContext,
    PrivacyAccessService privacyAccessService,
    CategoryMembershipService categoryMembershipService) : ControllerBase
{
    private const int MaxSearchTermsPerPlace = 40;
    private const int MaxSearchTermLength = 280;

    [HttpGet("places")]
    [ProducesResponseType<IReadOnlyList<MapPlaceDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MapPlaceDto>>> GetPlaces(
        [FromQuery] double? north,
        [FromQuery] double? south,
        [FromQuery] double? east,
        [FromQuery] double? west,
        [FromQuery] Guid? categoryId,
        [FromQuery] int take = 250,
        CancellationToken cancellationToken = default)
    {
        var hasBounds = north.HasValue || south.HasValue || east.HasValue || west.HasValue;
        if (hasBounds && (!north.HasValue || !south.HasValue || !east.HasValue || !west.HasValue))
        {
            throw ApiException.BadRequest("Provide either all map bounds or none of them.", "map_invalid_bounds");
        }

        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);
        var friendIds = socialAccess.FriendIds;
        var visibleUserIds = friendIds.Append(currentUserId).ToHashSet();
        take = Math.Clamp(take, 1, 500);

        if (categoryId.HasValue)
        {
            _ = await categoryMembershipService.GetSelectableCategoryAsync(currentUserId, categoryId.Value, cancellationToken);
        }

        var placeReviewQuery = dbContext.PlaceReviews
            .AsNoTracking()
            .VisibleInSocialScope(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds);

        var dishReviewQuery = dbContext.DishReviews
            .AsNoTracking()
            .VisibleInSocialScope(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds);

        if (hasBounds)
        {
            placeReviewQuery = placeReviewQuery.Where(x =>
                x.Place.Latitude <= north!.Value
                && x.Place.Latitude >= south!.Value
                && x.Place.Longitude <= east!.Value
                && x.Place.Longitude >= west!.Value);

            dishReviewQuery = dishReviewQuery.Where(x =>
                x.Place.Latitude <= north!.Value
                && x.Place.Latitude >= south!.Value
                && x.Place.Longitude <= east!.Value
                && x.Place.Longitude >= west!.Value);
        }

        var placeReviews = await placeReviewQuery
            .Select(x => new MapActivitySeed(
                x.PlaceId,
                x.AuthorUserId,
                x.Rating,
                x.UpdatedAt,
                x.Place.Name,
                x.Place.CustomDisplayName,
                x.Place.SourceType,
                x.Place.Address,
                x.Place.City,
                x.Place.CountryCode,
                x.Place.Latitude,
                x.Place.Longitude,
                x.Place.CreatedByUserId,
                x.AuthorUser.Handle,
                x.AuthorUser.DisplayName,
                x.AuthorUser.AvatarUrl,
                x.AuthorUser.ProfilePictureVisibility))
            .ToListAsync(cancellationToken);

        var dishReviews = await dishReviewQuery
            .Select(x => new MapActivitySeed(
                x.PlaceId,
                x.AuthorUserId,
                null,
                x.UpdatedAt,
                x.Place.Name,
                x.Place.CustomDisplayName,
                x.Place.SourceType,
                x.Place.Address,
                x.Place.City,
                x.Place.CountryCode,
                x.Place.Latitude,
                x.Place.Longitude,
                x.Place.CreatedByUserId,
                x.AuthorUser.Handle,
                x.AuthorUser.DisplayName,
                x.AuthorUser.AvatarUrl,
                x.AuthorUser.ProfilePictureVisibility))
            .ToListAsync(cancellationToken);

        var categoryAssignmentsQuery = dbContext.PlaceCategoryAssignments
            .AsNoTracking()
            .Where(x => visibleUserIds.Contains(x.AssignedByUserId) && !x.Category.IsDeleted);

        if (hasBounds)
        {
            categoryAssignmentsQuery = categoryAssignmentsQuery.Where(x =>
                x.Place.Latitude <= north!.Value
                && x.Place.Latitude >= south!.Value
                && x.Place.Longitude <= east!.Value
                && x.Place.Longitude >= west!.Value);
        }

        if (categoryId.HasValue)
        {
            categoryAssignmentsQuery = categoryAssignmentsQuery.Where(x => x.CategoryId == categoryId.Value);
        }

        var categoryAssignments = await categoryAssignmentsQuery
            .Select(x => new MapCategorySeed(x.PlaceId, x.AssignedByUserId, x.Category.Name))
            .ToListAsync(cancellationToken);

        var grouped = placeReviews
            .GroupBy(x => x.PlaceId)
            .ToDictionary(
                x => x.Key,
                x => new MapAggregation(
                    Place: x.First(),
                    PlaceReviewCount: x.Count(),
                    AverageRating: x.Average(item => item.Rating!.Value),
                    LatestActivity: x.Max(item => item.UpdatedAt),
                    Contributors: x.Select(item => item.AuthorUserId).ToHashSet()));

        foreach (var dishGroup in dishReviews.GroupBy(x => x.PlaceId))
        {
            if (!grouped.TryGetValue(dishGroup.Key, out var existing))
            {
                var first = dishGroup.First();
                grouped[dishGroup.Key] = new MapAggregation(
                    Place: first,
                    PlaceReviewCount: 0,
                    AverageRating: null,
                    LatestActivity: dishGroup.Max(item => item.UpdatedAt),
                    Contributors: dishGroup.Select(item => item.AuthorUserId).ToHashSet());

                continue;
            }

            var contributors = existing.Contributors;
            contributors.UnionWith(dishGroup.Select(item => item.AuthorUserId));

            grouped[dishGroup.Key] = new MapAggregation(
                existing.Place,
                existing.PlaceReviewCount,
                existing.AverageRating,
                new[] { existing.LatestActivity, dishGroup.Max(item => item.UpdatedAt) }.Max(),
                contributors);
        }

        var dishCounts = dishReviews
            .GroupBy(x => x.PlaceId)
            .ToDictionary(x => x.Key, x => x.Count());

        var contributorAccess = await privacyAccessService.GetAccessMapAsync(
            currentUserId,
            placeReviews.Select(x => x.AuthorUserId).Concat(dishReviews.Select(x => x.AuthorUserId)).ToArray(),
            cancellationToken);

        var recentContributorsByPlace = placeReviews
            .Concat(dishReviews)
            .GroupBy(x => x.PlaceId)
            .ToDictionary(
                x => x.Key,
                x => x.GroupBy(item => item.AuthorUserId)
                    .Select(authorGroup => authorGroup
                        .OrderByDescending(item => item.UpdatedAt)
                        .First())
                    .OrderByDescending(item => item.UpdatedAt)
                    .Take(3)
                    .Select(item => new UserSummaryDto(
                        item.AuthorUserId,
                        item.AuthorHandle,
                        item.AuthorDisplayName,
                        contributorAccess.GetValueOrDefault(item.AuthorUserId)?.CanAccess(item.AuthorProfilePictureVisibility) == true
                            ? item.AuthorAvatarUrl
                            : null))
                    .ToArray());

        var categoryNamesByPlace = categoryAssignments
            .Where(x => grouped.TryGetValue(x.PlaceId, out var aggregation) && aggregation.Contributors.Contains(x.AssignedByUserId))
            .GroupBy(x => x.PlaceId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(item => item.CategoryName)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item)
                    .ToArray());

        var result = grouped
            .Where(x => !categoryId.HasValue || categoryNamesByPlace.ContainsKey(x.Key))
            .Select(x =>
            {
                var place = x.Value.Place;
                return new MapPlaceDto(
                    x.Key,
                    place.Name,
                    Place.ResolveDisplayName(place.SourceType, place.Name, place.Address, place.CustomDisplayName),
                    place.CustomDisplayName,
                    place.SourceType,
                    place.Address,
                    place.City,
                    place.CountryCode,
                    place.Latitude,
                    place.Longitude,
                    place.CreatedByUserId,
                    categoryNamesByPlace.GetValueOrDefault(x.Key, []),
                    x.Value.PlaceReviewCount,
                    dishCounts.GetValueOrDefault(x.Key, 0),
                    x.Value.AverageRating,
                    x.Value.Contributors.Count,
                    x.Value.LatestActivity)
                {
                    RecentContributors = recentContributorsByPlace.GetValueOrDefault(x.Key, [])
                };
            })
            .OrderByDescending(x => x.LatestActivityAtUtc)
            .Take(take)
            .ToArray();

        var resultPlaceIds = result.Select(x => x.PlaceId).ToList();
        if (resultPlaceIds.Count > 0)
        {
            var reviewedPlaceIdsByCurrentUser = await dbContext.PlaceReviews
                .AsNoTracking()
                .Where(x => resultPlaceIds.Contains(x.PlaceId))
                .Where(x => x.AuthorUserId == currentUserId)
                .Select(x => x.PlaceId)
                .Distinct()
                .ToListAsync(cancellationToken);
            var reviewedPlaceIdSet = reviewedPlaceIdsByCurrentUser.ToHashSet();

            var placeSearchSeeds = await placeReviewQuery
                .Where(x => resultPlaceIds.Contains(x.PlaceId))
                .Select(x => new MapSearchTextSeed(
                    x.PlaceId,
                    x.Title,
                    x.Body,
                    null,
                    null))
                .ToListAsync(cancellationToken);

            var dishSearchSeeds = await dishReviewQuery
                .Where(x => resultPlaceIds.Contains(x.PlaceId))
                .Select(x => new MapSearchTextSeed(
                    x.PlaceId,
                    null,
                    null,
                    x.DishName,
                    x.Body))
                .ToListAsync(cancellationToken);

            var searchTextByPlace = BuildSearchTextByPlace(placeSearchSeeds.Concat(dishSearchSeeds));
            result = result
                .Select(x => x with
                {
                    IsReviewedByCurrentUser = reviewedPlaceIdSet.Contains(x.PlaceId),
                    SearchText = searchTextByPlace.GetValueOrDefault(x.PlaceId, "")
                })
                .ToArray();
        }

        return Ok(result);
    }

    [HttpGet("pins")]
    [ProducesResponseType<IReadOnlyList<MapPinDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MapPinDto>>> GetPins(
        [FromQuery] double? north,
        [FromQuery] double? south,
        [FromQuery] double? east,
        [FromQuery] double? west,
        CancellationToken cancellationToken = default)
    {
        var bounds = RequireBounds(north, south, east, west);
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);

        var pins = await BuildMapPinsAsync(
            currentUserId,
            socialAccess,
            bounds,
            placeId: null,
            cancellationToken);

        return Ok(pins
            .OrderByDescending(x => x.LatestActivityAtUtc)
            .ToArray());
    }

    [HttpGet("pins/{placeId:guid}")]
    [ProducesResponseType<MapPinDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MapPinDto?>> GetPin(
        Guid placeId,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);

        var pins = await BuildMapPinsAsync(
            currentUserId,
            socialAccess,
            bounds: null,
            placeId,
            cancellationToken);

        var pin = pins.SingleOrDefault();
        return pin is null
            ? new JsonResult(null)
            : Ok(pin);
    }

    private async Task<IReadOnlyList<MapPinDto>> BuildMapPinsAsync(
        Guid currentUserId,
        ViewerSocialAccess socialAccess,
        MapQueryBounds? bounds,
        Guid? placeId,
        CancellationToken cancellationToken)
    {
        var placeReviewQuery = dbContext.PlaceReviews
            .AsNoTracking()
            .VisibleInSocialScope(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds);

        var dishReviewQuery = dbContext.DishReviews
            .AsNoTracking()
            .VisibleInSocialScope(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds);

        if (bounds is not null)
        {
            placeReviewQuery = placeReviewQuery.Where(x =>
                x.Place.Latitude <= bounds.North
                && x.Place.Latitude >= bounds.South
                && x.Place.Longitude <= bounds.East
                && x.Place.Longitude >= bounds.West);

            dishReviewQuery = dishReviewQuery.Where(x =>
                x.Place.Latitude <= bounds.North
                && x.Place.Latitude >= bounds.South
                && x.Place.Longitude <= bounds.East
                && x.Place.Longitude >= bounds.West);
        }

        if (placeId.HasValue)
        {
            placeReviewQuery = placeReviewQuery.Where(x => x.PlaceId == placeId.Value);
            dishReviewQuery = dishReviewQuery.Where(x => x.PlaceId == placeId.Value);
        }

        var placeActivities = await placeReviewQuery
            .Select(x => new MapPinActivitySeed(
                x.PlaceId,
                x.AuthorUserId,
                x.Rating,
                x.UpdatedAt,
                x.Place.Name,
                x.Place.CustomDisplayName,
                x.Place.SourceType,
                x.Place.Address,
                x.Place.Latitude,
                x.Place.Longitude))
            .ToListAsync(cancellationToken);

        var dishActivities = await dishReviewQuery
            .Select(x => new MapPinActivitySeed(
                x.PlaceId,
                x.AuthorUserId,
                x.Rating,
                x.UpdatedAt,
                x.Place.Name,
                x.Place.CustomDisplayName,
                x.Place.SourceType,
                x.Place.Address,
                x.Place.Latitude,
                x.Place.Longitude))
            .ToListAsync(cancellationToken);

        var activities = placeActivities.Concat(dishActivities).ToArray();
        if (activities.Length == 0)
        {
            return [];
        }

        var averagePlaceRatingsByPlace = placeActivities
            .GroupBy(x => x.PlaceId)
            .ToDictionary(x => x.Key, x => (double?)x.Average(item => item.Rating));

        var grouped = activities
            .GroupBy(x => x.PlaceId)
            .ToDictionary(
                x => x.Key,
                x => new MapPinAggregation(
                    Place.ResolveDisplayName(x.First().SourceType, x.First().Name, x.First().Address, x.First().CustomDisplayName),
                    x.First().Latitude,
                    x.First().Longitude,
                    averagePlaceRatingsByPlace.GetValueOrDefault(x.Key),
                    x.Count(),
                    x.Select(item => item.AuthorUserId).ToHashSet(),
                    x.Any(item => item.AuthorUserId == currentUserId),
                    x.Max(item => item.UpdatedAt)));

        var placeIds = grouped.Keys.ToList();
        var visibleUserIds = socialAccess.FriendIds.Append(currentUserId).ToHashSet();

        var categoryAssignments = await dbContext.PlaceCategoryAssignments
            .AsNoTracking()
            .Where(x =>
                placeIds.Contains(x.PlaceId)
                && visibleUserIds.Contains(x.AssignedByUserId)
                && !x.Category.IsDeleted)
            .Select(x => new MapPinCategorySeed(x.PlaceId, x.AssignedByUserId, x.CategoryId))
            .ToListAsync(cancellationToken);

        var categoryIdsByPlace = categoryAssignments
            .Where(x => grouped.TryGetValue(x.PlaceId, out var aggregation) && aggregation.Contributors.Contains(x.AssignedByUserId))
            .GroupBy(x => x.PlaceId)
            .ToDictionary(
                x => x.Key,
                x => x.Select(item => item.CategoryId)
                    .Distinct()
                    .OrderBy(item => item)
                    .ToArray());

        return grouped
            .Select(x => new MapPinDto(
                x.Key,
                x.Value.DisplayName,
                x.Value.Latitude,
                x.Value.Longitude,
                x.Value.AverageRating,
                x.Value.ReviewCount,
                x.Value.Contributors.Count,
                categoryIdsByPlace.GetValueOrDefault(x.Key, []),
                x.Value.IsReviewedByCurrentUser,
                x.Value.LatestActivity))
            .ToArray();
    }

    private static MapQueryBounds RequireBounds(double? north, double? south, double? east, double? west)
    {
        if (!north.HasValue || !south.HasValue || !east.HasValue || !west.HasValue)
        {
            throw ApiException.BadRequest("Map pins require north, south, east, and west bounds.", "map_invalid_bounds");
        }

        return new MapQueryBounds(north.Value, south.Value, east.Value, west.Value);
    }

    private static IReadOnlyDictionary<Guid, string> BuildSearchTextByPlace(IEnumerable<MapSearchTextSeed> seeds)
    {
        return seeds
            .GroupBy(x => x.PlaceId)
            .ToDictionary(
                x => x.Key,
                x =>
                {
                    var terms = new List<string>();
                    var seenTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    foreach (var term in x.SelectMany(EnumerateSearchTerms))
                    {
                        if (terms.Count >= MaxSearchTermsPerPlace)
                        {
                            break;
                        }

                        var normalizedTerm = NormalizeSearchTerm(term);
                        if (normalizedTerm is null || !seenTerms.Add(normalizedTerm))
                        {
                            continue;
                        }

                        terms.Add(normalizedTerm);
                    }

                    return string.Join(" ", terms);
                });
    }

    private static IEnumerable<string?> EnumerateSearchTerms(MapSearchTextSeed seed)
    {
        yield return seed.PlaceReviewTitle;
        yield return seed.PlaceReviewBody;
        yield return seed.DishName;
        yield return seed.DishReviewBody;
    }

    private static string? NormalizeSearchTerm(string? term)
    {
        var trimmed = term?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= MaxSearchTermLength
            ? trimmed
            : trimmed[..MaxSearchTermLength];
    }

    private sealed record MapSearchTextSeed(
        Guid PlaceId,
        string? PlaceReviewTitle,
        string? PlaceReviewBody,
        string? DishName,
        string? DishReviewBody);
}

file sealed record MapActivitySeed(
    Guid PlaceId,
    Guid AuthorUserId,
    double? Rating,
    DateTime UpdatedAt,
    string Name,
    string? CustomDisplayName,
    PlaceSourceType SourceType,
    string Address,
    string? City,
    string? CountryCode,
    double Latitude,
    double Longitude,
    Guid? CreatedByUserId,
    string AuthorHandle,
    string AuthorDisplayName,
    string? AuthorAvatarUrl,
    VisibilityLevel AuthorProfilePictureVisibility);

file sealed record MapCategorySeed(
    Guid PlaceId,
    Guid AssignedByUserId,
    string CategoryName);

file sealed record MapPinActivitySeed(
    Guid PlaceId,
    Guid AuthorUserId,
    double Rating,
    DateTime UpdatedAt,
    string Name,
    string? CustomDisplayName,
    PlaceSourceType SourceType,
    string Address,
    double Latitude,
    double Longitude);

file sealed record MapPinCategorySeed(
    Guid PlaceId,
    Guid AssignedByUserId,
    Guid CategoryId);

file sealed record MapAggregation(
    MapActivitySeed Place,
    int PlaceReviewCount,
    double? AverageRating,
    DateTime LatestActivity,
    HashSet<Guid> Contributors);

file sealed record MapPinAggregation(
    string DisplayName,
    double Latitude,
    double Longitude,
    double? AverageRating,
    int ReviewCount,
    HashSet<Guid> Contributors,
    bool IsReviewedByCurrentUser,
    DateTime LatestActivity);

sealed record MapQueryBounds(
    double North,
    double South,
    double East,
    double West);
