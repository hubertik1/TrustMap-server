using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrustMap.Infrastructure.Authentication;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTrustMapInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<AppleAuthOptions>(configuration.GetSection(AppleAuthOptions.SectionName));
        services.Configure<PhotoStorageOptions>(configuration.GetSection(PhotoStorageOptions.SectionName));

        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is missing.");

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var serverVersion = new MySqlServerVersion(Version.Parse(databaseOptions.ServerVersion));

        services.AddDbContext<TrustMapDbContext>(options =>
        {
            options.UseMySql(
                connectionString,
                serverVersion,
                mySqlOptions => mySqlOptions.MigrationsAssembly(typeof(TrustMapDbContext).Assembly.FullName));
        });

        services.AddScoped<SessionTokenService>();
        services.AddSingleton<IAppleIdentityTokenVerifier, AppleIdentityTokenVerifier>();
        services.AddSingleton<IPhotoStorage, LocalPhotoStorage>();

        return services;
    }
}
