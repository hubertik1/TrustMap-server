namespace TrustMap.Domain.Entities;

public sealed class UserBlock
{
    private UserBlock() { }

    public UserBlock(Guid blockerUserId, Guid blockedUserId)
    {
        if (blockerUserId == blockedUserId)
            throw new InvalidOperationException("You cannot block yourself.");
        BlockerUserId = blockerUserId;
        BlockedUserId = blockedUserId;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid BlockerUserId { get; private set; }
    public Guid BlockedUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}
