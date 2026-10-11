using System.Diagnostics;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace TrustMap.Api.Services;

public sealed class AppleSignInWarmupService(
    IConfigurationManager<OpenIdConnectConfiguration> configurationManager,
    ILogger<AppleSignInWarmupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // On .NET 10 BackgroundService runs off the startup path. An Apple outage
        // must not delay listening or prevent existing TrustMap sessions from working.
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await configurationManager.GetConfigurationAsync(stoppingToken);
            logger.LogInformation("Apple sign-in configuration warmed in {ElapsedMilliseconds} ms.", stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // The next sign-in can try again. Do not spend the F1 CPU budget on polling.
            logger.LogWarning("Apple sign-in warm-up failed ({ExceptionType}) after {ElapsedMilliseconds} ms; sign-in will retry on demand.",
                exception.GetType().Name, stopwatch.ElapsedMilliseconds);
        }
    }
}
