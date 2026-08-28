using Hookwright.Core.Storage;
using Hookwright.EntityFrameworkCore;
using Hookwright.EntityFrameworkCore.Stores;
using Hookwright.Store.Conformance;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using Testcontainers.PostgreSql;

namespace Hookwright.PostgreSql.Tests;

internal sealed class PostgresStoreHarness : IStoreHarness
{
    private readonly string _database = $"hw_{Guid.CreateVersion7():N}"[..20];

    private PostgreSqlContainer? _server;

    public async Task InitialiseAsync()
    {
        _server = await PostgresTestServer.StartedAsync();

        await using NpgsqlConnection connection = new(_server.GetConnectionString());
        await connection.OpenAsync();

        await using NpgsqlCommand create = new($"CREATE DATABASE \"{_database}\"", connection);
        await create.ExecuteNonQueryAsync();

        await using HookwrightDbContext context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public IStoreSession OpenSession()
    {
        return new PostgresStoreSession(CreateContext());
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is null)
        {
            return;
        }

        await using NpgsqlConnection connection = new(_server.GetConnectionString());
        await connection.OpenAsync();

        await using NpgsqlCommand drop = new($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    private HookwrightDbContext CreateContext()
    {
        NpgsqlConnectionStringBuilder connectionString = new(_server!.GetConnectionString())
        {
            Database = _database
        };

        return new HookwrightDbContext(
            new DbContextOptionsBuilder<HookwrightDbContext>()
                .UseNpgsql(connectionString.ConnectionString)
                .Options);
    }

    private sealed class PostgresStoreSession : IStoreSession
    {
        private readonly HookwrightDbContext _context;

        public PostgresStoreSession(HookwrightDbContext context)
        {
            _context = context;

            Subscribers = new EfSubscriberStore(context);
            Endpoints = new EfEndpointStore(context);
            Events = new EfEventStore(context);
            Deliveries = new PostgresDeliveryLeaseStore(context);
        }

        public ISubscriberStore Subscribers { get; }

        public IWebhookEndpointStore Endpoints { get; }

        public IWebhookEventStore Events { get; }

        public IDeliveryLeaseStore Deliveries { get; }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
        }
    }
}