namespace TrustMap.Domain.Entities;

public sealed class AppleIdentity
{
    private AppleIdentity()
    {
    }

    public AppleIdentity(Guid id, Guid userId, string subject, string? email, bool? emailVerified, bool? isPrivateEmail)
    {
        Id = id;
        UserId = userId;
        Subject = subject.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        EmailVerified = emailVerified;
        IsPrivateEmail = isPrivateEmail;
        CreatedAt = DateTime.UtcNow;
        LastLoginAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Subject { get; private set; } = null!;

    public string? Email { get; private set; }

    public bool? EmailVerified { get; private set; }

    public bool? IsPrivateEmail { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime LastLoginAt { get; private set; }

    public User User { get; private set; } = null!;

    public void UpdateLoginMetadata(string? email, bool? emailVerified, bool? isPrivateEmail)
    {
        Email = string.IsNullOrWhiteSpace(email) ? Email : email.Trim();
        EmailVerified = emailVerified ?? EmailVerified;
        IsPrivateEmail = isPrivateEmail ?? IsPrivateEmail;
        LastLoginAt = DateTime.UtcNow;
    }
}
