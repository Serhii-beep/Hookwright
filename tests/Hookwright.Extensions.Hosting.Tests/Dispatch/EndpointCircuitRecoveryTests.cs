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
public sealed class EndpointCircuitRecoveryTests
{
    private const int Backlog = 100;

    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly HmacSha256Signer Signer = new();

    private static readonly PlaintextSecretProtector Protector = new();

    private static readonly DeliveryOptions Options = new()
    {
        BatchSize = 20,
        MaxConcurrency = 8,
        MaxConnectionsPerEndpoint = 4,
        PollInterval = TimeSpan.FromMilliseconds(100),
        LeaseDuration = TimeSpan.FromSeconds(30),
        RequestTimeout = TimeSpan.FromSeconds(5)
    };

    private static readonly CircuitOptions Circuit = new()
    {
        Cooldown = TimeSpan.FromMilliseconds(300),
        DisableAfter = TimeSpan.FromDays(1)
    };

    [Fact]
    public async Task RunAsync_GivenAnEndpointThatFailsThenRecovers_OpensTheCircuitProbesItAndDeliversEverything()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        int status = 500;
        int failures = 0;
        int successes = 0;

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(context =>
        {
            int answer = Volatile.Read(ref status);
            context.Response.StatusCode = answer;

            if (answer == 200)
            {
                Interlocked.Increment(ref successes);
            }
            else
            {
                Interlocked.Increment(ref failures);
            }

            return Task.CompletedTask;
        });

        await using SqliteDispatchDatabase database = await SqliteDispatchDatabase.CreateAsync(token);
        WebhookEndpoint registered = await database.SeedAsync(endpoint.Url, Backlog, WebhookSecret.Generate(), Seeded, _ => "{}", token);

        using MetricProbe opened = new("hookwright.circuits.opened");
        using MetricProbe closed = new("hookwright.circuits.closed");
        using MetricProbe deferred = new("hookwright.deliveries.deferred");
        SsrfGuard guard = Guards.Development();
        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(Options, guard);
        using HttpDeliveryTransport transport = new(handler, Signer, Protector, Options, new RetentionOptions());
        DispatchPipeline pipeline = CreatePipeline(database, transport);
        using CancellationTokenSource stopping = new();

        Task run = pipeline.RunAsync(stopping.Token);

        await UntilAsync(
            async () => (await database.FindEndpointAsync(registered.Id, token)).Health == EndpointHealth.Failing,
            "the store to show the circuit open",
            token);
        int failedBeforeTheFix = Volatile.Read(ref failures);
        Volatile.Write(ref status, 200);

        await UntilAsync(
            async () => await database.CountAsync(DeliveryState.Succeeded, token) == Backlog,
            "the backlog to drain once the endpoint recovered",
            token);

        await stopping.CancelAsync();
        await run;

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{failedBeforeTheFix} requests failed before the circuit opened, {Volatile.Read(ref failures)} in all; " +
            $"{deferred.Total} deliveries were deferred, the circuit opened {opened.Total} time(s) and closed {closed.Total}.");

        Volatile.Read(ref failures).ShouldBeInRange(EndpointCircuit.OpenAfter, 3 * EndpointCircuit.OpenAfter);
        Volatile.Read(ref successes).ShouldBe(Backlog);
        opened.Total.ShouldBeGreaterThanOrEqualTo(1);
        closed.Total.ShouldBeGreaterThanOrEqualTo(1);
        deferred.Total.ShouldBeGreaterThanOrEqualTo(1);
        WebhookEndpoint recovered = await database.FindEndpointAsync(registered.Id, token);
        recovered.Health.ShouldBe(EndpointHealth.Healthy);
        recovered.ConsecutiveFailures.ShouldBe(0);
        recovered.FailingSince.ShouldBeNull();
        (await database.CountAsync(DeliveryState.Pending, token)).ShouldBe(0);
        (await database.CountAsync(DeliveryState.InFlight, token)).ShouldBe(0);
    }

    private static DispatchPipeline CreatePipeline(SqliteDispatchDatabase database, IDeliveryTransport transport)
    {
        SessionPerCallLeaseStore store = new(database.Options);
        EndpointCircuits circuits = new(Options, Circuit);
        RetrySchedule quickly = new(Enumerable.Repeat(TimeSpan.FromMilliseconds(200), 20), jitterFactor: 0);
        DeliveryClaimer claimer = new(store, Options, circuits, Worker, TimeProvider.System, NullLogger<DeliveryClaimer>.Instance);
        DeliveryWorker worker = new(
            transport,
            new DefaultRetryPolicy(quickly, Random.Shared),
            circuits,
            Options,
            new RetentionOptions(),
            Worker,
            TimeProvider.System,
            NullLogger<DeliveryWorker>.Instance);
        CompletionWriter writer = new(store, NullEndpointNotifier.Instance, Options, Worker, TimeProvider.System, NullLogger<CompletionWriter>.Instance);

        return new DispatchPipeline(claimer, worker, writer, Options);
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

            await Task.Delay(50, token);
        }
    }
}