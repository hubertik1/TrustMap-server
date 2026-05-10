using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TrustMap.Infrastructure.Authentication;
using TrustMap.Infrastructure.Persistence;
using TrustMap.Infrastructure.Storage;

namespace TrustMap.IntegrationTests.Infrastructure;

internal sealed class TrustMapWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly string _photoRootPath = Path.Combine(Path.GetTempPath(), "trustmap-tests", Guid.NewGuid().ToString("N"));
    private readonly Action<IServiceCollection>? _configureServices;

    public TrustMapWebApplicationFactory(Action<IServiceCollection>? configureServices = null)
    {
        _configureServices = configureServices;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "TrustMap.Test",
                ["Jwt:Audience"] = "TrustMap.Tests",
                ["Jwt:SigningKey"] = "trustmap-test-signing-key-with-sufficient-length",
                ["Jwt:AccessTokenMinutes"] = "30",
                ["Jwt:RefreshTokenDays"] = "7",
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["PhotoStorage:Provider"] = "local",
                ["PhotoStorage:RootPath"] = _photoRootPath,
                ["PhotoStorage:RequestPath"] = "/uploads"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<TrustMapDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<TrustMapDbContext>();
            services.RemoveAll<IDbContextOptionsConfiguration<TrustMapDbContext>>();
            services.RemoveAll<IAppleIdentityTokenVerifier>();

            services.AddSingleton(_connection);
            services.AddDbContext<TrustMapDbContext>((serviceProvider, options) =>
            {
                options.UseSqlite(
                    serviceProvider.GetRequiredService<SqliteConnection>(),
                    sqliteOptions => sqliteOptions.MigrationsAssembly(typeof(TrustMapDbContext).Assembly.FullName));
            });

            services.AddSingleton<IAppleIdentityTokenVerifier, FakeAppleIdentityTokenVerifier>();

            services.PostConfigure<DatabaseOptions>(options =>
            {
                options.ApplyMigrationsOnStartup = false;
            });

            services.PostConfigure<PhotoStorageOptions>(options =>
            {
                options.Provider = "local";
                options.RootPath = _photoRootPath;
                options.RequestPath = "/uploads";
            });

            _configureServices?.Invoke(services);
        });
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_photoRootPath);
        await _connection.OpenAsync();
        await ResetAsync();
    }

    public new async Task DisposeAsync()
    {
        await _connection.DisposeAsync();

        if (Directory.Exists(_photoRootPath))
        {
            Directory.Delete(_photoRootPath, recursive: true);
        }
    }

    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TrustMapDbContext>();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.MigrateAsync();
    }
}
