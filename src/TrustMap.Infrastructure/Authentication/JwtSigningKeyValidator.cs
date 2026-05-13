using System.Text;

namespace TrustMap.Infrastructure.Authentication;

public static class JwtSigningKeyValidator
{
    public const string FailureMessage =
        "JWT signing key must be configured with a strong production secret. The default development key cannot be used in Production.";

    private const int MinimumSigningKeyCharacters = 32;
    private const int MinimumSigningKeyBytes = 32;

    private static readonly string[] ProductionDevelopmentKeys =
    [
        "trustmap-local-development-signing-key-change-before-production",
        "change-me-super-long-signing-key-for-local-development"
    ];

    private static readonly string[] ProductionPlaceholderFragments =
    [
        "change-before-production",
        "local-development",
        "development-signing-key",
        "placeholder",
        "example",
        "changeme",
        "replace-me",
        "secret",
        "test"
    ];

    public static void ValidateForEnvironment(JwtOptions options, string environmentName)
    {
        if (!IsValidForEnvironment(options, environmentName))
        {
            throw new InvalidOperationException(FailureMessage);
        }
    }

    public static bool IsValidForEnvironment(JwtOptions? options, string? environmentName)
    {
        if (options is null || string.IsNullOrWhiteSpace(options.SigningKey))
        {
            return false;
        }

        var signingKey = options.SigningKey.Trim();
        if (signingKey.Length < MinimumSigningKeyCharacters ||
            Encoding.UTF8.GetByteCount(signingKey) < MinimumSigningKeyBytes)
        {
            return false;
        }

        if (IsSimpleRepeatedValue(signingKey))
        {
            return false;
        }

        return !IsProduction(environmentName) || !IsProductionPlaceholder(signingKey);
    }

    private static bool IsProduction(string? environmentName)
    {
        return string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProductionPlaceholder(string signingKey)
    {
        foreach (var developmentKey in ProductionDevelopmentKeys)
        {
            if (string.Equals(signingKey, developmentKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        foreach (var fragment in ProductionPlaceholderFragments)
        {
            if (signingKey.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSimpleRepeatedValue(string signingKey)
    {
        var firstCharacter = signingKey[0];
        if (signingKey.All(character => character == firstCharacter))
        {
            return true;
        }

        for (var patternLength = 2; patternLength <= signingKey.Length / 2; patternLength++)
        {
            if (signingKey.Length % patternLength != 0)
            {
                continue;
            }

            var pattern = signingKey[..patternLength];
            var isRepeatedPattern = true;
            for (var index = patternLength; index < signingKey.Length; index += patternLength)
            {
                if (!signingKey.AsSpan(index, patternLength).SequenceEqual(pattern))
                {
                    isRepeatedPattern = false;
                    break;
                }
            }

            if (isRepeatedPattern)
            {
                return true;
            }
        }

        return false;
    }
}
