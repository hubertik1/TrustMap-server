using TrustMap.Domain.Common;
using TrustMap.Domain.Enums;

namespace TrustMap.Domain.Entities;

public sealed class FriendRequest
{
    private FriendRequest()
    {
    }

    public FriendRequest(Guid id, Guid senderUserId, Guid receiverUserId)
    {
        if (senderUserId == receiverUserId)
        {
            throw new InvalidOperationException("Cannot create a friend request to yourself.");
        }

        Id = id;
        SenderUserId = senderUserId;
        ReceiverUserId = receiverUserId;
        Status = FriendRequestStatus.Pending;
        ActiveRequestKey = BuildActiveRequestKey(senderUserId, receiverUserId);
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public Guid Id { get; private set; }

    public Guid SenderUserId { get; private set; }

    public Guid ReceiverUserId { get; private set; }

    public FriendRequestStatus Status { get; private set; }

    public string? ActiveRequestKey { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public User SenderUser { get; private set; } = null!;

    public User ReceiverUser { get; private set; } = null!;

    public void Accept()
    {
        EnsurePending();
        Transition(FriendRequestStatus.Accepted);
    }

    public void Reject()
    {
        EnsurePending();
        Transition(FriendRequestStatus.Rejected);
    }

    public void Cancel()
    {
        EnsurePending();
        Transition(FriendRequestStatus.Cancelled);
    }

    public static string BuildActiveRequestKey(Guid leftUserId, Guid rightUserId)
    {
        return leftUserId.CompareTo(rightUserId) < 0
            ? $"{leftUserId:N}:{rightUserId:N}"
            : $"{rightUserId:N}:{leftUserId:N}";
    }

    private void Transition(FriendRequestStatus status)
    {
        EnumGuard.EnsureDefined(status, nameof(status));
        Status = status;
        ActiveRequestKey = null;
        ResolvedAt = DateTime.UtcNow;
        UpdatedAt = ResolvedAt.Value;
    }

    private void EnsurePending()
    {
        EnumGuard.EnsureDefined(Status, nameof(Status));
        if (Status != FriendRequestStatus.Pending)
        {
            throw new InvalidOperationException("Friend request is no longer pending.");
        }
    }
}
