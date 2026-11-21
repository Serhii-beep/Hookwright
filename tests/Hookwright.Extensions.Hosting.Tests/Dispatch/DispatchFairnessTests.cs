using System.Diagnostics;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Transport;
using Hookwright.Dispatch;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;
using Hookwright.Signing;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

[Collection("Dispatch")]
public sealed class DispatchFairnessTests
{
    private const int Backlog = 2_000;

    private const int Trickle = 20;

    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan SlowAnswer = TimeSpan.FromMilliseconds(200);

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

    [Fact]
    public async Task RunAsync_GivenOneEndpointsBacklogAheadOfEverythingElse_StillServesTheOthersAtOnce()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        int slowInFlight = 0;
        int slowPeak = 0;
        int fastReceived = 0;

        await using LoopbackEndpoint server = await LoopbackEndpoint.StartAsync(async context =>
        {
            if (context.Request.Path == "/fast")
            {
                Interlocked.Increment(ref fastReceived);

                return;
            }

            RaiseTo(ref slowPeak, Interlocked.Increment(ref slowInFlight));
            await Task.Delay(SlowAnswer, context.RequestAborted);
            Interlocked.Decrement(ref slowInFlight);
        });

        await using SqliteDispatchDatabase database = await SqliteDispatchDatabase.CreateAsync(token);
        WebhookEndpoint slow = await database.SeedAsync(new Uri(server.Url, "/slow"), Backlog, WebhookSecret.Generate(), Seeded, _ => "{}", token);
        WebhookEndpoint fast = await database.SeedAsync(new Uri(server.Url, "/fast"), Trickle, WebhookSecret.Generate(), Seeded.AddSeconds(1), _ => "{}", token);

        using MetricProbe deferred = new("hookwright.deliveries.deferred");
        SsrfGuard guard = Guards.Development();
        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(Options, guard);
        using HttpDeliveryTransport transport = new(handler, Signer, Protector, Options, new RetentionOptions());
        DispatchPipeline pipeline = CreatePipeline(database, transport);
        using CancellationTokenSource stopping = new();

        Stopwatch clock = Stopwatch.StartNew();
        Task run = pipeline.RunAsync(stopping.Token);

        await UntilAsync(() => Volatile.Read(ref fastReceived) == Trickle, "the trickle to get through the backlog", token);
        TimeSpan trickleServedIn = clock.Elapsed;
        int slowSucceeded = await database.CountAsync(DeliveryState.Succeeded, slow.Id, token);

        await stopping.CancelAsync();
        await run;

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"The trickle of {Trickle} was served in {trickleServedIn.TotalMilliseconds:F0} ms, with {slowSucceeded} of the " +
            $"backlog of {Backlog} done; at most {Volatile.Read(ref slowPeak)} requests were in flight to the slow endpoint, " +
            $"and {deferred.Total} deliveries were deferred.");

        trickleServedIn.ShouldBeLessThan(TimeSpan.FromSeconds(10));
        slowSucceeded.ShouldBeLessThan(Backlog / 4);
        Volatile.Read(ref slowPeak).ShouldBeLessThanOrEqualTo(Options.MaxConnectionsPerEndpoint);
        (await database.CountAsync(DeliveryState.Succeeded, fast.Id, token)).ShouldBe(Trickle);
        (await database.CountAsync(DeliveryState.InFlight, token)).ShouldBe(0);
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