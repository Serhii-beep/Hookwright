using Hookwright.Core.Storage;
using Hookwright.EntityFrameworkCore;
using Hookwright.EntityFrameworkCore.Stores;
using Hookwright.Store.Conformance;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hookwright.Sqlite.Tests;

internal sealed class SqliteStoreHarness : IStoreHarness
{
    private readonly string _connectionString =
        $"Data Source=file:hookwright-{Guid.CreateVersion7():N}?mode=memory&cache=shared";

    private SqliteConnection? _keepAlive;

    public async Task InitialiseAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();

        await using HookwrightDbContext context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public IStoreSession OpenSession()
    {
        return new SqliteStoreSession(CreateContext());
    }

    public async ValueTask DisposeAsync()
    {
        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }
    }

    private HookwrightDbContext CreateContext()
    {
        return new HookwrightDbContext(
            new DbContextOptionsBuilder<HookwrightDbContext>().UseSqlite(_connectionString).Options);
    }

    private sealed class SqliteStoreSession : IStoreSession
    {
        private readonly HookwrightDbContext _context;

        public SqliteStoreSession(HookwrightDbContext context)
        {
            _context = context;

            Subscribers = new EfSubscriberStore(context);
            Endpoints = new EfEndpointStore(context);
            Events = new EfEventStore(context);
            Deliveries = new SqliteDeliveryLeaseStore(context);
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