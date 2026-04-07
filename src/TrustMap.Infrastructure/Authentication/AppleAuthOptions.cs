namespace TrustMap.Infrastructure.Authentication;

public sealed class AppleAuthOptions
{
    public const string SectionName = "AppleAuth";

    public string Issuer { get; set; } = "https://appleid.apple.com";

    public List<string> AllowedAudiences { get; set; } = [];

    public int ClockSkewMinutes { get; set; } = 2;
}
