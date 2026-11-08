using Hookwright.Core.Configuration;
using Hookwright.Core.Storage;
using Hookwright.Dispatch;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

[Collection("Dispatch")]
public sealed class CompletionWriterTests
{
    private const string Worker = "web-01:4242:a1b2c3";

    [Fact]
    public async Task RunAsync_GivenAFullBatch_WritesItWithoutWaiting()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        CompletionWriter writer = new(store, Options(batchSize: 3), Worker, time, new FakeLogger<CompletionWriter>());

        Task run = writer.RunAsync();

        for (int i = 0; i < 3; i++)
        {
            await writer.Completions.WriteAsync(Completion(time), token);
        }

        RecordingLeaseStore.CompleteCall call = await store.NextCompleteAsync(token);
        call.LeaseOwner.ShouldBe(Worker);
        call.Completions.Count.ShouldBe(3);

        writer.Complete();
        await run;
    }

    [Fact]
    public async Task RunAsync_GivenAPartialBatch_WaitsTheLingerAndThenWritesIt()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        CompletionWriter writer = new(store, Options(batchSize: 10), Worker, time, new FakeLogger<CompletionWriter>());

        Task run = writer.RunAsync();
        await writer.Completions.WriteAsync(Completion(time), token);

        await time.TimerCreatedAsync(token);
        store.Completions.ShouldBeEmpty();

        time.Advance(CompletionWriter.Linger);
        (await store.NextCompleteAsync(token)).Completions.Count.ShouldBe(1);

        writer.Complete();
        await run;
    }

    [Fact]
    public async Task RunAsync_GivenMoreResultsDuringTheLinger_TakesThemIntoTheSameBatch()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        CompletionWriter writer = new(store, Options(batchSize: 10), Worker, time, new FakeLogger<CompletionWriter>());

        Task run = writer.RunAsync();
        await writer.Completions.WriteAsync(Completion(time), token);
        await time.TimerCreatedAsync(token);

        await writer.Completions.WriteAsync(Completion(time), token);
        await writer.Completions.WriteAsync(Completion(time), token);
        time.Advance(CompletionWriter.Linger);

        (await store.NextCompleteAsync(token)).Completions.Count.ShouldBe(3);

        writer.Complete();
        await run;
    }

    [Fact]
    public async Task RunAsync_GivenResultsTheStoreDropped_CountsThemAndSaysSo()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new() { CompleteShortfall = 1 };
        FakeLogger<CompletionWriter> logger = new();
        SignallingTimeProvider time = new();
        CompletionWriter writer = new(store, Options(batchSize: 3), Worker, time, logger);
        using MetricProbe late = new("hookwright.completions.late");

        Task run = writer.RunAsync();

        for (int i = 0; i < 3; i++)
        {
            await writer.Completions.WriteAsync(Completion(time), token);
        }

        await store.NextCompleteAsync(token);
        writer.Complete();
        await run;

        late.Total.ShouldBe(1);
        FakeLogRecord record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.Message.ShouldBe($"{Worker} recorded 2 of 3 results. The rest were for leases it no longer held");
    }

    [Fact]
    public async Task RunAsync_GivenAStoreThatFails_ReportsTheLossAndGoesOn()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        InvalidOperationException failure = new("the database went away");
        RecordingLeaseStore store = new() { NextCompleteFailure = failure };
        FakeLogger<CompletionWriter> logger = new();
        SignallingTimeProvider time = new();
        CompletionWriter writer = new(store, Options(batchSize: 2), Worker, time, logger);
        using MetricProbe lost = new("hookwright.completions.lost");

        Task run = writer.RunAsync();

        await writer.Completions.WriteAsync(Completion(time), token);
        await writer.Completions.WriteAsync(Completion(time), token);
        await store.NextCompleteAsync(token);

        await writer.Completions.WriteAsync(Completion(time), token);
        await writer.Completions.WriteAsync(Completion(time), token);
        await store.NextCompleteAsync(token);

        writer.Complete();
        await run;

        store.Completions.Count.ShouldBe(2);
        lost.Total.ShouldBe(2);
        FakeLogRecord record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Error);
        record.Exception.ShouldBeSameAs(failure);
        record.Message.ShouldBe($"{Worker} could not record 2 results. Their leases will lapse and the deliveries be retried");
    }

    [Fact]
    public async Task Complete_Always_LetsRunAsyncFinishOnceEverythingIsWritten()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        CompletionWriter writer = new(store, Options(batchSize: 10), Worker, time, new FakeLogger<CompletionWriter>());

        Task run = writer.RunAsync();
        await writer.Completions.WriteAsync(Completion(time), token);
        await writer.Completions.WriteAsync(Completion(time), token);

        writer.Complete();
        await run;

        store.Completions.ShouldHaveSingleItem().Completions.Count.ShouldBe(2);
    }

    private static DeliveryOptions Options(int batchSize)
    {
        return new DeliveryOptions
        {
            BatchSize = batchSize,
            PollInterval = TimeSpan.FromSeconds(1),
            LeaseDuration = TimeSpan.FromSeconds(60),
            RequestTimeout = TimeSpan.FromSeconds(1)
        };
    }

    private static DeliveryCompletion Completion(FakeTimeProvider time)
    {
        return TestDeliveries.Completed(TestDeliveries.Claimed(time.GetUtcNow() + TimeSpan.FromSeconds(60)), time.GetUtcNow());
    }
}