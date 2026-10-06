using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services.Moderation;

public sealed class ReportDeliveryWorker(IServiceScopeFactory scopes, ILogger<ReportDeliveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        // Wait until startup migrations have finished before touching the outbox.
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<ModerationOptions>>().Value;
                if (!options.Enabled) continue;
                await scope.ServiceProvider.GetRequiredService<ReportDelivery>().DeliverPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                // Never log SMTP credentials, tokens, snapshots or provider replies.
                logger.LogError("Report delivery failed ({ExceptionType}).", exception.GetType().Name);
            }
        }
    }
}

public sealed class ReportDelivery(
    TrustMapDbContext dbContext,
    IReportEmailSender sender,
    TimeProvider timeProvider,
    IOptions<ModerationOptions> options,
    ILogger<ReportDelivery> logger)
{
    public async Task DeliverPendingAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var reports = await dbContext.ContentReports.Include(x => x.Photos)
            .Where(x => x.EmailProcessedAtUtc == null && x.ResolvedAtUtc == null && x.NextDeliveryAtUtc <= now)
            .OrderBy(x => x.CreatedAtUtc).Take(10).ToListAsync(cancellationToken);
        foreach (var report in reports)
        {
            // Claim before SMTP to avoid duplicate dispatch from another instance.
            var leaseUntil = now.AddMinutes(5);
            var claimed = await dbContext.ContentReports.Where(x => x.Id == report.Id && x.EmailProcessedAtUtc == null && x.NextDeliveryAtUtc <= now)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.NextDeliveryAtUtc, leaseUntil)
                    .SetProperty(r => r.DeliveryAttempts, r => r.DeliveryAttempts + 1), cancellationToken);
            if (claimed == 0) continue;
            report.NextDeliveryAtUtc = leaseUntil;
            report.DeliveryAttempts++;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(60));
                await sender.SendAsync(report, timeout.Token);
                report.EmailProcessedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                report.NextDeliveryAtUtc = now.AddMinutes(Math.Min(360, Math.Pow(2, Math.Min(report.DeliveryAttempts, 9))));
                logger.LogWarning("Report {ReportId} delivery attempt {Attempt} failed ({ExceptionType}); queued for retry.",
                    report.Id, report.DeliveryAttempts, exception.GetType().Name);
            }
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        // Processed and resolved evidence is retained for a bounded period.
        // Unresolved, unsent reports stay in the outbox during provider outages.
        var cutoff = now.AddDays(-options.Value.EvidenceRetentionDays);
        var expiredIds = await dbContext.ContentReports
            .Where(x => (x.EmailProcessedAtUtc != null && x.EmailProcessedAtUtc < cutoff)
                || (x.ResolvedAtUtc != null && x.ResolvedAtUtc < cutoff))
            .Select(x => x.Id).Take(100).ToListAsync(cancellationToken);
        foreach (var id in expiredIds)
        {
            try
            {
                var directory = Path.Combine(options.Value.EmailDirectory, id.ToString("N"));
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
                await dbContext.ContentReports.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning("Report {ReportId} evidence cleanup failed ({ExceptionType}); queued for retry.", id, exception.GetType().Name);
            }
        }
    }
}
