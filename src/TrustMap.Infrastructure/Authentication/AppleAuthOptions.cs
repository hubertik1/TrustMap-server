namespace TrustMap.Infrastructure.Authentication;

public sealed class AppleAuthOptions
{
    public const string SectionName = "AppleAuth";

    public string Issuer { get; set; } = "https://appleid.apple.com";

    public List<string> AllowedAudiences { get; set; } = [];

    public int ClockSkewMinutes { get; set; } = 2;

    public bool TokenManagementEnabled { get; set; }
    public string TeamId { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string PrivateKeyPem { get; set; } = "";
    public string PrivateKeyPath { get; set; } = "";
}
