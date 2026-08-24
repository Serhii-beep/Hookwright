using Hookwright.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hookwright.Sqlite.Tests;

internal sealed class SqliteDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HookwrightDbContext>
{
    public HookwrightDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<HookwrightDbContext> options = new DbContextOptionsBuilder<HookwrightDbContext>()
            .UseSqlite(
                "Data Source=:memory:",
                sqlite =>
                {
                    sqlite.MigrationsAssembly(typeof(SqliteDeliveryLeaseStore).Assembly.GetName().Name);
                    sqlite.MigrationsHistoryTable(HookwrightTables.MigrationsHistory);
                })
            .Options;

        return new HookwrightDbContext(options);
    }
}