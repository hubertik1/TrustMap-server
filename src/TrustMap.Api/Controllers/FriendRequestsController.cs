using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Friends;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("friend-requests")]
public sealed class FriendRequestsController(
    TrustMapDbContext dbContext) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<FriendRequestDto>(StatusCodes.Status201Created)]
    public async Task<ActionResult<FriendRequestDto>> Create(
        [FromBody] CreateFriendRequestRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        if (request.ReceiverUserId == currentUserId)
        {
            throw ApiException.BadRequest("You cannot send a friend request to yourself.", "friend_request_self");
        }

        var receiver = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.ReceiverUserId, cancellationToken)
            ?? throw ApiException.NotFound("Target user not found.", "user_not_found");

        var pairKey = SocialGraphService.BuildPairKey(currentUserId, request.ReceiverUserId);
        var friendshipExists = await dbContext.Friendships
            .AnyAsync(x => x.PairKey == pairKey, cancellationToken);

        if (friendshipExists)
        {
            throw ApiException.Conflict("Users are already friends.", "friendship_exists");
        }

        var existingPending = await dbContext.FriendRequests
            .Include(x => x.SenderUser)
            .Include(x => x.ReceiverUser)
            .SingleOrDefaultAsync(
                x => x.ActiveRequestKey == FriendRequest.BuildActiveRequestKey(currentUserId, request.ReceiverUserId),
                cancellationToken);

        if (existingPending is not null)
        {
            throw ApiException.Conflict("An active friend request already exists for these users.", "friend_request_exists");
        }

        var sender = await dbContext.Users.SingleAsync(x => x.Id == currentUserId, cancellationToken);
        var friendRequest = new FriendRequest(Guid.CreateVersion7(), currentUserId, request.ReceiverUserId);
        dbContext.FriendRequests.Add(friendRequest);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Created(
            $"/friend-requests/{friendRequest.Id}",
            new FriendRequestDto(friendRequest.Id, friendRequest.Status, friendRequest.CreatedAt, sender.ToSummaryDto(), receiver.ToSummaryDto()));
    }

    [HttpGet("incoming")]
    [ProducesResponseType<IReadOnlyList<FriendRequestDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FriendRequestDto>>> GetIncoming(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var requests = await dbContext.FriendRequests
            .AsNoTracking()
            .Include(x => x.SenderUser)
            .Include(x => x.ReceiverUser)
            .Where(x => x.ReceiverUserId == currentUserId && x.Status == FriendRequestStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(requests.Select(ToDto).ToArray());
    }

    [HttpGet("outgoing")]
    [ProducesResponseType<IReadOnlyList<FriendRequestDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<FriendRequestDto>>> GetOutgoing(CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var requests = await dbContext.FriendRequests
            .AsNoTracking()
            .Include(x => x.SenderUser)
            .Include(x => x.ReceiverUser)
            .Where(x => x.SenderUserId == currentUserId && x.Status == FriendRequestStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(requests.Select(ToDto).ToArray());
    }

    [HttpPost("{id:guid}/accept")]
    [ProducesResponseType<FriendDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FriendDto>> Accept(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var request = await dbContext.FriendRequests
            .Include(x => x.SenderUser)
            .Include(x => x.ReceiverUser)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Friend request not found.", "friend_request_not_found");

        if (request.ReceiverUserId != currentUserId)
        {
            throw ApiException.Forbidden("Only the receiver can accept this friend request.", "friend_request_forbidden");
        }

        if (request.Status != FriendRequestStatus.Pending)
        {
            throw ApiException.Conflict("Friend request is no longer pending.", "friend_request_not_pending");
        }

        var pairKey = SocialGraphService.BuildPairKey(request.SenderUserId, request.ReceiverUserId);
        if (await dbContext.Friendships.AnyAsync(x => x.PairKey == pairKey, cancellationToken))
        {
            throw ApiException.Conflict("Users are already friends.", "friendship_exists");
        }

        request.Accept();
        var friendship = new Friendship(Guid.CreateVersion7(), request.SenderUserId, request.ReceiverUserId, request.Id);
        dbContext.Friendships.Add(friendship);
        await dbContext.SaveChangesAsync(cancellationToken);

        var otherUser = request.SenderUserId == currentUserId ? request.ReceiverUser : request.SenderUser;
        return Ok(new FriendDto(otherUser.ToSummaryDto(), friendship.CreatedAt));
    }

    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<FriendRequestDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FriendRequestDto>> Reject(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var request = await dbContext.FriendRequests
            .Include(x => x.SenderUser)
            .Include(x => x.ReceiverUser)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Friend request not found.", "friend_request_not_found");

        if (request.ReceiverUserId != currentUserId)
        {
            throw ApiException.Forbidden("Only the receiver can reject this friend request.", "friend_request_forbidden");
        }

        request.Reject();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(request));
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<FriendRequestDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FriendRequestDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var request = await dbContext.FriendRequests
            .Include(x => x.SenderUser)
            .Include(x => x.ReceiverUser)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw ApiException.NotFound("Friend request not found.", "friend_request_not_found");

        if (request.SenderUserId != currentUserId)
        {
            throw ApiException.Forbidden("Only the sender can cancel this friend request.", "friend_request_forbidden");
        }

        request.Cancel();
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(request));
    }

    private static FriendRequestDto ToDto(FriendRequest request) =>
        new(
            request.Id,
            request.Status,
            request.CreatedAt,
            request.SenderUser.ToSummaryDto(),
            request.ReceiverUser.ToSummaryDto());
}
