using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Users;
using TrustMap.Api.Services;
using TrustMap.Domain.Common;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("users")]
public sealed class UsersController(
    TrustMapDbContext dbContext,
    SocialGraphService socialGraphService,
    ProfileQueryService profileQueryService) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public Task<UserProfileDto> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        return profileQueryService.GetProfileAsync(currentUserId, id, cancellationToken);
    }

    [HttpGet("search")]
    [ProducesResponseType<IReadOnlyList<UserSearchResultDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserSearchResultDto>>> SearchUsers(
        [FromQuery] string q,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(Array.Empty<UserSearchResultDto>());
        }

        take = Math.Clamp(take, 1, 50);
        var normalized = TextNormalizer.NormalizeForLookup(q);
        var currentUserId = User.GetRequiredUserId();

        var users = await dbContext.Users
            .AsNoTracking()
            .Where(x => x.NormalizedHandle.Contains(normalized) || x.NormalizedDisplayName.Contains(normalized))
            .OrderBy(x => x.NormalizedHandle.StartsWith(normalized) ? 0 : 1)
            .ThenBy(x => x.Handle)
            .Take(take)
            .ToListAsync(cancellationToken);

        var relationshipStatuses = await socialGraphService.GetRelationshipStatusesAsync(
            currentUserId,
            users.Select(x => x.Id).ToArray(),
            cancellationToken);

        return Ok(users
            .Select(x => new UserSearchResultDto(
                x.ToSummaryDto(),
                relationshipStatuses.GetValueOrDefault(x.Id, RelationshipStatus.None)))
            .ToArray());
    }
}
