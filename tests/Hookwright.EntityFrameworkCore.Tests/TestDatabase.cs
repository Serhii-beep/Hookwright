using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Tests;

internal static class TestDatabase
{
    internal static SqliteConnection Open()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        return connection;
    }

    internal static HookwrightDbContext CreateContext(SqliteConnection connection)
    {
        return new HookwrightDbContext(
            new DbContextOptionsBuilder<HookwrightDbContext>().UseSqlite(connection).Options);
    }

    internal static async Task<HookwrightDbContext> CreateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        HookwrightDbContext context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync(cancellationToken);

        return context;
    }
}