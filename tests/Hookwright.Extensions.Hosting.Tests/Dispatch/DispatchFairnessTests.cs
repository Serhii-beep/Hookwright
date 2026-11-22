using System.Diagnostics;
using System.Threading.Channels;

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
public sealed class DispatchFairnessTests
{
    private const int Backlogged = 8;

    private const int Trickle = 5;

    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SlowAnswer = TimeSpan.FromMilliseconds(200);

    private static readonly TimeSpan BetweenEvents = TimeSpan.FromMilliseconds(130);

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly HmacSha256Signer Signer = new();

    private static readonly PlaintextSecretProtector Protector = new();

    private static readonly DeliveryOptions Options = new()
    {
        BatchSize = 50,
        MaxConcurrency = 16,
        MaxConnectionsPerEndpoint = 4,
        PollInterval = TimeSpan.FromMilliseconds(100),
        LeaseDuration = TimeSpan.FromSeconds(30),
        RequestTimeout = TimeSpan.FromSeconds(5)
    };

    private static readonly int Backlog = 100 * Options.BatchSize;

    [Fact]
    public async Task RunAsync_GivenBacklogsThatTakeEveryWorker_ServesWhatIsPublishedBehindThemWithinARound()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        Channel<long> arrivals = Channel.CreateUnbounded<long>();
        int[] inFlight = new int[Backlogged];
        int[] peak = new int[Backlogged];
        int slowReceived = 0;

        await using SqliteDispatchDatabase database = await SqliteDispatchDatabase.CreateAsync(token);
        List<LoopbackEndpoint> servers = [];

        try
        {
            for (int i = 0; i < Backlogged; i++)
            {
                int endpoint = i;
                LoopbackEndpoint slow = await LoopbackEndpoint.StartAsync(async context =>
                {
                    Interlocked.Increment(ref slowReceived);
                    RaiseTo(ref peak[endpoint], Interlocked.Increment(ref inFlight[endpoint]));
                    await Task.Delay(SlowAnswer, context.RequestAborted);
                    Interlocked.Decrement(ref inFlight[endpoint]);
                });
                servers.Add(slow);
                await database.SeedAsync(slow.Url, Backlog / Backlogged, WebhookSecret.Generate(), Seeded, _ => "{}", token);
            }

            LoopbackEndpoint quick = await LoopbackEndpoint.StartAsync(_ =>
            {
                arrivals.Writer.TryWrite(Stopwatch.GetTimestamp());

                return Task.CompletedTask;
            });
            servers.Add(quick);
            WebhookEndpoint fast = await database.SeedAsync(quick.Url, 0, WebhookSecret.Generate(), Seeded, _ => "{}", token);

            using MetricProbe deferred = new("hookwright.deliveries.deferred");
            SsrfGuard guard = Guards.Development();
            using SocketsHttpHandler handler = DeliveryHttpHandler.Create(Options, guard);
            using HttpDeliveryTransport transport = new(handler, Signer, Protector, Options, new RetentionOptions());
            DispatchPipeline pipeline = CreatePipeline(database, transport);
            using CancellationTokenSource stopping = new();

            Task run = pipeline.RunAsync(stopping.Token);

            await UntilAsync(() => Volatile.Read(ref slowReceived) >= 2 * Options.MaxConcurrency, "the backlogs to be under way", token);
            List<TimeSpan> waits = [];

            for (int i = 0; i < Trickle; i++)
            {
                long published = Stopwatch.GetTimestamp();
                await database.PublishAsync(fast, TimeProvider.System.GetUtcNow(), token);
                long arrived = await arrivals.Reader.ReadAsync(token).AsTask().WaitAsync(Patience, token);
                waits.Add(Stopwatch.GetElapsedTime(published, arrived));

                await Task.Delay(BetweenEvents, token);
            }

            int done = await database.CountAsync(DeliveryState.Succeeded, token) - Trickle;

            await stopping.CancelAsync();
            await run;

            waits.Sort();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Behind {Backlogged} backlogs of {Backlog / Backlogged}, which between them take every worker, {Trickle} deliveries " +
                $"waited {waits[0].TotalMilliseconds:F0} to {waits[^1].TotalMilliseconds:F0} ms, {waits[Trickle / 2].TotalMilliseconds:F0} ms " +
                $"at the median, with a request taking {SlowAnswer.TotalMilliseconds:F0} ms. {done} of the {Backlog} were done by then; " +
                $"at most {peak.Max()} requests were in flight to one endpoint, and {deferred.Total} deliveries were deferred.");

            waits[^1].ShouldBeLessThan(TimeSpan.FromSeconds(5));
            done.ShouldBeLessThan(Backlog / 4);
            peak.Max().ShouldBeLessThanOrEqualTo(Options.MaxConnectionsPerEndpoint);
            (await database.CountAsync(DeliveryState.Succeeded, fast.Id, token)).ShouldBe(Trickle);
            (await database.CountAsync(DeliveryState.InFlight, token)).ShouldBe(0);
        }
        finally
        {
            foreach (LoopbackEndpoint server in servers)
            {
                await server.DisposeAsync();
            }
        }
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

    private static void RaiseTo(ref int peak, int seen)
    {
        int current = Volatile.Read(ref peak);

        while (current < seen)
        {
            int previous = Interlocked.CompareExchange(ref peak, seen, current);

            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }

    private static async Task UntilAsync(Func<bool> condition, string what, CancellationToken token)
    {
        Stopwatch waited = Stopwatch.StartNew();

        while (!condition())
        {
            if (waited.Elapsed > Patience)
            {
                throw new TimeoutException($"Waited {Patience} for {what}.");
            }

            await Task.Delay(25, token);
        }
    }
}