using Hookwright.Core.Configuration;
using Hookwright.Core.Storage;
using Hookwright.Dispatch;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

[Collection("Dispatch")]
public sealed class LeaseReaperTests
{
    private static readonly DeliveryOptions Options = new()
    {
        LeaseDuration = TimeSpan.FromSeconds(8),
        RequestTimeout = TimeSpan.FromSeconds(1)
    };

    [Fact]
    public void Period_Always_IsAQuarterOfTheLease()
    {
        new LeaseReaper(new RecordingLeaseStore(), Options, new SignallingTimeProvider(), new FakeLogger<LeaseReaper>())
            .Period.ShouldBe(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task RunAsync_Always_ReapsAtOnceAndThenEveryPeriodOnTheClock()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        LeaseReaper reaper = new(store, Options, time, new FakeLogger<LeaseReaper>());
        using CancellationTokenSource stopping = new();

        Task run = reaper.RunAsync(stopping.Token);

        (await store.NextReapAsync(token)).ShouldBe(start);

        time.Advance(TimeSpan.FromSeconds(2));
        (await store.NextReapAsync(token)).ShouldBe(start.AddSeconds(2));

        time.Advance(TimeSpan.FromSeconds(2));
        (await store.NextReapAsync(token)).ShouldBe(start.AddSeconds(4));

        await stopping.CancelAsync();
        await run;

        store.Reaps.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ReapAsync_GivenDroppedLeases_NamesEachWorkerAndCountsThem()
    {
        RecordingLeaseStore store = new();
        store.ReapAnswers.Enqueue([new ReclaimedLeases("web-01:4242:a1b2c3", 2), new ReclaimedLeases("web-02:4242:d4e5f6", 1)]);
        FakeLogger<LeaseReaper> logger = new();
        LeaseReaper reaper = new(store, Options, new SignallingTimeProvider(), logger);
        using MetricProbe reclaimed = new("hookwright.leases.reclaimed");

        IReadOnlyList<ReclaimedLeases> result = await reaper.ReapAsync(TestContext.Current.CancellationToken);

        result.Count.ShouldBe(2);
        reclaimed.Total.ShouldBe(3);

        IReadOnlyList<FakeLogRecord> records = logger.Collector.GetSnapshot();
        records.Count.ShouldBe(2);
        records.ShouldAllBe(record => record.Level == LogLevel.Warning);
        records[0].Message.ShouldBe("Reclaimed 2 deliveries whose lease lapsed under web-01:4242:a1b2c3");
        records[1].Message.ShouldBe("Reclaimed 1 deliveries whose lease lapsed under web-02:4242:d4e5f6");
    }

    [Fact]
    public async Task ReapAsync_GivenNothingLapsed_SaysNothing()
    {
        FakeLogger<LeaseReaper> logger = new();
        LeaseReaper reaper = new(new RecordingLeaseStore(), Options, new SignallingTimeProvider(), logger);

        (await reaper.ReapAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();

        logger.Collector.Count.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_GivenAStoreThatFails_LogsTheFailureAndTriesAgainNextPeriod()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        InvalidOperationException failure = new("the database went away");
        RecordingLeaseStore store = new() { NextReapFailure = failure };
        SignallingTimeProvider time = new();
        FakeLogger<LeaseReaper> logger = new();
        LeaseReaper reaper = new(store, Options, time, logger);
        using CancellationTokenSource stopping = new();

        Task run = reaper.RunAsync(stopping.Token);

        await store.NextReapAsync(token);
        time.Advance(TimeSpan.FromSeconds(2));
        await store.NextReapAsync(token);

        await stopping.CancelAsync();
        await run;

        FakeLogRecord record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Error);
        record.Exception.ShouldBeSameAs(failure);
        record.Message.ShouldBe("Could not reclaim expired leases. The next attempt is in 00:00:02");
    }
}