using System.Diagnostics;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Transport;
using Hookwright.Dispatch;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;
using Hookwright.Signing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

[Collection("Dispatch")]
public sealed class DispatchLoadTests : IAsyncLifetime
{
    private const int Backlog = 10_000;

    private const int PayloadLength = 8 * 1024;

    private const long HeapHeadroom = 32L * 1024 * 1024;

    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan SamplePeriod = TimeSpan.FromMilliseconds(250);

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

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

    private SqliteDispatchDatabase? _database;

    public async ValueTask InitializeAsync()
    {
        _database = await SqliteDispatchDatabase.CreateAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_database is not null)
        {
            await _database.DisposeAsync();
        }
    }

    [Fact]
    public async Task RunAsync_GivenTenThousandDeliveries_SendsEachOnceSignedAndKeepsTheHeapFlat()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        SqliteDispatchDatabase database = _database.ShouldNotBeNull();
        WebhookSecret secret = WebhookSecret.Generate();
        WebhookVerifier verifier = new(Signer);
        string padding = new('x', PayloadLength);
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

        await database.SeedAsync(endpoint.Url, Backlog, secret, Seeded, i => $$"""{"index":{{i}},"padding":"{{padding}}"}""", token);

        using MetricProbe stale = new("hookwright.deliveries.stale");
        using MetricProbe late = new("hookwright.completions.late");
        using MetricProbe lost = new("hookwright.completions.lost");
        SsrfGuard guard = Guards.Development();
        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(Options, guard);
        using HttpDeliveryTransport transport = new(handler, Signer, Protector, Options, new RetentionOptions());
        DispatchPipeline pipeline = CreatePipeline(database, transport);
        using CancellationTokenSource stopping = new();
        List<long> heap = [];

        long baseline = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch clock = Stopwatch.StartNew();
        Task run = pipeline.RunAsync(stopping.Token);

        while (await database.CountAsync(DeliveryState.Succeeded, token) < Backlog)
        {
            if (clock.Elapsed > Patience)
            {
                throw new TimeoutException(
                    $"Waited {Patience} for the backlog to drain; {await database.CountAsync(DeliveryState.Succeeded, token)} succeeded.");
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
        (await database.CountAsync(DeliveryState.InFlight, token)).ShouldBe(0);
        (await database.CountAsync(DeliveryState.Pending, token)).ShouldBe(0);
        (await database.CountAttemptsAsync(token)).ShouldBe((Backlog, Backlog));
        stale.Total.ShouldBe(0);
        late.Total.ShouldBe(0);
        lost.Total.ShouldBe(0);
        (heap.Max() - baseline).ShouldBeLessThan(HeapHeadroom);
    }

    private static DispatchPipeline CreatePipeline(SqliteDispatchDatabase database, IDeliveryTransport transport)
    {
        SessionPerCallLeaseStore store = new(database.Options);
        EndpointCircuits circuits = new(Options, new CircuitOptions());
        DeliveryClaimer claimer = new(store, Options, circuits, Worker, TimeProvider.System, NullLogger<DeliveryClaimer>.Instance);
        DeliveryWorker worker = new(
            transport,
            new DefaultRetryPolicy(RetrySchedule.Default, Random.Shared),
            circuits,
            Options,
            new RetentionOptions(),
            Worker,
            TimeProvider.System,
            NullLogger<DeliveryWorker>.Instance);
        CompletionWriter writer = new(store, NullEndpointNotifier.Instance, Options, Worker, TimeProvider.System, NullLogger<CompletionWriter>.Instance);

        return new DispatchPipeline(claimer, worker, writer, Options);
    }
}