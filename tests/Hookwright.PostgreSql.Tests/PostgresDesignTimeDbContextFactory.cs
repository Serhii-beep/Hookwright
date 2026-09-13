using Hookwright.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hookwright.PostgreSql.Tests;

internal sealed class PostgresDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HookwrightDbContext>
{
    public HookwrightDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<HookwrightDbContext> options = new DbContextOptionsBuilder<HookwrightDbContext>()
            .UseNpgsql("Host=localhost;Database=hookwright", npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(PostgresDeliveryLeaseStore).Assembly.GetName().Name);
                npgsql.MigrationsHistoryTable(HookwrightTables.MigrationsHistory);
            })
            .Options;

        return new HookwrightDbContext(options);
    }
}