using Hookwright.Core.Configuration;
using Hookwright.Dispatch;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

[Collection("Dispatch")]
public sealed class DeliveryClaimerTests
{
    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    private static DeliveryOptions Options(int batchSize)
    {
        return new DeliveryOptions
        {
            BatchSize = batchSize,
            PollInterval = TimeSpan.FromSeconds(1),
            LeaseDuration = Lease,
            RequestTimeout = TimeSpan.FromSeconds(1)
        };
    }

    [Fact]
    public async Task RunAsync_GivenDueDeliveries_ClaimsThemUnderThisWorkersLeaseAndHandsThemOn()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        FakeLogger<DeliveryClaimer> logger = new();
        store.MakeDue(3, time.GetUtcNow() + Lease);
        DeliveryClaimer claimer = new(store, Options(batchSize: 10), Worker, time, logger);
        using MetricProbe claimed = new("hookwright.deliveries.claimed");
        using CancellationTokenSource stopping = new();

        Task run = claimer.RunAsync(stopping.Token);

        (await store.NextClaimAsync(token)).ShouldBe(new RecordingLeaseStore.ClaimCall(Worker, 10, Lease, time.GetUtcNow()));

        for (int i = 0; i < 3; i++)
        {
            (await claimer.Deliveries.ReadAsync(token)).AttemptCount.ShouldBe(1);
        }

        await stopping.CancelAsync();
        await run;

