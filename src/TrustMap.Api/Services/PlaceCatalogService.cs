using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Places;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class PlaceCatalogService(TrustMapDbContext dbContext)
{
    public async Task<Place> CreateOrGetAsync(Guid currentUserId, CreatePlaceRequest request, CancellationToken cancellationToken)
    {
        var sourceType = Place.ResolveSourceType(request.Provider, request.ProviderPlaceId);
        var normalizedProvider = sourceType == PlaceSourceType.ProviderVenue
            ? request.Provider!.Trim().ToLowerInvariant()
            : null;
        var normalizedProviderPlaceId = sourceType == PlaceSourceType.ProviderVenue
            ? request.ProviderPlaceId!.Trim()
            : null;

        var existing = await FindByIdentityAsync(
            request,
            sourceType,
            normalizedProvider,
            normalizedProviderPlaceId,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var place = new Place(
            Guid.CreateVersion7(),
            request.Name,
            request.Address,
            request.City,
            request.CountryCode,
            request.Latitude,
            request.Longitude,
            normalizedProvider,
            normalizedProviderPlaceId,
            currentUserId);

        dbContext.Places.Add(place);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return place;
        }
        catch (DbUpdateException)
        {
            var retried = await FindByIdentityAsync(
                request,
                sourceType,
                normalizedProvider,
                normalizedProviderPlaceId,
                cancellationToken);
            if (retried is not null)
            {
                return retried;
            }

            throw;
        }
    }

    public async Task<Place> UpdateCustomDisplayNameAsync(
        Guid currentUserId,
        Guid placeId,
        UpdateCustomPlaceDisplayNameRequest request,
        CancellationToken cancellationToken)
    {
        var place = await dbContext.Places.SingleOrDefaultAsync(x => x.Id == placeId, cancellationToken)
            ?? throw ApiException.NotFound("Place not found.", "place_not_found");

        if (!place.IsCustomPin)
        {
            throw ApiException.Forbidden("Only custom map pins can be renamed.", "place_custom_name_forbidden");
        }

        if (!place.CanEditCustomDisplayName(currentUserId))
        {
            throw ApiException.Forbidden("Only the original creator can rename this custom place.", "place_custom_name_forbidden");
        }

        try
        {
            place.SetCustomDisplayName(currentUserId, request.DisplayName);
        }
        catch (InvalidOperationException exception)
        {
            throw ApiException.BadRequest(exception.Message, "place_custom_name_invalid");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return place;
    }

    private Task<Place?> FindByIdentityAsync(
        CreatePlaceRequest request,
        PlaceSourceType sourceType,
        string? provider,
        string? providerPlaceId,
        CancellationToken cancellationToken)
    {
        var fingerprint = Place.BuildFingerprint(
            request.Name,
            request.Address,
            request.City,
            request.CountryCode,
            request.Latitude,
            request.Longitude);

        return dbContext.Places.FirstOrDefaultAsync(
            x => x.SourceType == sourceType
                 && ((sourceType == PlaceSourceType.ProviderVenue
                        && x.Provider == provider
                        && x.ProviderPlaceId == providerPlaceId)
                     || x.IdentityFingerprint == fingerprint),
            cancellationToken);
    }
}
