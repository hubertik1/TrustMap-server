using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TrustMap.Infrastructure.Persistence;

public sealed class DesignTimeTrustMapDbContextFactory : IDesignTimeDbContextFactory<TrustMapDbContext>
{
    public TrustMapDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Server=localhost;Port=3306;Database=trustmap;User=root;Password=trustmap;";

        var optionsBuilder = new DbContextOptionsBuilder<TrustMapDbContext>();
        optionsBuilder.UseMySql(
            connectionString,
            new MySqlServerVersion(new Version(8, 4, 0)),
            mySqlOptions => mySqlOptions.MigrationsAssembly(typeof(TrustMapDbContext).Assembly.FullName));

        return new TrustMapDbContext(optionsBuilder.Options);
    }
}
