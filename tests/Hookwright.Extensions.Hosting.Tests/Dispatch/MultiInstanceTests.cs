using System.Diagnostics;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.Dispatch;
using Hookwright.EntityFrameworkCore;
using Hookwright.EntityFrameworkCore.Stores;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;
using Hookwright.Signing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

[Collection("Dispatch")]
public sealed class MultiInstanceTests : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly DeliveryOptions Options = new()
    {
        BatchSize = 10,
        PollInterval = TimeSpan.FromMilliseconds(200),
        LeaseDuration = TimeSpan.FromSeconds(2),
        RequestTimeout = TimeSpan.FromSeconds(1)
    };

    private readonly string _connectionString = $"Data Source=file:hookwright-{Guid.CreateVersion7():N}?mode=memory&cache=shared";
    private readonly DbContextOptions<HookwrightDbContext> _contextOptions;
    private SqliteConnection? _keepAlive;

    public MultiInstanceTests()
    {
        _contextOptions = new DbContextOptionsBuilder<HookwrightDbContext>().UseSqlite(_connectionString).Options;
    }

    public async ValueTask InitializeAsync()
    {
        _keepAlive = new SqliteConnection(_connectionString);
        await _keepAlive.OpenAsync();

        await using HookwrightDbContext context = new(_contextOptions);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_keepAlive is not null)
        {
            await _keepAlive.DisposeAsync();
        }
    }

    [Fact]
    public async Task ThreeInstances_WhenOneIsKilledMidRun_TheOthersFinishEveryDelivery()
    {
        const int backlog = 300;
        CancellationToken token = TestContext.Current.CancellationToken;
        await SeedAsync(backlog, token);
        FakeLogCollector log = new();
        using CancellationTokenSource killed = new();
        using CancellationTokenSource stopping = new();

        DispatchInstance first = DispatchInstance.Start("web-01", Options, Store(), log, stopping.Token);
        DispatchInstance second = DispatchInstance.Start("web-02", Options, Store(), log, killed.Token, hangAfter: 20);
        DispatchInstance third = DispatchInstance.Start("web-03", Options, Store(), log, stopping.Token);

        await UntilAsync(
            async () => second.Completed == 20 && await CountHeldAsync(second.WorkerId, token) > 0,
            "the doomed instance to hang with work in hand",
            token);
        await killed.CancelAsync();
        await second.Stopped;

        await UntilAsync(async () => await CountAsync(DeliveryState.Succeeded, token) == backlog, "every delivery to succeed", token);

        await stopping.CancelAsync();
        await Task.WhenAll(first.Stopped, third.Stopped);

        (await CountAsync(DeliveryState.InFlight, token)).ShouldBe(0);
        (first.Completed + third.Completed).ShouldBeGreaterThan(0);
        log.GetSnapshot().ShouldContain(record => record.Message.StartsWith("Reclaimed ", StringComparison.Ordinal)
            && record.Message.EndsWith(second.WorkerId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GracefulStop_GivenWorkClaimedButNotStarted_HandsItBackAtOnceWithItsAttempt()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await SeedAsync(25, token);
        DeliveryClaimer claimer = new(Store(), Options, "web-01:4242:a1b2c3", TimeProvider.System, new FakeLogger<DeliveryClaimer>());
        using CancellationTokenSource stopping = new();

        Task run = claimer.RunAsync(stopping.Token);
        await UntilAsync(async () => await CountAsync(DeliveryState.InFlight, token) == 20, "two batches to be claimed", token);

        await stopping.CancelAsync();
        await run;
        (await claimer.ReleaseAsync(token)).ShouldBe(20);

        IReadOnlyList<ClaimedDelivery> next = await Store().ClaimAsync(
            "web-02:4242:a1b2c3", 100, TimeSpan.FromSeconds(60), TimeProvider.System.GetUtcNow(), token);

        next.Count.ShouldBe(25);
        next.ShouldAllBe(delivery => delivery.AttemptCount == 1);
    }

    private static async Task UntilAsync(Func<Task<bool>> condition, string what, CancellationToken token)
    {
        Stopwatch waited = Stopwatch.StartNew();

        while (!await condition())
        {
            if (waited.Elapsed > Patience)
            {
                throw new TimeoutException($"Waited {Patience} for {what}.");
            }

            await Task.Delay(25, token);
        }
    }

    private SessionPerCallLeaseStore Store()
    {
        return new SessionPerCallLeaseStore(_contextOptions);
    }

    private async Task<int> CountHeldAsync(string leaseOwner, CancellationToken token)
    {
        await using HookwrightDbContext context = new(_contextOptions);

        return await context.Set<Delivery>().CountAsync(
            delivery => delivery.State == DeliveryState.InFlight && delivery.LeaseOwner == leaseOwner, token);
    }

    private async Task<int> CountAsync(DeliveryState state, CancellationToken token)
    {
        await using HookwrightDbContext context = new(_contextOptions);

        return await context.Set<Delivery>().CountAsync(delivery => delivery.State == state, token);
    }

    private async Task SeedAsync(int count, CancellationToken token)
    {
        await using HookwrightDbContext context = new(_contextOptions);

        Subscriber subscriber = Subscriber.Create($"org_{Guid.CreateVersion7():N}", null, Seeded);
        await new EfSubscriberStore(context).AddAsync(subscriber, token);

        WebhookEndpoint endpoint = WebhookEndpoint.Create(subscriber.Id, new Uri("https://example.com/"), null, Seeded);
        await new EfEndpointStore(context).AddAsync(
            EndpointRegistration.Create(
                endpoint, [EndpointSecret.Create(endpoint.Id, "protected-key", SignatureAlgorithm.HmacSha256, Seeded)]),
            token);

        EfEventStore events = new(context);

        for (int i = 0; i < count; i++)
        {
            WebhookEvent published = WebhookEvent.Create(subscriber.Id, "order.created", $$"""{"index":{{i}}}""", Seeded);
            events.Append(EventPublication.Create(published, [Delivery.Create(published.Id, endpoint.Id, null, Seeded)]));
        }

        await events.CommitAsync(token);
    }
}