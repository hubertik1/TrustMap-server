using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Friends;
using TrustMap.Api.Features.Users;
using TrustMap.Api.Quotas;
using TrustMap.Api.RateLimiting;
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
    ProfileQueryService profileQueryService,
    PrivacyAccessService privacyAccessService,
    IQuotaService quotaService,
    IOptions<QuotaOptions> quotaOptionsAccessor) : ControllerBase
{
    private const string LatinSmallLetterLWithStroke = "\u0142";
    private const string LatinSmallLetterL = "l";
    private static readonly Regex HandleHashSpacingRegex = new(@"\s*#\s*", RegexOptions.Compiled);
    private readonly QuotaOptions _quotaOptions = quotaOptionsAccessor.Value;

    [HttpGet("{id:guid}")]
    [ProducesResponseType<UserProfileDto>(StatusCodes.Status200OK)]
    public Task<UserProfileDto> GetUser(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        return profileQueryService.GetProfileAsync(currentUserId, id, cancellationToken);
    }

    [HttpGet("{id:guid}/friends")]
    [ProducesResponseType<IReadOnlyList<UserFriendDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserFriendDto>>> GetUserFriends(
        Guid id,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var targetUser = await dbContext.Users
            .AsNoTracking()
            .ActiveOnly()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("User not found.", "user_not_found");

        var targetAccess = await privacyAccessService.GetAccessMapAsync(currentUserId, [id], cancellationToken);
        var access = targetAccess[id];
        if (!access.CanAccess(targetUser.ProfileVisibility) || !access.CanAccess(targetUser.FriendListVisibility))
        {
            throw ApiException.Forbidden("This user keeps their friend list private.", "friend_list_private");
        }

        var friendships = await dbContext.Friendships
            .AsNoTracking()
            .Where(x => x.UserAId == id || x.UserBId == id)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var friendIds = friendships
            .Select(x => x.UserAId == id ? x.UserBId : x.UserAId)
            .Distinct()
            .ToList();

        var users = await dbContext.Users
            .AsNoTracking()
            .ActiveOnly()
            .Where(x => friendIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var relationshipStatuses = await socialGraphService.GetRelationshipStatusesAsync(
            currentUserId,
            friendIds,
            cancellationToken);

        var friendAccess = await privacyAccessService.GetAccessMapAsync(currentUserId, friendIds, cancellationToken);

        var result = friendships
            .Where(x => users.ContainsKey(x.UserAId == id ? x.UserBId : x.UserAId))
            .Select(x =>
            {
                var friendId = x.UserAId == id ? x.UserBId : x.UserAId;
                var user = users[friendId];
                return new UserFriendDto(
                    user.ToSummaryDto(friendAccess.GetValueOrDefault(friendId)?.CanAccess(user.ProfilePictureVisibility) ?? false),
                    x.CreatedAt,
                    relationshipStatuses.GetValueOrDefault(friendId, RelationshipStatus.None));
            })
            .ToArray();

        return Ok(result);
    }

    [HttpGet("search")]
    [EnableRateLimiting(RateLimitPolicyNames.Search)]
    [ProducesResponseType<IReadOnlyList<UserSearchResultDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<UserSearchResultDto>>> SearchUsers(
        [FromQuery] string q,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        var normalizedRawQuery = TextNormalizer.NormalizeForLookup(q ?? string.Empty);
        if (normalizedRawQuery.Length < _quotaOptions.Search.MinimumQueryLength)
        {
            return Ok(Array.Empty<UserSearchResultDto>());
        }

        take = Math.Clamp(take, 1, 50);
        var query = q ?? string.Empty;
        var normalizedHandleQuery = NormalizeHandleSearchQuery(query);
        var normalizedDisplayQuery = NormalizeDisplaySearchQuery(query);
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
        var quotaResult = await quotaService.CheckAndConsumeAsync(
            BuildSearchQuotaConsumptions(currentUserId),
            cancellationToken);
        QuotaRejection.ThrowIfRejected(HttpContext, quotaResult);

        var baseQuery = dbContext.Users.AsNoTracking().ActiveOnly();
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
        var accessMap = await privacyAccessService.GetAccessMapAsync(
            currentUserId,
            users.Select(x => x.Id).ToArray(),
            cancellationToken);

        return Ok(users
            .Select(x => new UserSearchResultDto(
                x.ToSummaryDto(accessMap.GetValueOrDefault(x.Id)?.CanAccess(x.ProfilePictureVisibility) ?? false),
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
}
