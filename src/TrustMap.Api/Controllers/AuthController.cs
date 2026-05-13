using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TrustMap.Api.Common;
using TrustMap.Api.Features.Auth;
using TrustMap.Api.RateLimiting;
using TrustMap.Api.Services;

namespace TrustMap.Api.Controllers;

[ApiController]
[Route("auth")]
public sealed class AuthController(
    SessionService sessionService,
    ProfileQueryService profileQueryService) : ControllerBase
{
    [HttpPost("apple")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.Auth)]
    [ProducesResponseType<AuthSessionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthSessionDto>> SignInWithApple(
        [FromBody] AppleAuthRequest request,
        CancellationToken cancellationToken)
    {
        var session = await sessionService.SignInWithAppleAsync(
            request.IdentityToken,
            request.RawNonce,
            request.Email,
            request.DisplayName,
            request.PreferredHandle,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        var profile = await profileQueryService.GetProfileAsync(session.User.Id, session.User.Id, cancellationToken);

        return Ok(new AuthSessionDto(
            session.AccessToken,
            session.AccessTokenExpiresAtUtc,
            session.RefreshToken,
            session.RefreshTokenExpiresAtUtc,
            profile));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicyNames.RefreshToken)]
    [ProducesResponseType<AuthSessionDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthSessionDto>> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var session = await sessionService.RefreshAsync(
            request.RefreshToken,
            Request.Headers.UserAgent.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        var profile = await profileQueryService.GetProfileAsync(session.User.Id, session.User.Id, cancellationToken);

        return Ok(new AuthSessionDto(
            session.AccessToken,
            session.AccessTokenExpiresAtUtc,
            session.RefreshToken,
            session.RefreshTokenExpiresAtUtc,
            profile));
    }

    [HttpPost("logout")]
    [Authorize]
    [EnableRateLimiting(RateLimitPolicyNames.RefreshToken)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await sessionService.LogoutAsync(
            User.GetRequiredUserId(),
            request.RefreshToken,
            request.AllDevices,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            cancellationToken);

        return NoContent();
    }
}
