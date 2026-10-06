using Microsoft.EntityFrameworkCore;
using TrustMap.Api.Common;
using TrustMap.Domain.Entities;
using TrustMap.Domain.Enums;
using TrustMap.Infrastructure.Authentication;
using TrustMap.Infrastructure.Persistence;

namespace TrustMap.Api.Services;

public sealed class SessionService(
    TrustMapDbContext dbContext,
    IAppleIdentityTokenVerifier appleIdentityTokenVerifier,
    SessionTokenService sessionTokenService,
    HandleGenerator handleGenerator,
    IAppleTokenClient appleTokenClient,
    AppleRefreshTokenProtection appleTokenProtection,
    Microsoft.Extensions.Options.IOptions<AppleAuthOptions> appleOptions)
{
    public async Task<AuthSessionResult> SignInWithAppleAsync(
        string identityToken,
        string? authorizationCode,
        string? rawNonce,
        string? email,
        string? displayName,
        string? preferredHandle,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var applePrincipal = await appleIdentityTokenVerifier.VerifyAsync(identityToken, rawNonce, cancellationToken);
        var persistedEmail = applePrincipal.Email ?? email;

        var appleIdentity = await dbContext.AppleIdentities
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.Subject == applePrincipal.Subject, cancellationToken);

        User user;

        if (appleIdentity is null)
        {
            var resolvedDisplayName = ResolveDisplayName(displayName, persistedEmail);
            var resolvedHandle = await handleGenerator.GenerateInitialAppleHandleAsync(
                persistedEmail,
                resolvedDisplayName,
                preferredHandle,
                cancellationToken);

            user = new User(Guid.CreateVersion7(), resolvedHandle, resolvedDisplayName, persistedEmail);
            appleIdentity = new AppleIdentity(
                Guid.CreateVersion7(),
                user.Id,
                applePrincipal.Subject,
                persistedEmail,
                applePrincipal.EmailVerified,
                applePrincipal.IsPrivateEmail);

            dbContext.Users.Add(user);
            dbContext.AppleIdentities.Add(appleIdentity);
        }
        else
        {
            user = appleIdentity.User;
            appleIdentity.UpdateLoginMetadata(persistedEmail, applePrincipal.EmailVerified, applePrincipal.IsPrivateEmail);

            if (user.Email is null && persistedEmail is not null)
            {
                user.UpdateEmail(persistedEmail);
            }
        }

        if (user.Status != UserAccountStatus.Active)
        {
            throw ApiException.Forbidden("This account is not active.", "account_not_active");
        }

        if (appleOptions.Value.TokenManagementEnabled)
        {
            if (string.IsNullOrWhiteSpace(authorizationCode) || string.IsNullOrWhiteSpace(applePrincipal.Audience))
                throw ApiException.BadRequest("A fresh Apple authorization code is required. Sign in with Apple again.", "apple_authorization_code_required");
            try
            {
                var token = await appleTokenClient.ExchangeAsync(authorizationCode, applePrincipal.Audience,
                    applePrincipal.Subject, rawNonce, cancellationToken);
                appleIdentity.StoreRefreshToken(applePrincipal.Audience, appleTokenProtection.Protect(token));
            }
            catch (AppleTokenException exception)
            {
                if (exception.InvalidGrant)
                    throw ApiException.Unauthorized("The Apple authorization code is invalid or expired. Sign in again.", "invalid_apple_authorization_code");
                throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable, "Service Unavailable",
                    "Apple sign-in is temporarily unavailable. Please try again.", "apple_token_service_unavailable");
            }
        }

        var accessToken = sessionTokenService.CreateAccessToken(user);
        var refreshToken = sessionTokenService.CreateRefreshToken(user.Id, userAgent, ipAddress);

        dbContext.RefreshTokens.Add(refreshToken.Entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthSessionResult(
            user,
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            refreshToken.RawToken,
            refreshToken.Entity.ExpiresAt);
    }

    public async Task<AuthSessionResult> RefreshAsync(
        string rawRefreshToken,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var tokenHash = RefreshTokenHasher.Hash(rawRefreshToken);

        var refreshToken = await dbContext.RefreshTokens
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (refreshToken is null || !refreshToken.IsActive(DateTime.UtcNow))
        {
            throw ApiException.Unauthorized("Refresh token is invalid or expired.", "invalid_refresh_token");
        }

        if (refreshToken.User.Status != UserAccountStatus.Active)
        {
            throw ApiException.Forbidden("This account is not active.", "account_not_active");
        }

        var replacement = sessionTokenService.CreateRefreshToken(refreshToken.UserId, userAgent, ipAddress);
        refreshToken.Revoke(replacement.Entity.Id, ipAddress);
        dbContext.RefreshTokens.Add(replacement.Entity);

        var accessToken = sessionTokenService.CreateAccessToken(refreshToken.User);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthSessionResult(
            refreshToken.User,
            accessToken.Token,
            accessToken.ExpiresAtUtc,
            replacement.RawToken,
            replacement.Entity.ExpiresAt);
    }

    public async Task LogoutAsync(
        Guid userId,
        string? rawRefreshToken,
        bool allDevices,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        IQueryable<RefreshToken> query = dbContext.RefreshTokens.Where(x => x.UserId == userId && !x.RevokedAt.HasValue);

        if (!allDevices && !string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            var tokenHash = RefreshTokenHasher.Hash(rawRefreshToken);
            query = query.Where(x => x.TokenHash == tokenHash);
        }

        var tokens = await query.ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(null, ipAddress);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string ResolveDisplayName(string? displayName, string? email)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            return email.Split('@', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
                ?? "TrustMap user";
        }

        return "TrustMap user";
    }
}

public sealed record AuthSessionResult(
    User User,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
