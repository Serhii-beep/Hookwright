using Testcontainers.MsSql;

namespace Hookwright.SqlServer.Tests;

internal static class SqlServerTestServer
{
    private static readonly Lazy<Task<MsSqlContainer>> Instance = new(
        async () =>
        {
            MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

            await container.StartAsync();

            return container;
        },
        LazyThreadSafetyMode.ExecutionAndPublication);

    internal static Task<MsSqlContainer> StartedAsync()
    {
        return Instance.Value;
    }
}