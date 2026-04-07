using System.Security.Claims;

namespace TrustMap.Api.Common;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetRequiredUserId(this ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub")
            ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(subject, out var userId))
        {
            throw ApiException.Unauthorized("Access token does not contain a valid subject.", "invalid_access_token");
        }

        return userId;
    }
}
