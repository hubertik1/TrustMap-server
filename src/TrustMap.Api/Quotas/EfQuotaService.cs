using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Quotas;

public sealed class EfQuotaService(
    TrustMapDbContext dbContext,
    IOptions<QuotaOptions> quotaOptionsAccessor,
    TimeProvider timeProvider,
    ILogger<EfQuotaService> logger) : IQuotaService
{
    private readonly QuotaOptions _quotaOptions = quotaOptionsAccessor.Value;

    public Task<QuotaConsumeResult> CheckAsync(
        IReadOnlyCollection<QuotaConsumption> consumptions,
        CancellationToken cancellationToken) =>
        CheckOrConsumeAsync(consumptions, consume: false, cancellationToken);

    public Task<QuotaConsumeResult> CheckAndConsumeAsync(
        IReadOnlyCollection<QuotaConsumption> consumptions,
        CancellationToken cancellationToken) =>
        CheckOrConsumeAsync(consumptions, consume: true, cancellationToken);

    private async Task<QuotaConsumeResult> CheckOrConsumeAsync(
        IReadOnlyCollection<QuotaConsumption> consumptions,
        bool consume,
        CancellationToken cancellationToken)
    {
        if (!_quotaOptions.Enabled || consumptions.Count == 0)
        {
            return QuotaConsumeResult.Success;
        }

        var effectiveConsumptions = consumptions
            .Where(x => x.CountLimit.HasValue || x.ByteLimit.HasValue)
            .ToArray();
        if (effectiveConsumptions.Length == 0)
        {
            return QuotaConsumeResult.Success;
        }

        ValidateConsumptions(effectiveConsumptions);
        var now = timeProvider.GetUtcNow();
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        try
        {
            foreach (var consumption in effectiveConsumptions)
            {
                var period = BuildPeriod(now, consumption.PeriodKind);
                await EnsureUsageRowAsync(consumption, period, cancellationToken);
            }

            foreach (var consumption in effectiveConsumptions)
            {
                var period = BuildPeriod(now, consumption.PeriodKind);
                var allowed = consume
                    ? await TryConsumeAsync(consumption, period, now, cancellationToken)
                    : await IsAllowedAsync(consumption, period, cancellationToken);

                if (!allowed)
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }

                    logger.LogWarning(
                        "Quota {QuotaName} rejected for scope {Scope}.",
                        consumption.QuotaName,
                        consumption.Scope);
                    return QuotaConsumeResult.Rejected(consumption.QuotaName, period.EndUtc);
                }
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            if (consume)
            {
                foreach (var consumption in effectiveConsumptions)
                {
                    logger.LogInformation(
                        "Consumed quota {QuotaName} for scope {Scope}.",
                        consumption.QuotaName,
                        consumption.Scope);
                }
            }

            return QuotaConsumeResult.Success;
        }
        catch (Exception exception)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            logger.LogError(exception, "Quota store operation failed.");
            throw;
        }
    }

    private async Task EnsureUsageRowAsync(
        QuotaConsumption consumption,
        QuotaPeriod period,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.QuotaUsages
            .AsNoTracking()
            .AnyAsync(x =>
                x.Scope == consumption.Scope
                && x.SubjectKey == consumption.SubjectKey
                && x.QuotaName == consumption.QuotaName
                && x.PeriodStartUtc == period.StartUtc,
                cancellationToken);

        if (exists)
        {
            return;
        }

        dbContext.QuotaUsages.Add(new QuotaUsage(
            Guid.CreateVersion7(),
            consumption.Scope,
            consumption.SubjectKey,
            consumption.QuotaName,
            period.StartUtc,
            period.EndUtc));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            foreach (var entry in dbContext.ChangeTracker
                         .Entries<QuotaUsage>()
                         .Where(x => x.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }
        }
    }

    private async Task<bool> IsAllowedAsync(
        QuotaConsumption consumption,
        QuotaPeriod period,
        CancellationToken cancellationToken)
    {
        var usage = await dbContext.QuotaUsages
            .AsNoTracking()
            .SingleAsync(x =>
                x.Scope == consumption.Scope
                && x.SubjectKey == consumption.SubjectKey
                && x.QuotaName == consumption.QuotaName
                && x.PeriodStartUtc == period.StartUtc,
                cancellationToken);

        return IsWithinLimits(usage.Count, usage.Bytes, consumption);
    }

    private async Task<bool> TryConsumeAsync(
        QuotaConsumption consumption,
        QuotaPeriod period,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var updatedRows = await dbContext.QuotaUsages
            .Where(x =>
                x.Scope == consumption.Scope
                && x.SubjectKey == consumption.SubjectKey
                && x.QuotaName == consumption.QuotaName
                && x.PeriodStartUtc == period.StartUtc
                && (!consumption.CountLimit.HasValue || x.Count + consumption.CountCost <= consumption.CountLimit.Value)
                && (!consumption.ByteLimit.HasValue || x.Bytes + consumption.ByteCost <= consumption.ByteLimit.Value))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Count, x => x.Count + consumption.CountCost)
                .SetProperty(x => x.Bytes, x => x.Bytes + consumption.ByteCost)
                .SetProperty(x => x.UpdatedAtUtc, now),
                cancellationToken);

        return updatedRows == 1;
    }

    private static bool IsWithinLimits(
        int currentCount,
        long currentBytes,
        QuotaConsumption consumption) =>
        (!consumption.CountLimit.HasValue || currentCount + consumption.CountCost <= consumption.CountLimit.Value)
        && (!consumption.ByteLimit.HasValue || currentBytes + consumption.ByteCost <= consumption.ByteLimit.Value);

    private static void ValidateConsumptions(IEnumerable<QuotaConsumption> consumptions)
    {
        foreach (var consumption in consumptions)
        {
            if (string.IsNullOrWhiteSpace(consumption.Scope))
            {
                throw new InvalidOperationException("Quota scope is required.");
            }

            if (string.IsNullOrWhiteSpace(consumption.SubjectKey))
            {
                throw new InvalidOperationException("Quota subject key is required.");
            }

            if (string.IsNullOrWhiteSpace(consumption.QuotaName))
            {
                throw new InvalidOperationException("Quota name is required.");
            }

            if (consumption.CountCost < 0 || consumption.ByteCost < 0)
            {
                throw new InvalidOperationException("Quota costs cannot be negative.");
            }

            if (consumption.CountCost == 0 && consumption.ByteCost == 0)
            {
                throw new InvalidOperationException("Quota consumption must have a positive count or byte cost.");
            }
        }
    }

    private static QuotaPeriod BuildPeriod(DateTimeOffset now, QuotaPeriodKind periodKind)
    {
        var utc = now.ToUniversalTime();
        var start = periodKind switch
        {
            QuotaPeriodKind.Hour => new DateTimeOffset(
                utc.Year,
                utc.Month,
                utc.Day,
                utc.Hour,
                0,
                0,
                TimeSpan.Zero),
            QuotaPeriodKind.Day => new DateTimeOffset(
                utc.Year,
                utc.Month,
                utc.Day,
                0,
                0,
                0,
                TimeSpan.Zero),
            _ => throw new InvalidOperationException("Unsupported quota period kind.")
        };

        var end = periodKind switch
        {
            QuotaPeriodKind.Hour => start.AddHours(1),
            QuotaPeriodKind.Day => start.AddDays(1),
            _ => throw new InvalidOperationException("Unsupported quota period kind.")
        };

        return new QuotaPeriod(start, end);
    }

    private sealed record QuotaPeriod(DateTimeOffset StartUtc, DateTimeOffset EndUtc);
}
