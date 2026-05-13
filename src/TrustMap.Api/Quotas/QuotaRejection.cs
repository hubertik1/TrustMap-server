using System.Globalization;
using TrustMap.Api.Common;

namespace TrustMap.Api.Quotas;

public static class QuotaRejection
{
    public static void ThrowIfRejected(HttpContext httpContext, QuotaConsumeResult result)
    {
        if (result.Allowed)
        {
            return;
        }

        if (result.RetryAfterUtc.HasValue)
        {
            var retryAfter = result.RetryAfterUtc.Value - DateTimeOffset.UtcNow;
            if (retryAfter > TimeSpan.Zero)
            {
                httpContext.Response.Headers.RetryAfter =
                    Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
            }
        }

        throw ApiException.TooManyRequests(
            "You have reached this limit. Please try again later.",
            "quota_exceeded");
    }
}
