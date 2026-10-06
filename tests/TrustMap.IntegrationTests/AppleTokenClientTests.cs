using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TrustMap.Api.Services;
using TrustMap.Infrastructure.Authentication;

namespace TrustMap.IntegrationTests;

public sealed class AppleTokenClientTests
{
    [Fact]
    public async Task Exchange_UsesAppleEndpointAndValidES256Secret_BindsReturnedIdentity()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Settings(key);
        var handler = new RecordingHandler(HttpStatusCode.OK,
            "{\"refresh_token\":\"apple-refresh\",\"id_token\":\"verified-identity\"}");
        var verifier = new RecordingVerifier();
        using var http = new HttpClient(handler);
        var client = new AppleTokenClient(http, Options.Create(options), verifier, TimeProvider.System);
        Assert.Equal("apple-refresh", await client.ExchangeAsync("fresh-code", "com.test.app", "apple-user", "nonce", default));
        Assert.Equal("https://appleid.apple.com/auth/token", handler.Url);
        Assert.Equal("POST", handler.Method);
        Assert.Equal("authorization_code", handler.Fields["grant_type"]);
        Assert.Equal("fresh-code", handler.Fields["code"]);
        Assert.Equal("com.test.app", handler.Fields["client_id"]);
        Assert.False(handler.Fields.ContainsKey("redirect_uri")); // Native flow supplied none.
        Assert.Equal("nonce", verifier.RawNonce);
        Assert.Equal("verified-identity", verifier.IdentityToken);

        var jwtHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        jwtHandler.ValidateToken(handler.Fields["client_secret"], new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new ECDsaSecurityKey(key) { KeyId = "TESTKEY" },
            ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidIssuer = "TESTTEAM", ValidAudience = "https://appleid.apple.com",
            ValidateLifetime = true, ClockSkew = TimeSpan.Zero
        }, out var validated);
        var jwt = (JwtSecurityToken)validated;
        Assert.Equal("com.test.app", jwt.Subject);
        Assert.Equal("TESTKEY", jwt.Header.Kid);
        Assert.InRange(jwt.ValidTo - DateTime.UtcNow, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(6));
    }

    [Theory]
    [InlineData("other-user", "com.test.app")]
    [InlineData("apple-user", "other-client")]
    public async Task Exchange_RejectsMismatchedSubjectOrAudience(string subject, string audience)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var http = new HttpClient(new RecordingHandler(HttpStatusCode.OK,
            "{\"refresh_token\":\"secret\",\"id_token\":\"verified-identity\"}"));
        var client = new AppleTokenClient(http, Options.Create(Settings(key)),
            new RecordingVerifier { Subject = subject, Audience = audience }, TimeProvider.System);
        var error = await Assert.ThrowsAsync<AppleTokenException>(() => client.ExchangeAsync("code", "com.test.app", "apple-user", "nonce", default));
        Assert.True(error.InvalidGrant);
    }

    [Fact]
    public async Task Revoke_SendsRefreshTokenAndAcceptsEmpty200Response()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var handler = new RecordingHandler(HttpStatusCode.OK, "");
        using var http = new HttpClient(handler);
        var client = new AppleTokenClient(http, Options.Create(Settings(key)), new RecordingVerifier(), TimeProvider.System);
        await client.RevokeAsync("refresh-secret", "com.test.app", default);
        Assert.Equal("https://appleid.apple.com/auth/revoke", handler.Url);
        Assert.Equal("refresh-secret", handler.Fields["token"]);
        Assert.Equal("refresh_token", handler.Fields["token_type_hint"]);
        Assert.Equal("com.test.app", handler.Fields["client_id"]);
        Assert.False(handler.Fields.ContainsKey("code"));
    }

    [Fact]
    public async Task ProviderError_DoesNotExposeTokensOrProviderBody()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var http = new HttpClient(new RecordingHandler(HttpStatusCode.BadRequest,
            "{\"error\":\"invalid_grant\",\"error_description\":\"sensitive-provider-details\"}"));
        var client = new AppleTokenClient(http, Options.Create(Settings(key)), new RecordingVerifier(), TimeProvider.System);
        var error = await Assert.ThrowsAsync<AppleTokenException>(() => client.ExchangeAsync("secret-code", "com.test.app", "apple-user", null, default));
        Assert.True(error.InvalidGrant);
        Assert.DoesNotContain("sensitive-provider-details", error.ToString());
        Assert.DoesNotContain("secret-code", error.ToString());
    }

    [Fact]
    public void EnabledOptions_RequireSignInWithApplePrivateKey()
    {
        var validator = new AppleTokenOptionsValidator();
        Assert.True(validator.Validate(null, new AppleAuthOptions { TokenManagementEnabled = true }).Failed);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var options = Settings(key);
        Assert.True(validator.Validate(null, options).Succeeded);
        options.PrivateKeyPem = key.ExportSubjectPublicKeyInfoPem();
        Assert.True(validator.Validate(null, options).Failed);
    }

    private static AppleAuthOptions Settings(ECDsa key) => new()
    {
        TokenManagementEnabled = true, TeamId = "TESTTEAM", KeyId = "TESTKEY",
        AllowedAudiences = ["com.test.app"], PrivateKeyPem = key.ExportPkcs8PrivateKeyPem()
    };

    private sealed class RecordingVerifier : IAppleIdentityTokenVerifier
    {
        public string Subject { get; init; } = "apple-user";
        public string Audience { get; init; } = "com.test.app";
        public string? RawNonce { get; private set; }
        public string? IdentityToken { get; private set; }
        public Task<AppleIdentityPrincipal> VerifyAsync(string identityToken, string? rawNonce, CancellationToken cancellationToken)
        {
            RawNonce = rawNonce; IdentityToken = identityToken;
            return Task.FromResult(new AppleIdentityPrincipal(Subject, null, null, null, Audience));
        }
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Url { get; private set; }
        public string? Method { get; private set; }
        public Dictionary<string, string> Fields { get; private set; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.AbsoluteUri;
            Method = request.Method.Method;
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            Fields = form.Split('&').Select(x => x.Split('=', 2)).ToDictionary(
                x => Uri.UnescapeDataString(x[0].Replace('+', ' ')), x => Uri.UnescapeDataString(x[1].Replace('+', ' ')));
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
