using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Map;
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
    SocialGraphService socialGraphService) : ControllerBase
{
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
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);
        var visibleUserIds = friendIds.Append(currentUserId).ToHashSet();
        take = Math.Clamp(take, 1, 500);

        var placeReviewQuery = dbContext.PlaceReviews
            .AsNoTracking()
            .VisibleInSocialScope(currentUserId, friendIds);

        var dishReviewQuery = dbContext.DishReviews
            .AsNoTracking()
            .VisibleInSocialScope(currentUserId, friendIds);

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
                x.Place.CreatedByUserId))
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
                x.Place.CreatedByUserId))
            .ToListAsync(cancellationToken);

        var categoryAssignmentsQuery = dbContext.PlaceCategoryAssignments
            .AsNoTracking()
            .Where(x => visibleUserIds.Contains(x.AssignedByUserId));

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
                    x.Value.LatestActivity);
            })
            .OrderByDescending(x => x.LatestActivityAtUtc)
            .Take(take)
            .ToArray();

        return Ok(result);
    }
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
    Guid? CreatedByUserId);

file sealed record MapCategorySeed(
    Guid PlaceId,
    Guid AssignedByUserId,
    string CategoryName);

file sealed record MapAggregation(
    MapActivitySeed Place,
    int PlaceReviewCount,
    double? AverageRating,
    DateTime LatestActivity,
    HashSet<Guid> Contributors);
