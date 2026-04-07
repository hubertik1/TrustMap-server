namespace TrustMap.Infrastructure.Authentication;

public interface IAppleIdentityTokenVerifier
{
    Task<AppleIdentityPrincipal> VerifyAsync(string identityToken, string? rawNonce, CancellationToken cancellationToken);
}
