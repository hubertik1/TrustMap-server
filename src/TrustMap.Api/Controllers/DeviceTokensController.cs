using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Domain.Entities;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Controllers;

[ApiController]
[Authorize]
[Route("me/device-tokens")]
public sealed class DeviceTokensController(TrustMapDbContext dbContext) : ControllerBase
{
    private const int MaxTokenLength = 256;
    private static readonly HashSet<string> SupportedPlatforms = new(StringComparer.Ordinal)
    {
        "ios"
    };

    private static readonly HashSet<string> SupportedEnvironments = new(StringComparer.Ordinal)
    {
        "development",
        "production"
    };

    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterDeviceTokenRequest request,
        CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var token = NormalizeAndValidateToken(request.Token);
        var platform = NormalizeAndValidateIdentifier(request.Platform, SupportedPlatforms, "platform");
        var environment = NormalizeAndValidateIdentifier(request.Environment, SupportedEnvironments, "environment");

        var existingToken = await dbContext.DeviceTokens
            .SingleOrDefaultAsync(
                x => x.Token == token && x.Platform == platform && x.Environment == environment,
                cancellationToken);

        if (existingToken is null)
        {
            dbContext.DeviceTokens.Add(new UserDeviceToken(
                Guid.CreateVersion7(),
                currentUserId,
                token,
                platform,
                environment,
                request.AppVersion,
                request.BuildNumber));
        }
        else
        {
            existingToken.RegisterForUser(
                currentUserId,
                token,
                platform,
                environment,
                request.AppVersion,
                request.BuildNumber);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{token}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(string token, CancellationToken cancellationToken)
    {
        var currentUserId = User.GetRequiredUserId();
        var normalizedToken = UserDeviceToken.NormalizeToken(token);

        if (string.IsNullOrWhiteSpace(normalizedToken))
        {
            return NoContent();
        }

        var deviceTokens = await dbContext.DeviceTokens
            .Where(x => x.UserId == currentUserId && x.Token == normalizedToken && x.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var deviceToken in deviceTokens)
        {
            deviceToken.Revoke();
        }

        if (deviceTokens.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    private static string NormalizeAndValidateToken(string? token)
    {
        var normalizedToken = UserDeviceToken.NormalizeToken(token ?? string.Empty);
        if (normalizedToken.Length is 0 or > MaxTokenLength)
        {
            throw ApiException.BadRequest("Device token is invalid.", "invalid_device_token");
        }

        return normalizedToken;
    }

    private static string NormalizeAndValidateIdentifier(
        string? value,
        IReadOnlySet<string> allowedValues,
        string fieldName)
    {
        var normalized = UserDeviceToken.NormalizeIdentifier(value ?? string.Empty);
        if (!allowedValues.Contains(normalized))
        {
            throw ApiException.BadRequest($"Device token {fieldName} is invalid.", $"invalid_device_token_{fieldName}");
        }

        return normalized;
    }

    public sealed record RegisterDeviceTokenRequest(
        string? Token,
        string? Platform,
        string? Environment,
        string? AppVersion,
        string? BuildNumber);
}
