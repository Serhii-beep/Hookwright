using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.EntityFrameworkCore;
using Hookwright.EntityFrameworkCore.Stores;
using Hookwright.Signing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

internal sealed class SqliteDispatchDatabase : IAsyncDisposable
{
    private const int SeedBatch = 1_000;

    private static readonly string[] Suffixes = ["", "-journal", "-wal", "-shm"];

    private static readonly PlaintextSecretProtector Protector = new();

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hookwright-{Guid.CreateVersion7():N}.db");

    private SqliteDispatchDatabase()
    {
        Options = new DbContextOptionsBuilder<HookwrightDbContext>().UseSqlite($"Data Source={_path}").Options;
    }

    public DbContextOptions<HookwrightDbContext> Options { get; }

    public static async Task<SqliteDispatchDatabase> CreateAsync(CancellationToken token)
    {
        SqliteDispatchDatabase database = new();

        await using HookwrightDbContext context = database.CreateContext();
        await context.Database.EnsureCreatedAsync(token);

        return database;
    }

    public HookwrightDbContext CreateContext()
    {
        return new HookwrightDbContext(Options);
    }

    public async Task<WebhookEndpoint> SeedAsync(
        Uri url,
        int count,
        WebhookSecret secret,
        DateTimeOffset at,
        Func<int, string> payload,
        CancellationToken token)
    {
        Subscriber subscriber = Subscriber.Create($"org_{Guid.CreateVersion7():N}", null, at);
        WebhookEndpoint endpoint = WebhookEndpoint.Create(subscriber.Id, url, null, at);

        await using (HookwrightDbContext context = CreateContext())
        {
            await new EfSubscriberStore(context).AddAsync(subscriber, token);
            await new EfEndpointStore(context).AddAsync(
                EndpointRegistration.Create(
                    endpoint, [EndpointSecret.Create(endpoint.Id, Protector.Protect(secret), SignatureAlgorithm.HmacSha256, at)]),
                token);
        }

        for (int first = 0; first < count; first += SeedBatch)
        {
            await using HookwrightDbContext context = CreateContext();
            EfEventStore events = new(context);

            for (int i = first; i < Math.Min(first + SeedBatch, count); i++)
            {
                WebhookEvent published = WebhookEvent.Create(subscriber.Id, "order.created", payload(i), at);
                events.Append(EventPublication.Create(published, [Delivery.Create(published.Id, endpoint.Id, null, at)]));
            }

            await events.CommitAsync(token);
        }

        return endpoint;
    }

    public async Task<int> CountAsync(DeliveryState state, CancellationToken token)
    {
        await using HookwrightDbContext context = CreateContext();

        return await context.Set<Delivery>().CountAsync(delivery => delivery.State == state, token);
    }

    public async Task<int> CountAsync(DeliveryState state, WebhookEndpointId endpointId, CancellationToken token)
    {
        await using HookwrightDbContext context = CreateContext();

        return await context.Set<Delivery>().CountAsync(delivery => delivery.State == state && delivery.EndpointId == endpointId, token);
    }

    public async Task<(int Total, int Succeeded)> CountAttemptsAsync(CancellationToken token)
    {
        await using HookwrightDbContext context = CreateContext();
        IQueryable<DeliveryAttempt> attempts = context.Set<DeliveryAttempt>();

        return (
            await attempts.CountAsync(token),
            await attempts.CountAsync(attempt => attempt.Outcome == AttemptOutcome.Succeeded && attempt.ResponseStatusCode == 200, token));
    }

    public async Task<WebhookEndpoint> FindEndpointAsync(WebhookEndpointId endpointId, CancellationToken token)
    {
        await using HookwrightDbContext context = CreateContext();

        return (await new EfEndpointStore(context).FindAsync(endpointId, token)).ShouldNotBeNull();
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                foreach (string suffix in Suffixes)
                {
                    File.Delete(_path + suffix);
                }

                return;
            }
            catch (IOException) when (attempt < 5)
            {
                await Task.Delay(100);
            }
        }
    }
}