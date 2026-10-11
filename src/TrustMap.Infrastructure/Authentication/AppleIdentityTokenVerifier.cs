using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace TrustMap.Infrastructure.Authentication;

public sealed class AppleIdentityTokenVerifier : IAppleIdentityTokenVerifier
{
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly AppleAuthOptions _options;
    private readonly JwtSecurityTokenHandler _tokenHandler = new()
    {
        MapInboundClaims = false
    };

    public AppleIdentityTokenVerifier(
        IOptions<AppleAuthOptions> options,
        IConfigurationManager<OpenIdConnectConfiguration> configurationManager)
    {
        _options = options.Value;
        _configurationManager = configurationManager;
    }

    public async Task<AppleIdentityPrincipal> VerifyAsync(
        string identityToken,
        string? rawNonce,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identityToken))
        {
            throw new SecurityTokenValidationException("Identity token is required.");
        }

        if (!_tokenHandler.CanReadToken(identityToken))
        {
            throw new SecurityTokenValidationException("Identity token format is invalid.");
        }

        if (_options.AllowedAudiences.Count == 0)
        {
            throw new InvalidOperationException("AppleAuth:AllowedAudiences must contain at least one bundle or service identifier.");
        }

        OpenIdConnectConfiguration openIdConfig;
        try
        {
            openIdConfig = await _configurationManager.GetConfigurationAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // A provider/network failure does not mean the user's identity is invalid.
            throw new AppleIdentityProviderUnavailableException();
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = openIdConfig.SigningKeys,
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudiences = _options.AllowedAudiences,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(_options.ClockSkewMinutes)
        };

        var principal = _tokenHandler.ValidateToken(identityToken, validationParameters, out _);

        var subject = principal.FindFirst("sub")?.Value
            ?? throw new SecurityTokenValidationException("Apple token does not contain subject.");

        ValidateNonce(principal, rawNonce);

        return new AppleIdentityPrincipal(
            Subject: subject,
            Email: principal.FindFirst("email")?.Value,
            EmailVerified: ParseBooleanClaim(principal.FindFirst("email_verified")?.Value),
            IsPrivateEmail: ParseBooleanClaim(principal.FindFirst("is_private_email")?.Value),
            Audience: principal.FindFirst("aud")?.Value);
    }

    private static void ValidateNonce(ClaimsPrincipal principal, string? rawNonce)
    {
        var tokenNonce = principal.FindFirst("nonce")?.Value;
        if (string.IsNullOrWhiteSpace(tokenNonce))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(rawNonce))
        {
            throw new SecurityTokenValidationException("rawNonce is required when Apple token contains a nonce claim.");
        }

        var hashedNonce = ComputeAppleNonce(rawNonce);
        if (!string.Equals(tokenNonce, hashedNonce, StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityTokenValidationException("Apple nonce validation failed.");
        }
    }

    private static bool? ParseBooleanClaim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return bool.TryParse(value, out var result) ? result : null;
    }

    private static string ComputeAppleNonce(string rawNonce)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawNonce));
        return Convert.ToHexStringLower(hash);
    }
}
