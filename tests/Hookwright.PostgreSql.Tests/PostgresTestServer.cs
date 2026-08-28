using Testcontainers.PostgreSql;

namespace Hookwright.PostgreSql.Tests;

internal static class PostgresTestServer
{
    private static readonly Lazy<Task<PostgreSqlContainer>> Instance = new(
        async () =>
        {
            PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();

            await container.StartAsync();

            return container;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static Task<PostgreSqlContainer> StartedAsync()
    {
        return Instance.Value;
    }
}