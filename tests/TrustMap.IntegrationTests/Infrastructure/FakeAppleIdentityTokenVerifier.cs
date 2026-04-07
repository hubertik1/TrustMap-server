using TrustMap.Infrastructure.Authentication;

namespace TrustMap.IntegrationTests.Infrastructure;

internal sealed class FakeAppleIdentityTokenVerifier : IAppleIdentityTokenVerifier
{
    public Task<AppleIdentityPrincipal> VerifyAsync(string identityToken, string? rawNonce, CancellationToken cancellationToken)
    {
        if (!identityToken.StartsWith("apple:", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected fake identity token format.");
        }

        var subject = identityToken["apple:".Length..];
        var email = $"{subject}@example.com";

        return Task.FromResult(new AppleIdentityPrincipal(
            Subject: subject,
            Email: email,
            EmailVerified: true,
            IsPrivateEmail: false));
    }
}
