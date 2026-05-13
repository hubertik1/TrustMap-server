using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace TrustMap.Api.RateLimiting;

public static class RateLimitPartitionKey
{
    public static string ForClientIp(HttpContext httpContext) =>
        $"ip:{HashValue(GetClientIp(httpContext))}";

    public static string ForAuthenticatedUserOrClientIp(HttpContext httpContext) =>
        TryGetUserId(httpContext.User, out var userId)
            ? $"user:{userId:N}"
            : ForClientIp(httpContext);

    public static string GetPartitionType(HttpContext httpContext) =>
        TryGetUserId(httpContext.User, out _)
            ? "user"
            : "ip";

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        var subject = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(subject, out userId);
    }

    private static string GetClientIp(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string HashValue(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes);
    }
}
