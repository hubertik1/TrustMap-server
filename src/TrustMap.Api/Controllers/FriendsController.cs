using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Friends;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("friends")]
public sealed class FriendsController(TrustMapDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FriendDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FriendDto>>> Get(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var friendships = await dbContext.Friendships
            .AsNoTracking()
            .Where(x => x.UserAId == currentUserId || x.UserBId == currentUserId)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var friendIds = friendships.Select(x => x.UserAId == currentUserId ? x.UserBId : x.UserAId).Distinct().ToList();
        var users = await dbContext.Users
            .AsNoTracking()
            .Where(x => friendIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var result = friendships
            .Select(x =>
            {
                var friendId = x.UserAId == currentUserId ? x.UserBId : x.UserAId;
                return new FriendDto(users[friendId].ToSummaryDto(), x.CreatedAt);
            })
            .ToArray();

        return Ok(result);
    }

    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid userId, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var pairKey = Domain.Entities.Friendship.BuildPairKey(currentUserId, userId);
        var friendship = await dbContext.Friendships.SingleOrDefaultAsync(x => x.PairKey == pairKey, cancellationToken)
            ?? throw ApiException.NotFound("Friendship not found.", "friendship_not_found");

        dbContext.Friendships.Remove(friendship);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
