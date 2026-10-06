using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("me/blocks")]
public sealed class BlocksController(TrustMapDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        // The only client endpoint allowed to list names of blocked users. Do not
        // return avatar URLs: media remains protected by the normal access policy.
        var blocked = await (from block in dbContext.UserBlocks
                             join user in dbContext.Users.IgnoreQueryFilters() on block.BlockedUserId equals user.Id
                             where block.BlockerUserId == userId && user.Status != UserAccountStatus.Deleted
                             orderby block.CreatedAtUtc descending
                             select new { userId = user.Id, user.Handle, user.DisplayName, blockedAtUtc = block.CreatedAtUtc })
            .ToListAsync(cancellationToken);
        return Ok(blocked);
    }

    [HttpPut("{blockedUserId:guid}")]
    public async Task<IActionResult> Block(Guid blockedUserId, CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        if (userId == blockedUserId)
            throw ApiException.BadRequest("You cannot block yourself.", "block_self");

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (await dbContext.UserBlocks.AnyAsync(x => x.BlockerUserId == userId && x.BlockedUserId == blockedUserId, cancellationToken))
            return NoContent();
        // A reverse block must not prevent someone from adding their own block.
        if (!await dbContext.Users.IgnoreQueryFilters().AnyAsync(x => x.Id == blockedUserId && x.Status == UserAccountStatus.Active, cancellationToken))
            throw ApiException.NotFound("User not found.", "user_not_found");

        dbContext.UserBlocks.Add(new UserBlock(userId, blockedUserId));
        await dbContext.Friendships.IgnoreQueryFilters()
            .Where(x => (x.UserAId == userId && x.UserBId == blockedUserId) || (x.UserBId == userId && x.UserAId == blockedUserId))
            .ExecuteDeleteAsync(cancellationToken);
        var pending = await dbContext.FriendRequests.IgnoreQueryFilters()
            .Where(x => x.Status == FriendRequestStatus.Pending
                && ((x.SenderUserId == userId && x.ReceiverUserId == blockedUserId)
                    || (x.ReceiverUserId == userId && x.SenderUserId == blockedUserId)))
            .ToListAsync(cancellationToken);
        foreach (var request in pending) request.Cancel();
        await dbContext.Notifications.IgnoreQueryFilters()
            .Where(x => (x.ActorUserId == userId && x.RecipientUserId == blockedUserId)
                || (x.RecipientUserId == userId && x.ActorUserId == blockedUserId))
            .ExecuteDeleteAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{blockedUserId:guid}")]
    public async Task<IActionResult> Unblock(Guid blockedUserId, CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        await dbContext.UserBlocks.Where(x => x.BlockerUserId == userId && x.BlockedUserId == blockedUserId)
            .ExecuteDeleteAsync(cancellationToken);
        return NoContent();
    }
}
