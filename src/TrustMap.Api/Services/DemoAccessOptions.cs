using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace TrustMap.Api.Services;

public sealed class DemoAccessOptions
{
    public const string SectionName = "DemoAccess";
    public const int PasswordIterations = 600_000;

    public bool Enabled { get; set; }
    public string Username { get; set; } = "appreview";
    public string PasswordSalt { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public Guid AccountId { get; set; }

    public bool VerifyPassword(string password)
    {
        var actual = Rfc2898DeriveBytes.Pbkdf2(password,
            Convert.FromBase64String(PasswordSalt), PasswordIterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(PasswordHash));
    }
}

public sealed class DemoAccessOptionsValidator : IValidateOptions<DemoAccessOptions>
{
    public ValidateOptionsResult Validate(string? name, DemoAccessOptions options)
    {
        if (!options.Enabled) return ValidateOptionsResult.Success;
        if (string.IsNullOrWhiteSpace(options.Username) || options.Username.Length > 100 || options.AccountId == Guid.Empty)
            return ValidateOptionsResult.Fail("DemoAccess requires a username and a non-empty AccountId.");
        try
        {
            if (Convert.FromBase64String(options.PasswordSalt).Length >= 16
                && Convert.FromBase64String(options.PasswordHash).Length == 32)
                return ValidateOptionsResult.Success;
        }
        catch (FormatException) { }
        return ValidateOptionsResult.Fail("DemoAccess requires a base64 salt (at least 16 bytes) and PBKDF2-SHA256 hash (32 bytes). Use scripts/configure_demo_access.py.");
    }
}
