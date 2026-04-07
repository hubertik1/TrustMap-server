using System.ComponentModel.DataAnnotations;
using TrustMap.Api.Features.Users;
using TrustMap.Domain.Enums;

namespace TrustMap.Api.Features.Friends;

public sealed class CreateFriendRequestRequest
{
    [Required]
    public Guid ReceiverUserId { get; init; }
}

public sealed record FriendRequestDto(
    Guid Id,
    FriendRequestStatus Status,
    DateTime CreatedAt,
    UserSummaryDto Sender,
    UserSummaryDto Receiver);

public sealed record FriendDto(
    UserSummaryDto User,
    DateTime FriendsSinceUtc);
