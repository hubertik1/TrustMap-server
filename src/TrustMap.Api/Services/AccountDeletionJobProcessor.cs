using Microsoft.EntityFrameworkCore;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Api.Services;

public sealed class AccountDeletionJobProcessor(
    TrustMapDbContext dbContext,
    IPhotoStorage storage,
    IAppleTokenClient apple,
    AppleRefreshTokenProtection protection,
    TimeProvider timeProvider,
    ILogger<AccountDeletionJobProcessor> logger)
{
    public async Task ProcessPendingAsync(CancellationToken cancellationToken, IReadOnlyCollection<Guid>? jobIds = null)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var query = dbContext.AccountDeletionJobs.AsNoTracking().Where(x => x.NextAttemptAtUtc <= now);
        if (jobIds is not null) query = query.Where(x => jobIds.Contains(x.Id));
        var jobs = await query.OrderBy(x => x.CreatedAtUtc).Take(50).ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            var claimedAt = timeProvider.GetUtcNow().UtcDateTime;
            var leaseUntil = claimedAt.AddMinutes(2);
            var claimed = await dbContext.AccountDeletionJobs.Where(x => x.Id == job.Id
                    && x.NextAttemptAtUtc == job.NextAttemptAtUtc && x.Attempts == job.Attempts && x.NextAttemptAtUtc <= claimedAt)
                .ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptAtUtc, leaseUntil)
                    .SetProperty(j => j.Attempts, j => j.Attempts + 1), cancellationToken);
            if (claimed == 0) continue;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                switch (job.Kind)
                {
                    case AccountDeletionJobKind.Media:
                        await storage.DeleteAsync(job.Payload, timeout.Token);
                        break;
                    case AccountDeletionJobKind.AppleRevocation:
                        await apple.RevokeAsync(protection.Unprotect(job.Payload), job.ClientId!, timeout.Token);
                        break;
                    default:
                        throw new InvalidOperationException("Unknown account deletion job kind.");
                }
                await dbContext.AccountDeletionJobs.Where(x => x.Id == job.Id
                    && x.Attempts == job.Attempts + 1 && x.NextAttemptAtUtc == leaseUntil).ExecuteDeleteAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                var retryAt = timeProvider.GetUtcNow().UtcDateTime.AddMinutes(Math.Min(360, Math.Pow(2, Math.Min(job.Attempts + 1, 9))));
                await dbContext.AccountDeletionJobs.Where(x => x.Id == job.Id
                        && x.Attempts == job.Attempts + 1 && x.NextAttemptAtUtc == leaseUntil)
                    .ExecuteUpdateAsync(x => x.SetProperty(j => j.NextAttemptAtUtc, retryAt), cancellationToken);
                logger.LogWarning("Account deletion job {JobId} ({Kind}) failed ({ExceptionType}); queued for retry.",
                    job.Id, job.Kind, exception.GetType().Name);
            }
        }
    }
}

public sealed class AccountDeletionWorker(IServiceScopeFactory scopes, ILogger<AccountDeletionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        // Defer until startup migrations have completed.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AccountDeletionJobProcessor>().ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError("Account deletion queue processing failed ({ExceptionType}).", exception.GetType().Name);
            }
        }
    }
}
