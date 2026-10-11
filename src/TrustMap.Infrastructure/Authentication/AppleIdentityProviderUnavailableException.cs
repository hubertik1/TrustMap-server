namespace TrustMap.Infrastructure.Authentication;

public sealed class AppleIdentityProviderUnavailableException()
    : Exception("Apple sign-in configuration is temporarily unavailable.");
