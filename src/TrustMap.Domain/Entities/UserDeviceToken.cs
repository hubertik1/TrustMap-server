namespace TrustMap.Domain.Entities;

public sealed class UserDeviceToken
{
    private UserDeviceToken()
    {
    }

    public UserDeviceToken(
        Guid id,
        Guid userId,
        string token,
        string platform,
        string environment,
        string? appVersion,
        string? buildNumber)
    {
        Id = id;
        CreatedAt = DateTime.UtcNow;
        RegisterForUser(userId, token, platform, environment, appVersion, buildNumber);
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string Token { get; private set; } = null!;

    public string Platform { get; private set; } = null!;

    public string Environment { get; private set; } = null!;

    public string? AppVersion { get; private set; }

    public string? BuildNumber { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public DateTime? LastSeenAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public User User { get; private set; } = null!;

    public void RegisterForUser(
        Guid userId,
        string token,
        string platform,
        string environment,
        string? appVersion,
        string? buildNumber)
    {
        var now = DateTime.UtcNow;
        UserId = userId;
        Token = NormalizeToken(token);
        Platform = NormalizeIdentifier(platform);
        Environment = NormalizeIdentifier(environment);
        AppVersion = NormalizeOptional(appVersion);
        BuildNumber = NormalizeOptional(buildNumber);
        UpdatedAt = now;
        LastSeenAt = now;
        RevokedAt = null;
    }

    public void Revoke()
    {
        var now = DateTime.UtcNow;
        RevokedAt ??= now;
        UpdatedAt = now;
    }

    public static string NormalizeToken(string token) =>
        token.Trim().ToLowerInvariant();

    public static string NormalizeIdentifier(string value) =>
        value.Trim().ToLowerInvariant();

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }
}
