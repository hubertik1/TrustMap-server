namespace TrustMap.Infrastructure.Authentication;

public sealed record AccessTokenEnvelope(
    string Token,
    DateTime ExpiresAtUtc);
