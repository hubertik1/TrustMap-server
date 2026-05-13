namespace TrustMap.Api.Quotas;

public enum QuotaPeriodKind
{
    Hour,
    Day
}

public sealed record QuotaConsumption(
    string Scope,
    string SubjectKey,
    string QuotaName,
    QuotaPeriodKind PeriodKind,
    int CountCost,
    long ByteCost,
    int? CountLimit,
    long? ByteLimit);

public sealed record QuotaConsumeResult(
    bool Allowed,
    string? QuotaName,
    DateTimeOffset? RetryAfterUtc)
{
    public static QuotaConsumeResult Success { get; } = new(true, null, null);

    public static QuotaConsumeResult Rejected(string quotaName, DateTimeOffset retryAfterUtc) =>
        new(false, quotaName, retryAfterUtc);
}

public interface IQuotaService
{
    Task<QuotaConsumeResult> CheckAsync(
        IReadOnlyCollection<QuotaConsumption> consumptions,
        CancellationToken cancellationToken);

    Task<QuotaConsumeResult> CheckAndConsumeAsync(
        IReadOnlyCollection<QuotaConsumption> consumptions,
        CancellationToken cancellationToken);
}
