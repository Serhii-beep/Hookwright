using Hookwright.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Hookwright.SqlServer.Tests;

internal sealed class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HookwrightDbContext>
{
    public HookwrightDbContext CreateDbContext(string[] args)
    {
        DbContextOptions<HookwrightDbContext> options = new DbContextOptionsBuilder<HookwrightDbContext>()
            .UseSqlServer("Server=localhost;Database=hookwright;TrustServerCertificate=True", sqlServer =>
            {
                sqlServer.MigrationsAssembly(typeof(SqlServerDeliveryLeaseStore).Assembly.GetName().Name);
                sqlServer.MigrationsHistoryTable(HookwrightTables.MigrationsHistory);
            })
            .Options;

        return new HookwrightDbContext(options);
    }
}