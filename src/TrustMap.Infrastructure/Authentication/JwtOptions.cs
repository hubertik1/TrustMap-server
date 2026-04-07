namespace TrustMap.Infrastructure.Authentication;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "TrustMap";

    public string Audience { get; set; } = "TrustMap.Mobile";

    public string SigningKey { get; set; } = "change-me-super-long-signing-key-for-local-development";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;
}
