using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using TrustMap.Infrastructure.Authentication;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTrustMapInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRootPath,
        string environmentName)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>>(_ => new JwtOptionsValidator(environmentName));
        services.Configure<AppleAuthOptions>(configuration.GetSection(AppleAuthOptions.SectionName));
        services.Configure<PhotoStorageOptions>(configuration.GetSection(PhotoStorageOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is missing.");

        var sqliteConnectionString = BuildSqliteConnectionString(connectionString, contentRootPath);

        services.AddDbContext<TrustMapDbContext>(options =>
        {
            options.UseSqlite(
                sqliteConnectionString,
                sqliteOptions => sqliteOptions.MigrationsAssembly(typeof(TrustMapDbContext).Assembly.FullName));
        });

        services.AddScoped<SessionTokenService>();
        services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>, AppleIdentityConfiguration>();
        services.AddSingleton<IAppleIdentityTokenVerifier, AppleIdentityTokenVerifier>();
        services.AddSingleton<IPhotoStorage, LocalPhotoStorage>();

        return services;
    }

    private static string BuildSqliteConnectionString(string connectionString, string contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new InvalidOperationException("SQLite connection string must contain a Data Source.");
        }

        builder.ForeignKeys ??= true;

        if (!string.Equals(builder.DataSource, ":memory:", StringComparison.Ordinal)
            && !Path.IsPathRooted(builder.DataSource)
            && !builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            builder.DataSource = Path.GetFullPath(builder.DataSource, contentRootPath);
        }

        if (!string.Equals(builder.DataSource, ":memory:", StringComparison.Ordinal)
            && !builder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(builder.DataSource);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        return builder.ToString();
    }
}
