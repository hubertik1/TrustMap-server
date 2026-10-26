using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TrustMap.Api.Common;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Authentication;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class DemoAccessService(
    TrustMapDbContext dbContext,
    DemoDataSeeder seeder,
    SessionTokenService tokenService,
    IOptions<DemoAccessOptions> optionsAccessor)
{
    // Prevent simultaneous first logins from seeding the same fixture twice.
    private static readonly SemaphoreSlim ProvisioningGate = new(1, 1);

    public async Task<AuthSessionResult> SignInAsync(string username, string password,
        string? userAgent, string? ipAddress, CancellationToken cancellationToken)
    {
        var options = optionsAccessor.Value;
        if (!options.Enabled)
            throw ApiException.NotFound("Demo access is not available.", "demo_access_unavailable");

        // Always perform the password derivation, including for an unknown username.
        var passwordMatches = options.VerifyPassword(password);
        if (!passwordMatches || !string.Equals(username.Trim(), options.Username, StringComparison.OrdinalIgnoreCase))
            throw ApiException.Unauthorized("The demo username or password is incorrect.", "invalid_demo_credentials");

        await ProvisioningGate.WaitAsync(cancellationToken);
        try
        {
            var user = await dbContext.Users.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.Id == options.AccountId, cancellationToken);
            if (user is null)
                user = await seeder.CreateAsync(options.AccountId, cancellationToken);

            // Deletion and moderation decisions remain effective. Signing in never resets data.
            if (user.Status != UserAccountStatus.Active)
                throw ApiException.Forbidden("This demo account is no longer active.", "account_not_active");

            var accessToken = tokenService.CreateAccessToken(user);
            var refreshToken = tokenService.CreateRefreshToken(user.Id, userAgent, ipAddress);
            dbContext.RefreshTokens.Add(refreshToken.Entity);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new AuthSessionResult(user, accessToken.Token, accessToken.ExpiresAtUtc,
                refreshToken.RawToken, refreshToken.Entity.ExpiresAt);
        }
        finally
        {
            ProvisioningGate.Release();
        }
    }
}
