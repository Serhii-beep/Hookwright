using System.Diagnostics;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.Core.Transport;
using Hookwright.Dispatch;
using Hookwright.EntityFrameworkCore;
using Hookwright.EntityFrameworkCore.Stores;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;
using Hookwright.Signing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

[Collection("Dispatch")]
public sealed class DispatchLoadTests : IAsyncLifetime
{
    private const int Backlog = 10_000;

    private const int PayloadLength = 8 * 1024;

    private const int SeedBatch = 1_000;

    private const long HeapHeadroom = 32L * 1024 * 1024;

    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan SamplePeriod = TimeSpan.FromMilliseconds(250);

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] SqliteSuffixes = ["", "-journal", "-wal", "-shm"];

    private static readonly HmacSha256Signer Signer = new();

    private static readonly PlaintextSecretProtector Protector = new();

    private static readonly DeliveryOptions Options = new()
    {
        BatchSize = 100,
        MaxConcurrency = 64,
        MaxConnectionsPerEndpoint = 64,
        PollInterval = TimeSpan.FromSeconds(1),
        LeaseDuration = TimeSpan.FromMinutes(5),
        RequestTimeout = TimeSpan.FromSeconds(10)
    };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"hookwright-load-{Guid.CreateVersion7():N}.db");
    private readonly DbContextOptions<HookwrightDbContext> _contextOptions;

    public DispatchLoadTests()
    {
        _contextOptions = new DbContextOptionsBuilder<HookwrightDbContext>().UseSqlite($"Data Source = {_path}").Options;
    }

    public async ValueTask InitializeAsync()
    {
        await using HookwrightDbContext context = new(_contextOptions);
        await context.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                foreach (string suffix in SqliteSuffixes)
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

    [Fact]
    public async Task RunAsync_GivenTenThousandDeliveries_SendsEachOnceSignedAndKeepsTheHeapFlat()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        WebhookSecret secret = WebhookSecret.Generate();
        WebhookVerifier verifier = new(Signer);
        int received = 0;
        int verified = 0;

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            ReceivedRequest request = await ReceivedRequest.ReadAsync(context.Request);
            Interlocked.Increment(ref received);

            if (verifier.Verify(request.Headers, request.Body.Span, [secret], TimeProvider.System.GetUtcNow()).IsValid)
            {
                Interlocked.Increment(ref verified);
            }
        });

        await SeedAsync(endpoint.Url, secret, token);

        using MetricProbe stale = new("hookwright.deliveries.stale");
        using MetricProbe late = new("hookwright.completions.late");
        using MetricProbe lost = new("hookwright.completions.lost");
        SsrfGuard guard = Guards.Development();
        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(Options, guard);
        using HttpDeliveryTransport transport = new(handler, Signer, Protector, Options, new RetentionOptions());
        DispatchPipeline pipeline = CreatePipeline(transport);
        using CancellationTokenSource stopping = new();
        List<long> heap = [];

        long baseline = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch clock = Stopwatch.StartNew();
        Task run = pipeline.RunAsync(stopping.Token);

        while (await CountAsync(DeliveryState.Succeeded, token) < Backlog)
        {
            if (clock.Elapsed > Patience)
            {
                throw new TimeoutException(
                    $"Waited {Patience} for the backlog to drain; {await CountAsync(DeliveryState.Succeeded, token)} succeeded.");
            }

            await Task.Delay(SamplePeriod, token);
            heap.Add(GC.GetTotalMemory(forceFullCollection: true));
        }

        clock.Stop();
        await stopping.CancelAsync();
        await run;
        heap.Add(GC.GetTotalMemory(forceFullCollection: true));

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{Backlog} deliveries in {clock.Elapsed.TotalSeconds:F1} s, {Backlog / clock.Elapsed.TotalSeconds:F0}/s." +
            $"Heap {baseline >> 20} MB before the run, {heap.Min() >> 20}-{heap.Max() >> 20} MB during it, over {heap.Count} samples.");

        Volatile.Read(ref received).ShouldBe(Backlog);
        Volatile.Read(ref verified).ShouldBe(Backlog);
        (await CountAsync(DeliveryState.InFlight, token)).ShouldBe(0);
        (await CountAsync(DeliveryState.Pending, token)).ShouldBe(0);
        (await CountAttemptsAsync(token)).ShouldBe((Backlog, Backlog));
        stale.Total.ShouldBe(0);
        late.Total.ShouldBe(0);
        lost.Total.ShouldBe(0);
        (heap.Max() - baseline).ShouldBeLessThan(HeapHeadroom);
    }

    private DispatchPipeline CreatePipeline(IDeliveryTransport transport)
    {
        SessionPerCallLeaseStore store = new(_contextOptions);
        DeliveryClaimer claimer = new(store, Options, Worker, TimeProvider.System, NullLogger<DeliveryClaimer>.Instance);
        DeliveryWorker worker = new(
            transport,
            new DefaultRetryPolicy(RetrySchedule.Default, Random.Shared),
            Options,
            new RetentionOptions(),
            Worker,
            TimeProvider.System,
            NullLogger<DeliveryWorker>.Instance);
        CompletionWriter writer = new(store, Options, Worker, TimeProvider.System, NullLogger<CompletionWriter>.Instance);

        return new DispatchPipeline(claimer, worker, writer, Options);
    }

    private async Task<int> CountAsync(DeliveryState state, CancellationToken token)
    {
        await using HookwrightDbContext context = new(_contextOptions);

        return await context.Set<Delivery>().CountAsync(delivery => delivery.State == state, token);
    }

    private async Task<(int Total, int Succeeded)> CountAttemptsAsync(CancellationToken token)
    {
        await using HookwrightDbContext context = new(_contextOptions);
        IQueryable<DeliveryAttempt> attempts = context.Set<DeliveryAttempt>();

        return (
            await attempts.CountAsync(token),
            await attempts.CountAsync(attempt => attempt.Outcome == AttemptOutcome.Succeeded && attempt.ResponseStatusCode == 200, token));
    }

    private async Task SeedAsync(Uri url, WebhookSecret secret, CancellationToken token)
    {
        Subscriber subscriber = Subscriber.Create($"org_{Guid.CreateVersion7():N}", null, Seeded);
        WebhookEndpoint endpoint = WebhookEndpoint.Create(subscriber.Id, url, null, Seeded);
        string padding = new('x', PayloadLength);

        await using (HookwrightDbContext context = new(_contextOptions))
        {
            await new EfSubscriberStore(context).AddAsync(subscriber, token);
            await new EfEndpointStore(context).AddAsync(
                EndpointRegistration.Create(
                    endpoint, [EndpointSecret.Create(endpoint.Id, Protector.Protect(secret), SignatureAlgorithm.HmacSha256, Seeded)]),
                token);
        }

        for (int first = 0; first < Backlog; first += SeedBatch)
        {
            await using HookwrightDbContext context = new(_contextOptions);
            EfEventStore events = new(context);

            for (int i = first; i < first + SeedBatch; i++)
            {
                WebhookEvent published = WebhookEvent.Create(
                    subscriber.Id, "order.created", $$"""{"index":{{i}},"padding":"{{padding}}"}""", Seeded);
                events.Append(EventPublication.Create(published, [Delivery.Create(published.Id, endpoint.Id, null, Seeded)]));
            }

            await events.CommitAsync(token);
        }
    }
}