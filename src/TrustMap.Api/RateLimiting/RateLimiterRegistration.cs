using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace TrustMap.Api.RateLimiting;

public static class RateLimiterRegistration
{
    public static IServiceCollection AddTrustMapRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<TrustMapRateLimitOptions>()
            .Bind(configuration.GetSection(TrustMapRateLimitOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TrustMapRateLimitOptions>, TrustMapRateLimitOptionsValidator>();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext =>
                {
                    var rateLimitOptions = GetOptions(httpContext);
                    return rateLimitOptions.Enabled
                        ? GetFixedWindowLimiter(
                            RateLimitPartitionKey.ForAuthenticatedUserOrClientIp(httpContext),
                            rateLimitOptions.Global)
                        : RateLimitPartition.GetNoLimiter("disabled");
                });

            options.AddPolicy(RateLimitPolicyNames.Auth, httpContext =>
            {
                var rateLimitOptions = GetOptions(httpContext);
                return rateLimitOptions.Enabled
                    ? GetFixedWindowLimiter(RateLimitPartitionKey.ForClientIp(httpContext), rateLimitOptions.Auth)
                    : RateLimitPartition.GetNoLimiter("disabled");
            });

            options.AddPolicy(RateLimitPolicyNames.RefreshToken, httpContext =>
            {
                var rateLimitOptions = GetOptions(httpContext);
                return rateLimitOptions.Enabled
                    ? GetFixedWindowLimiter(
                        RateLimitPartitionKey.ForAuthenticatedUserOrClientIp(httpContext),
                        rateLimitOptions.RefreshToken)
                    : RateLimitPartition.GetNoLimiter("disabled");
            });

            options.AddPolicy(RateLimitPolicyNames.Upload, httpContext =>
            {
                var rateLimitOptions = GetOptions(httpContext);
                return rateLimitOptions.Enabled
                    ? GetFixedWindowLimiter(
                        RateLimitPartitionKey.ForAuthenticatedUserOrClientIp(httpContext),
                        rateLimitOptions.Upload)
                    : RateLimitPartition.GetNoLimiter("disabled");
            });

            options.AddPolicy(RateLimitPolicyNames.Search, httpContext =>
            {
                var rateLimitOptions = GetOptions(httpContext);
                return rateLimitOptions.Enabled
                    ? GetFixedWindowLimiter(
                        RateLimitPartitionKey.ForAuthenticatedUserOrClientIp(httpContext),
                        rateLimitOptions.Search)
                    : RateLimitPartition.GetNoLimiter("disabled");
            });

            options.AddPolicy(RateLimitPolicyNames.FriendRequests, httpContext =>
            {
                var rateLimitOptions = GetOptions(httpContext);
                return rateLimitOptions.Enabled
                    ? GetFixedWindowLimiter(
                        RateLimitPartitionKey.ForAuthenticatedUserOrClientIp(httpContext),
                        rateLimitOptions.FriendRequests)
                    : RateLimitPartition.GetNoLimiter("disabled");
            });
        });

        return services;
    }

    private static TrustMapRateLimitOptions GetOptions(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<IOptions<TrustMapRateLimitOptions>>().Value;

    private static RateLimitPartition<string> GetFixedWindowLimiter(
        string partitionKey,
        RateLimitPolicyOptions options) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = options.PermitLimit,
                QueueLimit = options.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = TimeSpan.FromSeconds(options.WindowSeconds)
            });

    private static async ValueTask OnRejectedAsync(
        OnRejectedContext context,
        CancellationToken cancellationToken)
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var endpoint = context.HttpContext.GetEndpoint();
        var policyName = endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
            ?? "Global";
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("TrustMap.RateLimiting");

        logger.LogWarning(
            "Rate limit rejected request for policy {PolicyName} on {Path}; partition type {PartitionType}.",
            policyName,
            context.HttpContext.Request.Path.Value,
            RateLimitPartitionKey.GetPartitionType(context.HttpContext));

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too Many Requests",
                Detail = "Too many requests. Please try again later.",
                Extensions =
                {
                    ["errorCode"] = "rate_limit_exceeded",
                    ["traceId"] = context.HttpContext.TraceIdentifier
                }
            },
            cancellationToken);
    }
}
