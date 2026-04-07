using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Services;
using TrustMap.Domain.Common;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("places")]
public sealed class PlacesController(
    TrustMapDbContext dbContext,
    PlaceCatalogService placeCatalogService,
    SocialGraphService socialGraphService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlaceSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaceSummaryDto>>> GetPlaces(
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 100);

        var places = await dbContext.Places
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(places.Select(x => x.ToSummaryDto()).ToArray());
    }

    [HttpGet("search")]
    [ProducesResponseType<IReadOnlyList<PlaceSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaceSummaryDto>>> SearchPlaces(
        [FromQuery] string q,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<PlaceSummaryDto>());
        }

        take = Math.Clamp(take, 1, 50);
        var normalized = TextNormalizer.NormalizeForLookup(q);

        var places = await dbContext.Places
            .AsNoTracking()
            .Where(x => x.NormalizedName.Contains(normalized) || x.Address.Contains(q))
            .OrderBy(x => x.NormalizedName.StartsWith(normalized) ? 0 : 1)
            .ThenBy(x => x.Name)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(places.Select(x => x.ToSummaryDto()).ToArray());
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PlaceDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaceDetailDto>> GetPlace(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var friendIds = await socialGraphService.GetFriendIdsAsync(currentUserId, cancellationToken);

        var place = await dbContext.Places
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Place not found.", "place_not_found");

        var visiblePlaceReviewsQuery = dbContext.PlaceReviews
            .AsNoTracking()
            .VisibleToViewer(currentUserId, friendIds)
            .Where(x => x.PlaceId == id);

        var visibleDishReviewsQuery = dbContext.DishReviews
            .AsNoTracking()
            .VisibleToViewer(currentUserId, friendIds)
            .Where(x => x.PlaceId == id);

        var visiblePlaceReviewCount = await visiblePlaceReviewsQuery.CountAsync(cancellationToken);
        var visibleDishReviewCount = await visibleDishReviewsQuery.CountAsync(cancellationToken);
        var averagePlaceRating = visiblePlaceReviewCount == 0
            ? (double?)null
            : await visiblePlaceReviewsQuery.AverageAsync(x => (double)x.Rating, cancellationToken);

        var viewerPlaceReviewId = await visiblePlaceReviewsQuery
            .Where(x => x.AuthorUserId == currentUserId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

        var viewerDishReviewCount = await visibleDishReviewsQuery
            .CountAsync(x => x.AuthorUserId == currentUserId, cancellationToken);

        var latestPlaceActivity = await visiblePlaceReviewsQuery
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => (DateTime?)x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var latestDishActivity = await visibleDishReviewsQuery
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => (DateTime?)x.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var latestActivityAt = new[] { latestPlaceActivity, latestDishActivity }
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .DefaultIfEmpty()
            .Max();

        return Ok(new PlaceDetailDto(
            place.Id,
            place.Name,
            place.Address,
            place.City,
            place.CountryCode,
            place.Latitude,
            place.Longitude,
            visiblePlaceReviewCount,
            visibleDishReviewCount,
            averagePlaceRating,
            viewerPlaceReviewId,
            viewerDishReviewCount,
            latestActivityAt == default ? null : latestActivityAt));
    }

    [HttpPost]
    [ProducesResponseType<PlaceSummaryDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<PlaceSummaryDto>> CreatePlace(
        [FromBody] CreatePlaceRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var place = await placeCatalogService.CreateOrGetAsync(currentUserId, request, cancellationToken);
        var dto = place.ToSummaryDto();
        return Created($"/places/{place.Id}", dto);
    }
}
