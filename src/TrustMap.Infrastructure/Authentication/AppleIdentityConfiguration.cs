using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace TrustMap.Infrastructure.Authentication;

// Shared by startup warm-up and token validation, including after an idle restart.
public sealed class AppleIdentityConfiguration : IConfigurationManager<OpenIdConnectConfiguration>, IDisposable
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _manager;

    public AppleIdentityConfiguration(IOptions<AppleAuthOptions> options)
    {
        _manager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{options.Value.Issuer}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(_httpClient) { RequireHttps = true });
    }

    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancellationToken) =>
        // IdentityModel may keep its initial fetch running after a caller cancels.
        // Bound the caller's wait as well as each outbound HTTP request.
        _manager.GetConfigurationAsync(cancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

    public void RequestRefresh() => _manager.RequestRefresh();

    public void Dispose() => _httpClient.Dispose();
}
