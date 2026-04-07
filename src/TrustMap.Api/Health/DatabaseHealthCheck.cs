using Microsoft.Extensions.Diagnostics.HealthChecks;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Health;

public sealed class DatabaseHealthCheck(TrustMapDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? HealthCheckResult.Healthy("Database connection succeeded.")
            : HealthCheckResult.Unhealthy("Database connection failed.");
    }
}