        claimed.Total.ShouldBe(3);
        FakeLogRecord record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Debug);
        record.Message.ShouldBe("web-01:4242:a1b2c3 claimed 3 deliveries");
    }

    [Fact]
    public async Task RunAsync_GivenAFullBatch_ClaimsAgainWithoutPausing()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        store.MakeDue(4, start + Lease);
        DeliveryClaimer claimer = new(store, Options(batchSize: 2), Worker, time, new FakeLogger<DeliveryClaimer>());
        using CancellationTokenSource stopping = new();

        Task run = claimer.RunAsync(stopping.Token);

        (await store.NextClaimAsync(token)).Now.ShouldBe(start);
        (await store.NextClaimAsync(token)).Now.ShouldBe(start);
        time.TimersCreated.ShouldBe(0);

        await stopping.CancelAsync();
        await run;
    }

    [Fact]
    public async Task RunAsync_GivenNothingDue_BacksOffFromTheFloorToThePollInterval()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        DeliveryClaimer claimer = new(store, Options(batchSize: 2), Worker, time, new FakeLogger<DeliveryClaimer>());
        using CancellationTokenSource stopping = new();

        Task run = claimer.RunAsync(stopping.Token);

        foreach (int pause in new[] { 100, 200, 400, 800, 1000, 1000 })
        {
            await store.NextClaimAsync(token);
            await time.TimerCreatedAsync(token);
            time.Advance(TimeSpan.FromMilliseconds(pause));
        }

        await store.NextClaimAsync(token);
        await stopping.CancelAsync();
        await run;

        store.Claims.Select(claim => (claim.Now - start).TotalMilliseconds).ShouldBe([0, 100, 300, 700, 1500, 2500, 3500]);
    }

    [Fact]
    public async Task RunAsync_GivenATrickle_PausesTheFloorAndOnlyThenBacksOff()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        store.MakeDue(1, start + Lease);
        DeliveryClaimer claimer = new(store, Options(batchSize: 2), Worker, time, new FakeLogger<DeliveryClaimer>());
        using CancellationTokenSource stopping = new();

        Task run = claimer.RunAsync(stopping.Token);

        foreach (int pause in new[] { 100, 100, 200 })
        {
            await store.NextClaimAsync(token);
            await time.TimerCreatedAsync(token);
            time.Advance(TimeSpan.FromMilliseconds(pause));
        }

        await store.NextClaimAsync(token);
        await stopping.CancelAsync();
        await run;

        store.Claims.Select(claim => (claim.Now - start).TotalMilliseconds).ShouldBe([0, 100, 200, 400]);
    }

    [Fact]
    public async Task RunAsync_GivenNoRoomDownstream_ClaimsNothingUntilThereIsAndThenOnlyWhatFits()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        store.MakeDue(10, time.GetUtcNow() + Lease);
        DeliveryClaimer claimer = new(store, Options(batchSize: 2), Worker, time, new FakeLogger<DeliveryClaimer>());
        using CancellationTokenSource stopping = new();

        Task run = claimer.RunAsync(stopping.Token);

        await store.NextClaimAsync(token);
        await store.NextClaimAsync(token);
        await Task.Delay(100, token);
        store.Claims.Count.ShouldBe(2);
        time.TimersCreated.ShouldBe(0);

        await claimer.Deliveries.ReadAsync(token);
        (await store.NextClaimAsync(token)).BatchSize.ShouldBe(1);

        await stopping.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Wake_GivenAnIdleClaimer_ClaimsAtOnceAndForgetsTheBackoff()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        RecordingLeaseStore store = new();
        SignallingTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        DeliveryClaimer claimer = new(store, Options(batchSize: 2), Worker, time, new FakeLogger<DeliveryClaimer>());
        using CancellationTokenSource stopping = new();

        Task run = claimer.RunAsync(stopping.Token);

        await store.NextClaimAsync(token);
        await time.TimerCreatedAsync(token);
        time.Advance(TimeSpan.FromMilliseconds(100));
        await store.NextClaimAsync(token);
        await time.TimerCreatedAsync(token);

        claimer.Wake();
        await store.NextClaimAsync(token);
        await time.TimerCreatedAsync(token);
        time.Advance(TimeSpan.FromMilliseconds(100));
        await store.NextClaimAsync(token);

        await stopping.CancelAsync();
        await run;

        store.Claims.Select(claim => (claim.Now - start).TotalMilliseconds).ShouldBe([0, 100, 100, 200]);
    }

    [Fact]
    public async Task RunAsync_GivenAClaimThatFails_LogsItAndBacksOffAsIfIdle()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        InvalidOperationException failure = new("the database went away");
        RecordingLeaseStore store = new() { NextClaimFailure = failure };
        SignallingTimeProvider time = new();
        DateTimeOffset start = time.GetUtcNow();
        FakeLogger<DeliveryClaimer> logger = new();
        DeliveryClaimer claimer = new(store, Options(batchSize: 2), Worker, time, logger);
        using CancellationTokenSource stopping = new();

        Task run = claimer.RunAsync(stopping.Token);

        await store.NextClaimAsync(token);
        await time.TimerCreatedAsync(token);
        time.Advance(TimeSpan.FromMilliseconds(100));
        await store.NextClaimAsync(token);

        await stopping.CancelAsync();
        await run;

        store.Claims.Select(claim => (claim.Now - start).TotalMilliseconds).ShouldBe([0, 100]);
        FakeLogRecord record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Error);
        record.Exception.ShouldBeSameAs(failure);
        record.Message.ShouldBe("web-01:4242:a1b2c3 could not claim deliveries. The next attempt is in 00:00:00.1000000");
    }

    [Fact]
    public async Task RunAsync_WhenStopped_CompletesDeliveries()
    {
        RecordingLeaseStore store = new();
        DeliveryClaimer claimer = new(store, Options(batchSize: 2), Worker, new SignallingTimeProvider(), new FakeLogger<DeliveryClaimer>());
        using CancellationTokenSource stopping = new();

        Task run = claimer.RunAsync(stopping.Token);
        await store.NextClaimAsync(TestContext.Current.CancellationToken);

        await stopping.CancelAsync();
        await run;

        claimer.Deliveries.Completion.IsCompletedSuccessfully.ShouldBeTrue();
    }

    [Fact]
    public async Task ReleaseAsync_GivenWorkStillHeld_HandsItBackAndSaysSo()
    {
        RecordingLeaseStore store = new() { ReleaseAnswer = 3 };
        SignallingTimeProvider time = new();
        FakeLogger<DeliveryClaimer> logger = new();
        DeliveryClaimer claimer = new(store, Options(batchSize: 2), Worker, time, logger);
        using MetricProbe released = new("hookwright.leases.released");

        (await claimer.ReleaseAsync(TestContext.Current.CancellationToken)).ShouldBe(3);

        store.Releases.ShouldHaveSingleItem().ShouldBe(new RecordingLeaseStore.ReleaseCall(Worker, time.GetUtcNow()));
        released.Total.ShouldBe(3);
        FakeLogRecord record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Information);
        record.Message.ShouldBe("web-01:4242:a1b2c3 handed back 3 deliveries it had not started");
    }

    [Fact]
    public async Task ReleaseAsync_GivenNothingHeld_SaysNothing()
    {
        FakeLogger<DeliveryClaimer> logger = new();
        DeliveryClaimer claimer = new(new RecordingLeaseStore(), Options(batchSize: 2), Worker, new SignallingTimeProvider(), logger);

        (await claimer.ReleaseAsync(TestContext.Current.CancellationToken)).ShouldBe(0);

        logger.Collector.Count.ShouldBe(0);
    }

    [Fact]
    public void Constructor_GivenNoWorkerId_Throws()
    {
        Should.Throw<ArgumentException>(() => new DeliveryClaimer(
            new RecordingLeaseStore(), Options(batchSize: 2), " ", new SignallingTimeProvider(), new FakeLogger<DeliveryClaimer>()));
    }
}