using Microsoft.Extensions.Options;

namespace TrustMap.Infrastructure.Authentication;

public sealed class JwtOptionsValidator(string environmentName) : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        if (name is not null && name != Options.DefaultName)
        {
            return ValidateOptionsResult.Skip;
        }

        return JwtSigningKeyValidator.IsValidForEnvironment(options, environmentName)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(JwtSigningKeyValidator.FailureMessage);
    }
}
