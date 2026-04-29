using System.Text.RegularExpressions;
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
    private const string LatinSmallLetterLWithStroke = "\u0142";
    private const string LatinSmallLetterL = "l";
    private static readonly Regex HandleHashSpacingRegex = new(@"\s*#\s*", RegexOptions.Compiled);

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
        var normalizedHandleQuery = NormalizeHandleSearchQuery(q);
        var normalizedDisplayQuery = NormalizeDisplaySearchQuery(q);
        var displayTokens = normalizedDisplayQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var hasHandleQuery = !string.IsNullOrWhiteSpace(normalizedHandleQuery);
        var hasDisplayQuery = !string.IsNullOrWhiteSpace(normalizedDisplayQuery);

        if (!hasHandleQuery && displayTokens.Length == 0)
        {
            return Ok(Array.Empty<UserSearchResultDto>());
        }

        var currentUserId = User.GetRequiredUserId();
        var baseQuery = dbContext.Users.AsNoTracking();
        IQueryable<Domain.Entities.User>? matches = null;

        if (hasHandleQuery)
        {
            matches = baseQuery.Where(x => x.NormalizedHandle.Contains(normalizedHandleQuery));
        }

        if (displayTokens.Length > 0)
        {
            var displayMatches = baseQuery;
            foreach (var token in displayTokens)
            {
                var captured = token;
                displayMatches = displayMatches.Where(x =>
                    x.NormalizedDisplayName.Contains(captured)
                    || x.NormalizedDisplayName.Replace(LatinSmallLetterLWithStroke, LatinSmallLetterL).Contains(captured));
            }

            matches = matches is null ? displayMatches : matches.Union(displayMatches);
        }

        var users = await matches!
            .OrderBy(x =>
                hasHandleQuery && x.NormalizedHandle == normalizedHandleQuery ? 0 :
                hasHandleQuery && x.NormalizedHandle.StartsWith(normalizedHandleQuery) ? 1 :
                hasDisplayQuery
                    && (x.NormalizedDisplayName == normalizedDisplayQuery
                        || x.NormalizedDisplayName.Replace(LatinSmallLetterLWithStroke, LatinSmallLetterL) == normalizedDisplayQuery) ? 2 :
                hasDisplayQuery
                    && (x.NormalizedDisplayName.StartsWith(normalizedDisplayQuery)
                        || x.NormalizedDisplayName.Replace(LatinSmallLetterLWithStroke, LatinSmallLetterL).StartsWith(normalizedDisplayQuery)) ? 3 :
                hasHandleQuery && x.NormalizedHandle.Contains(normalizedHandleQuery) ? 4 :
                5)
            .ThenBy(x => x.DisplayName)
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

    private static string NormalizeHandleSearchQuery(string query)
    {
        var normalized = TextNormalizer.NormalizeForLookup(query);
        if (normalized.StartsWith("@", StringComparison.Ordinal))
        {
            normalized = normalized[1..];
        }

        normalized = normalized.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        normalized = HandleHashSpacingRegex.Replace(normalized, "#");
        return TextNormalizer.NormalizeHandle(normalized);
    }

    private static string NormalizeDisplaySearchQuery(string query) =>
        TextNormalizer.NormalizeForLookup(query)
            .Replace(LatinSmallLetterLWithStroke, LatinSmallLetterL);
}
