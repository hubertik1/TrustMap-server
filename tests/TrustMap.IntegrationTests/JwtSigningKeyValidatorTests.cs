using TrustMap.Infrastructure.Authentication;
using TrustMap.IntegrationTests.Infrastructure;

namespace TrustMap.IntegrationTests;

public sealed class JwtSigningKeyValidatorTests
{
    private const string DefaultDevelopmentSigningKey =
        "trustmap-local-development-signing-key-change-before-production";

    [Fact]
    public void Production_with_default_development_signing_key_fails()
    {
        var exception = AssertProductionFailure(DefaultDevelopmentSigningKey);

        Assert.Contains(JwtSigningKeyValidator.FailureMessage, exception.Message);
    }

    [Fact]
    public void Production_with_empty_signing_key_fails()
    {
        AssertProductionFailure(string.Empty);
    }

    [Fact]
    public void Production_with_whitespace_signing_key_fails()
    {
        AssertProductionFailure("   ");
    }

    [Fact]
    public void Production_with_short_signing_key_fails()
    {
        AssertProductionFailure("short-signing-key");
    }

    [Fact]
    public void Production_with_change_before_production_placeholder_fragment_fails()
    {
        AssertProductionFailure("prod-value-change-before-production-value-1234567890");
    }

    [Fact]
    public void Production_with_local_development_placeholder_fragment_fails()
    {
        AssertProductionFailure("prod-value-local-development-value-1234567890");
    }

    [Theory]
    [InlineData("placeholder")]
    [InlineData("example")]
    [InlineData("changeme")]
    [InlineData("replace-me")]
    [InlineData("secret")]
    [InlineData("test")]
    public void Production_with_placeholder_like_fragment_fails(string fragment)
    {
        AssertProductionFailure($"prod-value-{fragment}-value-1234567890");
    }

    [Theory]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("11111111111111111111111111111111")]
    [InlineData("passwordpasswordpasswordpassword")]
    [InlineData("trustmaptrustmaptrustmaptrustmap")]
    public void Repeated_or_simple_signing_key_fails(string signingKey)
    {
        AssertProductionFailure(signingKey);
    }

    [Fact]
    public void Production_with_strong_random_looking_signing_key_passes()
    {
        var exception = Record.Exception(() => JwtSigningKeyValidator.ValidateForEnvironment(
            new JwtOptions { SigningKey = "Z9rNd7VGn2QwP4sY8kLmC6xA1bAeF5hJ" },
            "Production"));

        Assert.Null(exception);
    }

    [Fact]
    public void Development_with_development_signing_key_passes()
    {
        var exception = Record.Exception(() => JwtSigningKeyValidator.ValidateForEnvironment(
            new JwtOptions { SigningKey = DefaultDevelopmentSigningKey },
            "Development"));

        Assert.Null(exception);
    }

    [Fact]
    public void Failure_message_does_not_contain_signing_key_value()
    {
        const string actualSigningKey = "prod-value-change-before-production-sensitive-token-1234567890";

        var exception = AssertProductionFailure(actualSigningKey);

        Assert.DoesNotContain(actualSigningKey, exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_startup_with_default_development_signing_key_fails()
    {
        using var factory = new TrustMapWebApplicationFactory(
            configurationOverrides: new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = DefaultDevelopmentSigningKey
            },
            environmentName: "Production");

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(JwtSigningKeyValidator.FailureMessage, exception.ToString());
        Assert.DoesNotContain(DefaultDevelopmentSigningKey, exception.ToString(), StringComparison.Ordinal);
    }

    private static InvalidOperationException AssertProductionFailure(string signingKey)
    {
        return Assert.Throws<InvalidOperationException>(() => JwtSigningKeyValidator.ValidateForEnvironment(
            new JwtOptions { SigningKey = signingKey },
            "Production"));
    }
}
