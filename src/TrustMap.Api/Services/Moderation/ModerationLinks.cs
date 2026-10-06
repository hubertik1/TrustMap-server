using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;

namespace TrustMap.Api.Services.Moderation;

public sealed class ModerationLinks(IDataProtectionProvider protection, IOptions<ModerationOptions> options)
{
    public static readonly string[] Actions = ["ignore", "remove", "ban"];

    public string Create(Guid reportId, string action)
    {
        var token = Protector(action).Protect(reportId.ToString("D"), TimeSpan.FromDays(7));
        return $"{options.Value.PublicBaseUrl.TrimEnd('/')}/moderation/{action}?token={Uri.EscapeDataString(token)}";
    }

    public Guid Read(string action, string token)
    {
        if (!options.Value.Enabled || !Actions.Contains(action) || token.Length > 4096)
            throw ApiException.NotFound("This moderation link is unavailable.");
        try
        {
            return Guid.Parse(Protector(action).Unprotect(token));
        }
        catch (Exception exception) when (exception is System.Security.Cryptography.CryptographicException or FormatException or ArgumentException)
        {
            throw ApiException.NotFound("This moderation link is invalid or has expired.");
        }
    }

    private ITimeLimitedDataProtector Protector(string action) =>
        protection.CreateProtector("TrustMap.Moderation.v1", action).ToTimeLimitedDataProtector();
}
