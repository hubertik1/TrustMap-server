namespace TrustMap.Domain.Entities;

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public RefreshToken(
        Guid id,
        Guid userId,
        string tokenHash,
        DateTime expiresAt,
        string? userAgent,
        string? createdByIpAddress)
    {
        Id = id;
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent.Trim();
        CreatedByIpAddress = string.IsNullOrWhiteSpace(createdByIpAddress) ? null : createdByIpAddress.Trim();
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public DateTime ExpiresAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CreatedByIpAddress { get; private set; }

    public string? RevokedByIpAddress { get; private set; }

    public User User { get; private set; } = null!;

    public bool IsExpired(DateTime nowUtc) => ExpiresAt <= nowUtc;

    public bool IsRevoked => RevokedAt.HasValue;

    public bool IsActive(DateTime nowUtc) => !IsRevoked && !IsExpired(nowUtc);

    public void Revoke(Guid? replacedByTokenId, string? revokedByIpAddress)
    {
        if (RevokedAt.HasValue)
        {
            return;
        }

        RevokedAt = DateTime.UtcNow;
        ReplacedByTokenId = replacedByTokenId;
        RevokedByIpAddress = string.IsNullOrWhiteSpace(revokedByIpAddress) ? null : revokedByIpAddress.Trim();
    }
}
