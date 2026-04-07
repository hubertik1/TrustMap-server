using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TrustMap.Domain.Entities;

namespace TrustMap.Infrastructure.Authentication;

public sealed class SessionTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _options = options.Value;

    public AccessTokenEnvelope CreateAccessToken(User user)
    {
        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString()),
            new Claim("handle", user.Handle),
            new Claim("display_name", user.DisplayName)
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            NotBefore = now,
            Expires = expiresAt,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(descriptor);

        return new AccessTokenEnvelope(handler.WriteToken(token), expiresAt);
    }

    public IssuedRefreshToken CreateRefreshToken(Guid userId, string? userAgent, string? ipAddress)
    {
        var rawToken = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        var tokenHash = RefreshTokenHasher.Hash(rawToken);
        var entity = new RefreshToken(
            Guid.CreateVersion7(),
            userId,
            tokenHash,
            DateTime.UtcNow.AddDays(_options.RefreshTokenDays),
            userAgent,
            ipAddress);

        return new IssuedRefreshToken(rawToken, entity);
    }
}
