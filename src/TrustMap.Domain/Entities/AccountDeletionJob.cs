namespace TrustMap.Domain.Entities;

public enum AccountDeletionJobKind { Media, AppleRevocation }

// Independent of user/identity rows so deletion cannot cascade away pending work.
public sealed class AccountDeletionJob
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public AccountDeletionJobKind Kind { get; set; }
    public string Payload { get; set; } = "";
    public string? ClientId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public int Attempts { get; set; }
}
