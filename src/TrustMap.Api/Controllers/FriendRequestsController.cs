using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Common;
using TrustMap.Api.Features.Friends;
using TrustMap.Api.Quotas;
using TrustMap.Api.RateLimiting;
using TrustMap.Api.Services;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("friend-requests")]
public sealed class FriendRequestsController(
    TrustMapDbContext dbContext,
    PrivacyAccessService privacyAccessService,
    NotificationService notificationService,
    IQuotaService quotaService,
    IOptions<QuotaOptions> quotaOptionsAccessor,
    TimeProvider timeProvider,
    ILogger<FriendRequestsController> logger) : ControllerBase
{
    private readonly QuotaOptions _quotaOptions = quotaOptionsAccessor.Value;

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicyNames.FriendRequests)]
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

        IReadOnlyList<UserNotification> notifications;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            await EnsureFriendRequestQuotaAllowsCreateAsync(currentUserId, request.ReceiverUserId, cancellationToken);

            var quotaResult = await quotaService.CheckAndConsumeAsync(
                BuildFriendRequestQuotaConsumptions(currentUserId),
                cancellationToken);
            QuotaRejection.ThrowIfRejected(HttpContext, quotaResult);

            dbContext.FriendRequests.Add(friendRequest);
            notifications = await notificationService.CreateFriendRequestSentAsync(
                currentUserId,
                request.ReceiverUserId,
                friendRequest.Id,
                cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation(
            "Accepted outgoing friend request quota usage for user {UserId}.",
            currentUserId);
        await notificationService.SendPushNotificationsAsync(notifications, CancellationToken.None);
        var accessMap = await privacyAccessService.GetAccessMapAsync(
            currentUserId,
            [sender.Id, receiver.Id],
            cancellationToken);

        return Created(
            $"/friend-requests/{friendRequest.Id}",
            ToDto(friendRequest.Id, friendRequest.Status, friendRequest.CreatedAt, sender, receiver, accessMap));
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

        var accessMap = await privacyAccessService.GetAccessMapAsync(
            currentUserId,
            requests.SelectMany(x => new[] { x.SenderUserId, x.ReceiverUserId }).ToArray(),
            cancellationToken);

        return Ok(requests.Select(x => ToDto(x, accessMap)).ToArray());
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

        var accessMap = await privacyAccessService.GetAccessMapAsync(
            currentUserId,
            requests.SelectMany(x => new[] { x.SenderUserId, x.ReceiverUserId }).ToArray(),
            cancellationToken);

        return Ok(requests.Select(x => ToDto(x, accessMap)).ToArray());
    }

    [HttpPost("{id:guid}/accept")]
    [EnableRateLimiting(RateLimitPolicyNames.FriendRequests)]
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
        await notificationService.MarkFriendRequestNotificationReadAsync(request.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        var otherUser = request.SenderUserId == currentUserId ? request.ReceiverUser : request.SenderUser;
        var accessMap = await privacyAccessService.GetAccessMapAsync(currentUserId, [otherUser.Id], cancellationToken);
        return Ok(new FriendDto(
            otherUser.ToSummaryDto(accessMap[otherUser.Id].CanAccess(otherUser.ProfilePictureVisibility)),
            friendship.CreatedAt));
    }

    [HttpPost("{id:guid}/reject")]
    [EnableRateLimiting(RateLimitPolicyNames.FriendRequests)]
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
        await notificationService.MarkFriendRequestNotificationReadAsync(request.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await ToDtoAsync(currentUserId, request, cancellationToken));
    }

    [HttpPost("{id:guid}/cancel")]
    [EnableRateLimiting(RateLimitPolicyNames.FriendRequests)]
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
        await notificationService.DeleteFriendRequestNotificationAsync(request.Id, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await ToDtoAsync(currentUserId, request, cancellationToken));
    }

    private async Task EnsureFriendRequestQuotaAllowsCreateAsync(
        Guid senderUserId,
        Guid receiverUserId,
        CancellationToken cancellationToken)
    {
        if (!_quotaOptions.Enabled)
        {
            return;
        }

        var pendingOutgoingCount = await dbContext.FriendRequests
            .AsNoTracking()
            .CountAsync(
                x => x.SenderUserId == senderUserId && x.Status == FriendRequestStatus.Pending,
                cancellationToken);

        if (pendingOutgoingCount >= _quotaOptions.FriendRequests.MaxPendingOutgoingPerUser)
        {
            throw ApiException.TooManyRequests(
                "You have too many pending friend requests. Please resolve some before sending more.",
                "friend_request_pending_quota_exceeded");
        }

        if (_quotaOptions.FriendRequests.RejectedTargetCooldownHours > 0)
        {
            var cooldownStartedAt = timeProvider
                .GetUtcNow()
                .AddHours(-_quotaOptions.FriendRequests.RejectedTargetCooldownHours)
                .UtcDateTime;
            var recentlyResolved = await dbContext.FriendRequests
                .AsNoTracking()
                .AnyAsync(
                    x => x.SenderUserId == senderUserId
                        && x.ReceiverUserId == receiverUserId
                        && x.ResolvedAt >= cooldownStartedAt
                        && (x.Status == FriendRequestStatus.Rejected || x.Status == FriendRequestStatus.Cancelled),
                    cancellationToken);

            if (recentlyResolved)
            {
                throw ApiException.TooManyRequests(
                    "Please wait before sending another friend request to this user.",
                    "friend_request_target_cooldown");
            }
        }

        var quotaPrecheck = await quotaService.CheckAsync(
            BuildFriendRequestQuotaConsumptions(senderUserId),
            cancellationToken);
        QuotaRejection.ThrowIfRejected(HttpContext, quotaPrecheck);
    }

    private IReadOnlyCollection<QuotaConsumption> BuildFriendRequestQuotaConsumptions(Guid userId) =>
    [
        new(
            QuotaSubjectKeys.UserScope,
            QuotaSubjectKeys.User(userId),
            QuotaNames.FriendRequestsPerHour,
            QuotaPeriodKind.Hour,
            CountCost: 1,
            ByteCost: 0,
            CountLimit: _quotaOptions.FriendRequests.MaxOutgoingPerUserPerHour,
            ByteLimit: null),
        new(
            QuotaSubjectKeys.UserScope,
            QuotaSubjectKeys.User(userId),
            QuotaNames.FriendRequestsPerDay,
            QuotaPeriodKind.Day,
            CountCost: 1,
            ByteCost: 0,
            CountLimit: _quotaOptions.FriendRequests.MaxOutgoingPerUserPerDay,
            ByteLimit: null)
    ];

    private async Task<FriendRequestDto> ToDtoAsync(
        Guid viewerUserId,
        FriendRequest request,
        CancellationToken cancellationToken)
    {
        var accessMap = await privacyAccessService.GetAccessMapAsync(
            viewerUserId,
            [request.SenderUserId, request.ReceiverUserId],
            cancellationToken);

        return ToDto(request, accessMap);
    }

    private static FriendRequestDto ToDto(
        FriendRequest request,
        IReadOnlyDictionary<Guid, ViewerAccess> accessMap) =>
        ToDto(request.Id, request.Status, request.CreatedAt, request.SenderUser, request.ReceiverUser, accessMap);

    private static FriendRequestDto ToDto(
        Guid requestId,
        FriendRequestStatus status,
        DateTime createdAt,
        Domain.Entities.User sender,
        Domain.Entities.User receiver,
        IReadOnlyDictionary<Guid, ViewerAccess> accessMap) =>
        new(
            requestId,
            status,
            createdAt,
            sender.ToSummaryDto(accessMap.GetValueOrDefault(sender.Id)?.CanAccess(sender.ProfilePictureVisibility) ?? false),
            receiver.ToSummaryDto(accessMap.GetValueOrDefault(receiver.Id)?.CanAccess(receiver.ProfilePictureVisibility) ?? false));
}
