using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrustMap.Infrastructure.Persistence;

public sealed class DesignTimeTrustMapDbContextFactory : IDesignTimeDbContextFactory<TrustMapDbContext>
{
    public TrustMapDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Data Source=App_Data/trustmap.db;Foreign Keys=True;";

        var sqliteBuilder = new SqliteConnectionStringBuilder(connectionString)
        {
            ForeignKeys = true
        };

        if (!string.Equals(sqliteBuilder.DataSource, ":memory:", StringComparison.Ordinal)
            && !Path.IsPathRooted(sqliteBuilder.DataSource)
            && !sqliteBuilder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            sqliteBuilder.DataSource = Path.GetFullPath(sqliteBuilder.DataSource, ResolveContentRootPath());
        }

        if (!string.Equals(sqliteBuilder.DataSource, ":memory:", StringComparison.Ordinal)
            && !sqliteBuilder.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.GetDirectoryName(sqliteBuilder.DataSource);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        var optionsBuilder = new DbContextOptionsBuilder<TrustMapDbContext>();
        optionsBuilder.UseSqlite(
            sqliteBuilder.ToString(),
            sqliteOptions => sqliteOptions.MigrationsAssembly(typeof(TrustMapDbContext).Assembly.FullName));

        return new TrustMapDbContext(optionsBuilder.Options);
    }

    private static string ResolveContentRootPath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var startupProjectPath = Path.Combine(currentDirectory, "src", "TrustMap.Api");
        return Directory.Exists(startupProjectPath)
            ? Path.GetFullPath(startupProjectPath)
            : currentDirectory;
    }
}
