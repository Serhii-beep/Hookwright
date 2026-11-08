using System.Diagnostics;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Dispatch;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

using Microsoft.Extensions.Logging.Testing;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

[Collection("Dispatch")]
public sealed class DispatchPipelineTests
{
    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task RunAsync_GivenABacklog_AttemptsAllOfItAndRecordsEveryResultBeforeReleasing()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        ScriptedTransport transport = new();
        store.MakeDue(25, TimeProvider.System.GetUtcNow() + Lease);
        DispatchPipeline pipeline = CreatePipeline(store, transport, Options(batchSize: 10, concurrency: 3));
        using CancellationTokenSource stopping = new();

        Task run = pipeline.RunAsync(stopping.Token);

        await UntilAsync(() => store.Recorded == 25, "every result to be recorded", token);
        await stopping.CancelAsync();
        await run;

        transport.SentCount.ShouldBe(25);
        store.Completions.SelectMany(call => call.Completions).ShouldAllBe(completion => completion.State == DeliveryState.Succeeded);
        store.Completions.ShouldAllBe(call => call.LeaseOwner == Worker);
        store.Releases.ShouldHaveSingleItem().LeaseOwner.ShouldBe(Worker);
        store.Calls.Last().ShouldBe("release");
    }

    [Fact]
    public async Task RunAsync_Always_RunsNoMoreAttemptsAtOnceThanTheConcurrency()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedTransport transport = new() { Gate = gate.Task };
        store.MakeDue(20, TimeProvider.System.GetUtcNow() + Lease);
        DispatchPipeline pipeline = CreatePipeline(store, transport, Options(batchSize: 10, concurrency: 4));
        using CancellationTokenSource stopping = new();

        Task run = pipeline.RunAsync(stopping.Token);

        for (int i = 0; i < 4; i++)
        {
            await transport.NextSendAsync(token);
        }

        await Task.Delay(100, token);
        transport.MaxInFlight.ShouldBe(4);

        gate.SetResult();
        await UntilAsync(() => store.Recorded == 20, "every result to be recorded", token);
        await stopping.CancelAsync();
        await run;

        transport.MaxInFlight.ShouldBe(4);
    }

    [Fact]
    public async Task RunAsync_GivenAStoreThatStopsAnswering_StopsClaimingInsteadOfBuffering()
    {
        const int buffered = 20 + 20 + 2 + 10;
        CancellationToken token = TestContext.Current.CancellationToken;
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingLeaseStore store = new() { CompleteGate = gate.Task };
        ScriptedTransport transport = new();
        store.MakeDue(1000, TimeProvider.System.GetUtcNow() + Lease);
        DispatchPipeline pipeline = CreatePipeline(store, transport, Options(batchSize: 10, concurrency: 2));
        using CancellationTokenSource stopping = new();

        Task run = pipeline.RunAsync(stopping.Token);

        await UntilAsync(() => 1000 - store.Due >= buffered - 9, "the pipeline to fill up", token);
        await Task.Delay(300, token);
        int claimed = 1000 - store.Due;
        claimed.ShouldBeInRange(buffered - 9, buffered);

        gate.SetResult();
        await UntilAsync(() => store.Recorded == 1000, "the backlog to drain", token);
        await stopping.CancelAsync();
        await run;

        store.Due.ShouldBe(0);
        transport.SentCount.ShouldBe(1000);
    }

    [Fact]
    public async Task RunAsync_WhenStopped_RecordsTheAttemptInFlightBeforeReleasingTheRest()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedTransport transport = new() { Gate = gate.Task };
        store.MakeDue(5, TimeProvider.System.GetUtcNow() + Lease);
        DispatchPipeline pipeline = CreatePipeline(store, transport, Options(batchSize: 10, concurrency: 1));
        using CancellationTokenSource stopping = new();

        Task run = pipeline.RunAsync(stopping.Token);

        await transport.NextSendAsync(token);
        await stopping.CancelAsync();
        gate.SetResult();
        await run;

        transport.SentCount.ShouldBe(1);
        store.Recorded.ShouldBe(1);
        store.Releases.ShouldHaveSingleItem();
        store.Calls.Count(call => call == "complete").ShouldBe(1);
        store.Calls.IndexOf("complete").ShouldBeLessThan(store.Calls.IndexOf("release"));
        store.Calls.Last().ShouldBe("release");
    }

    private static DeliveryOptions Options(int batchSize, int concurrency)
    {
        return new DeliveryOptions
        {
            BatchSize = batchSize,
            MaxConcurrency = concurrency,
            PollInterval = TimeSpan.FromSeconds(1),
            LeaseDuration = Lease,
            RequestTimeout = TimeSpan.FromSeconds(1)
        };
    }

    private static DispatchPipeline CreatePipeline(RecordingLeaseStore store, ScriptedTransport transport, DeliveryOptions options)
    {
        DeliveryClaimer claimer = new(store, options, Worker, TimeProvider.System, new FakeLogger<DeliveryClaimer>());
        DeliveryWorker worker = new(
            transport,
            new DefaultRetryPolicy(RetrySchedule.Default, Random.Shared),
            options,
            new RetentionOptions(),
            Worker,
            TimeProvider.System,
            new FakeLogger<DeliveryWorker>());
        CompletionWriter writer = new(store, options, Worker, TimeProvider.System, new FakeLogger<CompletionWriter>());

        return new DispatchPipeline(claimer, worker, writer, options);
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