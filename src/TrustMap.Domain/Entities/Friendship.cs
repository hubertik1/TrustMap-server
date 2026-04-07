namespace TrustMap.Domain.Entities;

public sealed class Friendship
{
    private Friendship()
    {
    }

    public Friendship(Guid id, Guid firstUserId, Guid secondUserId, Guid? sourceRequestId = null)
    {
        if (firstUserId == secondUserId)
        {
            throw new InvalidOperationException("Cannot create friendship to yourself.");
        }

        Id = id;
        var ordered = Order(firstUserId, secondUserId);
        UserAId = ordered.left;
        UserBId = ordered.right;
        PairKey = BuildPairKey(firstUserId, secondUserId);
        SourceRequestId = sourceRequestId;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserAId { get; private set; }

    public Guid UserBId { get; private set; }

    public string PairKey { get; private set; } = null!;

    public Guid? SourceRequestId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public User UserA { get; private set; } = null!;

    public User UserB { get; private set; } = null!;

    public static string BuildPairKey(Guid leftUserId, Guid rightUserId)
    {
        return leftUserId.CompareTo(rightUserId) < 0
            ? $"{leftUserId:N}:{rightUserId:N}"
            : $"{rightUserId:N}:{leftUserId:N}";
    }

    private static (Guid left, Guid right) Order(Guid firstUserId, Guid secondUserId)
    {
        return firstUserId.CompareTo(secondUserId) < 0
            ? (firstUserId, secondUserId)
            : (secondUserId, firstUserId);
    }
}
