using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Places;
using TrustMap.Api.Quotas;
using TrustMap.Api.RateLimiting;
using TrustMap.Api.Services;
using TrustMap.Domain.Common;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("places")]
public sealed class PlacesController(
    TrustMapDbContext dbContext,
    PlaceCatalogService placeCatalogService,
    PrivacyAccessService privacyAccessService,
    IQuotaService quotaService,
    IOptions<QuotaOptions> quotaOptionsAccessor) : ControllerBase
{
    private readonly QuotaOptions _quotaOptions = quotaOptionsAccessor.Value;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlaceSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaceSummaryDto>>> GetPlaces(
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);
        take = Math.Clamp(take, 1, 100);

        var places = await dbContext.Places
            .AsNoTracking()
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .OrderBy(x => x.SourceType == PlaceSourceType.CustomPin
                ? x.NormalizedCustomDisplayName ?? x.NormalizedAddress
                : x.NormalizedName)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(places.Select(x => x.ToSummaryDto(currentUserId)).ToArray());
    }

    [HttpGet("search")]
    [EnableRateLimiting(RateLimitPolicyNames.Search)]
    [ProducesResponseType<IReadOnlyList<PlaceSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PlaceSummaryDto>>> SearchPlaces(
        [FromQuery] string q,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var normalized = TextNormalizer.NormalizeForLookup(q);
        if (normalized.Length < _quotaOptions.Search.MinimumQueryLength)
        {
            return Ok(Array.Empty<PlaceSummaryDto>());
        }

        take = Math.Clamp(take, 1, 50);
        var currentUserId = User.GetRequiredUserId();
        var quotaResult = await quotaService.CheckAndConsumeAsync(
            BuildSearchQuotaConsumptions(currentUserId),
            cancellationToken);
        QuotaRejection.ThrowIfRejected(HttpContext, quotaResult);

        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);

        var places = await dbContext.Places
            .AsNoTracking()
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .Where(x =>
                x.NormalizedName.Contains(normalized)
                || x.NormalizedAddress.Contains(normalized)
                || (x.NormalizedCustomDisplayName != null && x.NormalizedCustomDisplayName.Contains(normalized)))
            .OrderBy(x => x.NormalizedCustomDisplayName != null && x.NormalizedCustomDisplayName.StartsWith(normalized)
                ? 0
                : x.SourceType == PlaceSourceType.CustomPin && x.NormalizedAddress.StartsWith(normalized)
                    ? 1
                    : x.NormalizedName.StartsWith(normalized)
                        ? 2
                        : 3)
            .ThenBy(x => x.SourceType == PlaceSourceType.CustomPin
                ? x.CustomDisplayName ?? x.Address
                : x.Name)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(places.Select(x => x.ToSummaryDto(currentUserId)).ToArray());
    }

    private IReadOnlyCollection<QuotaConsumption> BuildSearchQuotaConsumptions(Guid userId) =>
        _quotaOptions.Search.MaxSearchesPerUserPerDay <= 0
            ? []
            :
            [
                new(
                    QuotaSubjectKeys.UserScope,
                    QuotaSubjectKeys.User(userId),
                    QuotaNames.SearchesPerDay,
                    QuotaPeriodKind.Day,
                    CountCost: 1,
                    ByteCost: 0,
                    CountLimit: _quotaOptions.Search.MaxSearchesPerUserPerDay,
                    ByteLimit: null)
            ];

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PlaceDetailDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaceDetailDto>> GetPlace(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);

        var place = await dbContext.Places
            .AsNoTracking()
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Place not found.", "place_not_found");

        var visiblePlaceReviewsQuery = dbContext.PlaceReviews
            .AsNoTracking()
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .Where(x => x.PlaceId == id);

        var visibleDishReviewsQuery = dbContext.DishReviews
            .AsNoTracking()
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
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

        var visibleContributorIds = await visiblePlaceReviewsQuery
            .Select(x => x.AuthorUserId)
            .Concat(visibleDishReviewsQuery.Select(x => x.AuthorUserId))
            .Distinct()
            .ToListAsync(cancellationToken);

        var categoryNames = visibleContributorIds.Count == 0
            ? []
            : await dbContext.PlaceCategoryAssignments
                .AsNoTracking()
                .Where(x =>
                    x.PlaceId == id
                    && visibleContributorIds.Contains(x.AssignedByUserId)
                    && !x.Category.IsDeleted)
                .Select(x => x.Category.Name)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync(cancellationToken);

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
            place.DisplayName,
            place.CustomDisplayName,
            place.SourceType,
            place.CanEditCustomDisplayName(currentUserId),
            place.Address,
            place.City,
            place.CountryCode,
            place.Latitude,
            place.Longitude,
            place.CreatedByUserId,
            categoryNames,
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
        var dto = place.ToSummaryDto(currentUserId);
        return Created($"/places/{place.Id}", dto);
    }

    [HttpPatch("{id:guid}/custom-display-name")]
    [ProducesResponseType<PlaceSummaryDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaceSummaryDto>> UpdateCustomDisplayName(
        Guid id,
        [FromBody] UpdateCustomPlaceDisplayNameRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var socialAccess = await privacyAccessService.GetViewerSocialAccessAsync(currentUserId, cancellationToken);
        var canViewPlace = await dbContext.Places
            .AsNoTracking()
            .VisibleToViewer(currentUserId, socialAccess.FriendIds, socialAccess.FriendOfFriendIds)
            .AnyAsync(x => x.Id == id, cancellationToken);
        if (!canViewPlace)
        {
            throw ApiException.NotFound("Place not found.", "place_not_found");
        }

        var place = await placeCatalogService.UpdateCustomDisplayNameAsync(currentUserId, id, request, cancellationToken);
        return Ok(place.ToSummaryDto(currentUserId));
    }
}
