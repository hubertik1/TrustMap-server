namespace TrustMap.Domain.Entities;

public sealed class QuotaUsage
{
    private QuotaUsage()
    {
    }

    public QuotaUsage(
        Guid id,
        string scope,
        string subjectKey,
        string quotaName,
        DateTimeOffset periodStartUtc,
        DateTimeOffset periodEndUtc)
    {
        Id = id;
        Scope = scope.Trim();
        SubjectKey = subjectKey.Trim();
        QuotaName = quotaName.Trim();
        PeriodStartUtc = periodStartUtc;
        PeriodEndUtc = periodEndUtc;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; private set; }

    public string Scope { get; private set; } = null!;

    public string SubjectKey { get; private set; } = null!;

    public string QuotaName { get; private set; } = null!;

    public DateTimeOffset PeriodStartUtc { get; private set; }

    public DateTimeOffset PeriodEndUtc { get; private set; }

    public int Count { get; private set; }

    public long Bytes { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }
}
