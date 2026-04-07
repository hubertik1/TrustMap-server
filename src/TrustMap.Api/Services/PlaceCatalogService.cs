using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Features.Places;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class PlaceCatalogService(TrustMapDbContext dbContext)
{
    public async Task<Place> CreateOrGetAsync(Guid currentUserId, CreatePlaceRequest request, CancellationToken cancellationToken)
    {
        var existing = await FindByIdentityAsync(request, cancellationToken);
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
            request.Provider,
            request.ProviderPlaceId,
            currentUserId);

        dbContext.Places.Add(place);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return place;
        }
        catch (DbUpdateException)
        {
            var retried = await FindByIdentityAsync(request, cancellationToken);
            if (retried is not null)
            {
                return retried;
            }

            throw;
        }
    }

    private Task<Place?> FindByIdentityAsync(CreatePlaceRequest request, CancellationToken cancellationToken)
    {
        var fingerprint = Place.BuildFingerprint(
            request.Name,
            request.Address,
            request.City,
            request.CountryCode,
            request.Latitude,
            request.Longitude);

        return dbContext.Places.FirstOrDefaultAsync(
            x => (!string.IsNullOrWhiteSpace(request.Provider)
                    && !string.IsNullOrWhiteSpace(request.ProviderPlaceId)
                    && x.Provider == request.Provider.ToLower()
                    && x.ProviderPlaceId == request.ProviderPlaceId)
                 || x.IdentityFingerprint == fingerprint,
            cancellationToken);
    }
}
