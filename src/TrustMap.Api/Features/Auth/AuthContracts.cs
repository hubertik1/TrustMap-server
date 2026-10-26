using System.ComponentModel.DataAnnotations;
using TrustMap.Api.Features.Users;

namespace TrustMap.Api.Features.Auth;

public sealed class AppleAuthRequest
{
    [Required]
    public string IdentityToken { get; init; } = null!;

    public string? AuthorizationCode { get; init; }

    public string? RawNonce { get; init; }

    [StringLength(320)]
    public string? Email { get; init; }

    [StringLength(100)]
    public string? DisplayName { get; init; }

    [StringLength(32)]
    public string? PreferredHandle { get; init; }
}

public sealed class DemoAuthRequest
{
    [Required, StringLength(100)]
    public string Username { get; init; } = null!;

    [Required, StringLength(256)]
    public string Password { get; init; } = null!;
}

public sealed class RefreshTokenRequest
{
    [Required]
    public string RefreshToken { get; init; } = null!;
}

public sealed class LogoutRequest
{
    public string? RefreshToken { get; init; }

    public bool AllDevices { get; init; }
}

public sealed record AuthSessionDto(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserProfileDto User);
