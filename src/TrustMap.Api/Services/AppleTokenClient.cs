using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TrustMap.Infrastructure.Authentication;

namespace TrustMap.Api.Services;

public interface IAppleTokenClient
{
    Task<string> ExchangeAsync(string code, string clientId, string subject, string? rawNonce, CancellationToken cancellationToken);
    Task RevokeAsync(string refreshToken, string clientId, CancellationToken cancellationToken);
}

public sealed class AppleTokenException(bool invalidGrant = false) : Exception("Apple token request failed.")
{
    public bool InvalidGrant { get; } = invalidGrant;
}

public sealed class AppleRefreshTokenProtection(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("TrustMap.AppleRefreshToken.v1");
    public string Protect(string token) => _protector.Protect(token);
    public string Unprotect(string token) => _protector.Unprotect(token);
}

public sealed class AppleTokenClient(
    HttpClient httpClient,
    IOptions<AppleAuthOptions> options,
    IAppleIdentityTokenVerifier verifier,
    TimeProvider timeProvider) : IAppleTokenClient
{
    public async Task<string> ExchangeAsync(string code, string clientId, string subject, string? rawNonce, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await PostAsync("token", clientId,
                new() { ["grant_type"] = "authorization_code", ["code"] = code }, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (tokens is null || string.IsNullOrWhiteSpace(tokens.RefreshToken) || string.IsNullOrWhiteSpace(tokens.IdentityToken))
                throw new AppleTokenException();

            // Bind the exchanged grant to the identity and audience already verified at login.
            var principal = await verifier.VerifyAsync(tokens.IdentityToken, rawNonce, cancellationToken);
            if (principal.Subject != subject || principal.Audience != clientId)
                throw new AppleTokenException(invalidGrant: true);
            return tokens.RefreshToken;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (AppleTokenException) { throw; }
        catch (Exception) { throw new AppleTokenException(); }
    }

    public async Task RevokeAsync(string refreshToken, string clientId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await PostAsync("revoke", clientId,
                new() { ["token"] = refreshToken, ["token_type_hint"] = "refresh_token" }, cancellationToken);
            // Apple also returns 200 for a token already revoked: safe to retry after a crash.
            await EnsureSuccessAsync(response, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (AppleTokenException) { throw; }
        catch (Exception) { throw new AppleTokenException(); }
    }

    private async Task<HttpResponseMessage> PostAsync(string endpoint, string clientId, Dictionary<string, string> fields, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.TokenManagementEnabled || !settings.AllowedAudiences.Contains(clientId))
            throw new AppleTokenException();
        fields["client_id"] = clientId;
        fields["client_secret"] = CreateClientSecret(settings, clientId);
        using var content = new FormUrlEncodedContent(fields);
        return await httpClient.PostAsync($"https://appleid.apple.com/auth/{endpoint}", content, cancellationToken);
    }

    private string CreateClientSecret(AppleAuthOptions settings, string clientId)
    {
        using var key = ECDsa.Create();
        key.ImportFromPem(string.IsNullOrWhiteSpace(settings.PrivateKeyPem)
            ? File.ReadAllText(settings.PrivateKeyPath) : settings.PrivateKeyPem);
        var securityKey = new ECDsaSecurityKey(key)
        {
            KeyId = settings.KeyId,
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
        };
        var now = timeProvider.GetUtcNow();
        var jwt = new JwtSecurityToken(
            new JwtHeader(new SigningCredentials(securityKey, SecurityAlgorithms.EcdsaSha256)),
            new JwtPayload
            {
                ["iss"] = settings.TeamId,
                ["sub"] = clientId,
                ["aud"] = "https://appleid.apple.com",
                ["iat"] = now.ToUnixTimeSeconds(),
                ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds()
            });
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.OK) return;
        var invalidGrant = false;
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorResponse>(cancellationToken);
                invalidGrant = error?.Error == "invalid_grant";
            }
            catch (JsonException) { }
        }
        // Never include Apple's body, submitted code, token or client secret in exceptions/logs.
        throw new AppleTokenException(invalidGrant);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdentityToken);
    private sealed record ErrorResponse([property: JsonPropertyName("error")] string? Error);
}

public sealed class AppleTokenOptionsValidator : IValidateOptions<AppleAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AppleAuthOptions options)
    {
        if (!options.TokenManagementEnabled) return ValidateOptionsResult.Success;
        if (string.IsNullOrWhiteSpace(options.TeamId) || string.IsNullOrWhiteSpace(options.KeyId)
            || options.AllowedAudiences.Count == 0
            || string.IsNullOrWhiteSpace(options.PrivateKeyPem) == string.IsNullOrWhiteSpace(options.PrivateKeyPath))
            return ValidateOptionsResult.Fail("Apple token management requires TeamId, KeyId, AllowedAudiences and exactly one PrivateKeyPem or PrivateKeyPath.");
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(string.IsNullOrWhiteSpace(options.PrivateKeyPem)
                ? File.ReadAllText(options.PrivateKeyPath) : options.PrivateKeyPem);
            var parameters = key.ExportParameters(includePrivateParameters: true);
            if (parameters.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value || parameters.D is null)
                return ValidateOptionsResult.Fail("Apple token management requires a P-256 private key.");
        }
        catch (Exception)
        {
            return ValidateOptionsResult.Fail("The Apple Sign in with Apple private key could not be loaded.");
        }
        return ValidateOptionsResult.Success;
    }
}
