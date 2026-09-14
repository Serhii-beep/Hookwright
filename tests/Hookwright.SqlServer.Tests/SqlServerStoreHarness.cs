using Hookwright.Core.Storage;
using Hookwright.EntityFrameworkCore;
using Hookwright.EntityFrameworkCore.Stores;
using Hookwright.Store.Conformance;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using Testcontainers.MsSql;

namespace Hookwright.SqlServer.Tests;

internal sealed class SqlServerStoreHarness : IStoreHarness
{
    private readonly string _database = $"hw_{Guid.CreateVersion7():N}"[..20];

    private MsSqlContainer? _server;

    public async Task InitialiseAsync()
    {
        _server = await SqlServerTestServer.StartedAsync();

        await using SqlConnection connection = new(_server.GetConnectionString());
        await connection.OpenAsync();

        await using SqlCommand create = new($"CREATE DATABASE [{_database}]", connection);
        await create.ExecuteNonQueryAsync();

        await using HookwrightDbContext context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public IStoreSession OpenSession()
    {
        return new SqlServerStoreSession(CreateContext());
    }

    public async ValueTask DisposeAsync()
    {
        if (_server is null)
        {
            return;
        }

        await using SqlConnection connection = new(_server.GetConnectionString());
        await connection.OpenAsync();

        await using SqlCommand drop = new(
            $"ALTER DATABASE [{_database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_database}]",
            connection);
        await drop.ExecuteNonQueryAsync();
    }

    private HookwrightDbContext CreateContext()
    {
        SqlConnectionStringBuilder connectionString = new(_server!.GetConnectionString())
        {
            InitialCatalog = _database
        };

        return new HookwrightDbContext(
            new DbContextOptionsBuilder<HookwrightDbContext>()
                .UseSqlServer(connectionString.ConnectionString)
                .Options);
    }

    private sealed class SqlServerStoreSession : IStoreSession
    {
        private readonly HookwrightDbContext _context;

        public SqlServerStoreSession(HookwrightDbContext context)
        {
            _context = context;

            Subscribers = new EfSubscriberStore(context);
            Endpoints = new EfEndpointStore(context);
            Events = new EfEventStore(context);
            Deliveries = new SqlServerDeliveryLeaseStore(context);
            Retention = new EfRetentionStore(context);
        }

        public ISubscriberStore Subscribers { get; }

        public IWebhookEndpointStore Endpoints { get; }

        public IWebhookEventStore Events { get; }

        public IDeliveryLeaseStore Deliveries { get; }

        public IRetentionStore Retention { get; }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
        }
    }
}