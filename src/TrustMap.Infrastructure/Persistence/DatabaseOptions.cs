namespace TrustMap.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ServerVersion { get; set; } = "8.4.0";

    public bool ApplyMigrationsOnStartup { get; set; }
}
