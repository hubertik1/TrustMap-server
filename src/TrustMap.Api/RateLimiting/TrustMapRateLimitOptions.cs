using Microsoft.Extensions.Options;

namespace TrustMap.Api.RateLimiting;

public sealed class TrustMapRateLimitOptions
{
    public const string SectionName = "RateLimits";

    public bool Enabled { get; init; } = true;

    public RateLimitPolicyOptions Global { get; init; } = new()
    {
        PermitLimit = 600,
        WindowSeconds = 60
    };

    public RateLimitPolicyOptions Auth { get; init; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 60
    };

    public RateLimitPolicyOptions RefreshToken { get; init; } = new()
    {
        PermitLimit = 30,
        WindowSeconds = 60
    };

    public RateLimitPolicyOptions Upload { get; init; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 3_600
    };

    public RateLimitPolicyOptions Search { get; init; } = new()
    {
        PermitLimit = 60,
        WindowSeconds = 60
    };

    public RateLimitPolicyOptions FriendRequests { get; init; } = new()
    {
        PermitLimit = 10,
        WindowSeconds = 3_600
    };
}

public sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; init; }

    public int WindowSeconds { get; init; }

    public int QueueLimit { get; init; }
}

public sealed class TrustMapRateLimitOptionsValidator : IValidateOptions<TrustMapRateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, TrustMapRateLimitOptions options)
    {
        var failures = ValidateOptions(options);
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    public static void ThrowIfInvalid(TrustMapRateLimitOptions options)
    {
        var failures = ValidateOptions(options);
        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(TrustMapRateLimitOptions),
                failures);
        }
    }

    private static IReadOnlyList<string> ValidateOptions(TrustMapRateLimitOptions options)
    {
        var failures = new List<string>();
        ValidatePolicy(nameof(options.Global), options.Global, failures);
        ValidatePolicy(nameof(options.Auth), options.Auth, failures);
        ValidatePolicy(nameof(options.RefreshToken), options.RefreshToken, failures);
        ValidatePolicy(nameof(options.Upload), options.Upload, failures);
        ValidatePolicy(nameof(options.Search), options.Search, failures);
        ValidatePolicy(nameof(options.FriendRequests), options.FriendRequests, failures);
        return failures;
    }

    private static void ValidatePolicy(
        string policyName,
        RateLimitPolicyOptions options,
        ICollection<string> failures)
    {
        if (options.PermitLimit <= 0)
        {
            failures.Add($"RateLimits:{policyName}:PermitLimit must be positive.");
        }

        if (options.WindowSeconds <= 0)
        {
            failures.Add($"RateLimits:{policyName}:WindowSeconds must be positive.");
        }

        if (options.QueueLimit < 0)
        {
            failures.Add($"RateLimits:{policyName}:QueueLimit cannot be negative.");
        }
    }
}
