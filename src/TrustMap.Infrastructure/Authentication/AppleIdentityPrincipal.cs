namespace TrustMap.Infrastructure.Authentication;

public sealed record AppleIdentityPrincipal(
    string Subject,
    string? Email,
    bool? EmailVerified,
    bool? IsPrivateEmail);
