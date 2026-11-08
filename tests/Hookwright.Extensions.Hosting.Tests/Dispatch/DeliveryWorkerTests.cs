using System.Threading.Channels;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Storage;
using Hookwright.Dispatch;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

[Collection("Dispatch")]
public sealed class DeliveryWorkerTests
{
    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    private static readonly DeliveryOptions Options = new()
    {
        BatchSize = 10,
        PollInterval = TimeSpan.FromSeconds(1),
        LeaseDuration = Lease,
        RequestTimeout = TimeSpan.FromSeconds(1)
    };

    private static readonly DefaultRetryPolicy Policy = new(
        new RetrySchedule(RetrySchedule.Default.Steps, jitterFactor: 0), Random.Shared);

    [Fact]
    public async Task AttemptAsync_GivenSuccess_CompletesTheDeliveryAndRecordsTheAttempt()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new();
        FakeLogger<DeliveryWorker> logger = new();
        using MetricProbe attempted = new("hookwright.deliveries.attempted");
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + Lease);

        DeliveryCompletion? completion = await CreateWorker(transport, time, logger).AttemptAsync(claimed);

        completion.ShouldNotBeNull();
        completion.DeliveryId.ShouldBe(claimed.DeliveryId);
        completion.State.ShouldBe(DeliveryState.Succeeded);
        completion.CompletedAt.ShouldBe(time.GetUtcNow());
        completion.Attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        completion.Attempt.ResponseStatusCode.ShouldBe(200);
        completion.Attempt.WorkerId.ShouldBe(Worker);
        attempted.Total.ShouldBe(1);
        FakeLogRecord record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Debug);
        record.Message.ShouldBe($"{Worker} attempted {claimed.DeliveryId}: Succeeded in 00:00:00.0050000");
    }

    [Fact]
    public async Task AttemptAsync_GivenAServerError_SchedulesTheRetryThePolicyChose()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new(_ => 500);
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + Lease);

        DeliveryCompletion? completion = await CreateWorker(transport, time).AttemptAsync(claimed);

        completion.ShouldNotBeNull();
        completion.State.ShouldBe(DeliveryState.Pending);
        completion.NextAttemptAt.ShouldBe(time.GetUtcNow() + TimeSpan.FromSeconds(5));
        completion.CompletedAt.ShouldBeNull();
        completion.Attempt.Outcome.ShouldBe(AttemptOutcome.ServerError);
    }

    [Fact]
    public async Task AttemptAsync_GivenARequestedDelay_HandsItToThePolicy()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new(_ => 429, requestedDelay: TimeSpan.FromMinutes(2));
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + Lease);

        DeliveryCompletion? completion = await CreateWorker(transport, time).AttemptAsync(claimed);

        completion.ShouldNotBeNull();
        completion.State.ShouldBe(DeliveryState.Pending);
        completion.NextAttemptAt.ShouldBe(time.GetUtcNow() + TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task AttemptAsync_GivenGone_FailsTheDeliveryAndRetiresTheEndpoint()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new(_ => 410);
        FakeLogger<DeliveryWorker> logger = new();
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + Lease);

        DeliveryCompletion? completion = await CreateWorker(transport, time, logger).AttemptAsync(claimed);

        completion.ShouldNotBeNull();
        completion.State.ShouldBe(DeliveryState.Failed);
        completion.RetireEndpoint.ShouldBeTrue();
        logger.Collector.GetSnapshot().ShouldContain(record => record.Level == LogLevel.Information
            && record.Message == $"{Worker} retired endpoint {claimed.EndpointId}. It answered 410 Gone");
    }

    [Fact]
    public async Task AttemptAsync_GivenATransportThatRefuses_FailsTheDeliveryAsUnsendable()
    {
        FakeTimeProvider time = new();
        InvalidOperationException refusal = new("Endpoint ep_1 has no active HmacSha256 signing key, so delivery dlv_1 cannot be sent.");
        ScriptedTransport transport = new(_ => throw refusal);
        FakeLogger<DeliveryWorker> logger = new();
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + Lease);

        DeliveryCompletion? completion = await CreateWorker(transport, time, logger).AttemptAsync(claimed);

        completion.ShouldNotBeNull();
        completion.State.ShouldBe(DeliveryState.Failed);
        completion.Attempt.Outcome.ShouldBe(AttemptOutcome.Unsendable);
        completion.Attempt.ResponseStatusCode.ShouldBeNull();
        completion.Attempt.ErrorDetail.ShouldBe(refusal.Message);
        transport.SentCount.ShouldBe(0);
        FakeLogRecord error = logger.Collector.GetSnapshot().Single(record => record.Level == LogLevel.Error);
        error.Exception.ShouldBeSameAs(refusal);
        error.Message.ShouldBe($"{Worker} could not attempt {claimed.DeliveryId}, which is failed. {refusal.Message}");
    }

    [Fact]
    public async Task AttemptAsync_GivenAnErrorDetailLengthOfZero_RecordsNoReason()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new(_ => throw new InvalidOperationException("no key"));
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + Lease);

        DeliveryCompletion? completion = await CreateWorker(transport, time, retention: new RetentionOptions
        {
            ErrorDetailLength = 0
        })
        .AttemptAsync(claimed);

        completion.ShouldNotBeNull();
        completion.Attempt.Outcome.ShouldBe(AttemptOutcome.Unsendable);
        completion.Attempt.ErrorDetail.ShouldBeNull();
    }

    [Fact]
    public async Task AttemptAsync_GivenTooLittleLeaseLeft_LeavesTheDeliveryToTheReaper()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new();
        FakeLogger<DeliveryWorker> logger = new();
        using MetricProbe stale = new("hookwright.deliveries.stale");
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + TimeSpan.FromMilliseconds(999));

        DeliveryCompletion? completion = await CreateWorker(transport, time, logger).AttemptAsync(claimed);

        completion.ShouldBeNull();
        transport.SentCount.ShouldBe(0);
        stale.Total.ShouldBe(1);
        FakeLogRecord record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.Message.ShouldBe($"{Worker} left {claimed.DeliveryId} to the reaper. Its lease had 00:00:00.9990000 left, less than the request timeout");
    }

    [Fact]
    public async Task AttemptAsync_GivenExactlyTheRequestTimeoutLeft_Sends()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new();
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + Options.RequestTimeout);

        DeliveryCompletion? completion = await CreateWorker(transport, time).AttemptAsync(claimed);

        completion.ShouldNotBeNull();
        transport.SentCount.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_Always_AttemptsWhatItReadsAndQueuesEveryResult()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        FakeTimeProvider time = new();
        ScriptedTransport transport = new();
        Channel<ClaimedDelivery> deliveries = Channel.CreateUnbounded<ClaimedDelivery>();
        Channel<DeliveryCompletion> completions = Channel.CreateUnbounded<DeliveryCompletion>();
        using CancellationTokenSource stopping = new();

        for (int i = 0; i < 3; i++)
        {
            deliveries.Writer.TryWrite(TestDeliveries.Claimed(time.GetUtcNow() + Lease));
        }

        Task run = CreateWorker(transport, time).RunAsync(deliveries.Reader, completions.Writer, stopping.Token);

        for (int i = 0; i < 3; i++)
        {
            (await completions.Reader.ReadAsync(token)).State.ShouldBe(DeliveryState.Succeeded);
        }

        await stopping.CancelAsync();
        await run;

        transport.SentCount.ShouldBe(3);
    }

    [Fact]
    public async Task RunAsync_WhenStopped_FinishesTheAttemptInHandQueuesItsResultAndTakesNoMore()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        FakeTimeProvider time = new();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedTransport transport = new() { Gate = gate.Task };
        Channel<ClaimedDelivery> deliveries = Channel.CreateUnbounded<ClaimedDelivery>();
        Channel<DeliveryCompletion> completions = Channel.CreateUnbounded<DeliveryCompletion>();
        using CancellationTokenSource stopping = new();
        deliveries.Writer.TryWrite(TestDeliveries.Claimed(time.GetUtcNow() + Lease));
        deliveries.Writer.TryWrite(TestDeliveries.Claimed(time.GetUtcNow() + Lease));

        Task run = CreateWorker(transport, time).RunAsync(deliveries.Reader, completions.Writer, stopping.Token);

        await transport.NextSendAsync(token);
        await stopping.CancelAsync();
        gate.SetResult();
        await run;

        completions.Reader.Count.ShouldBe(1);
        transport.SentCount.ShouldBe(1);
        deliveries.Reader.Count.ShouldBe(1);
    }

    [Fact]
    public async Task RunAsync_WhenStoppedWithNoRoomForTheResult_AbandonsItAndSaysSo()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        FakeTimeProvider time = new();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedTransport transport = new() { Gate = gate.Task };
        FakeLogger<DeliveryWorker> logger = new();
        Channel<ClaimedDelivery> deliveries = Channel.CreateUnbounded<ClaimedDelivery>();
        Channel<DeliveryCompletion> completions = Channel.CreateBounded<DeliveryCompletion>(1);
        using CancellationTokenSource stopping = new();
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + Lease);
        deliveries.Writer.TryWrite(claimed);
        completions.Writer.TryWrite(TestDeliveries.Completed(TestDeliveries.Claimed(time.GetUtcNow() + Lease), time.GetUtcNow()));

        Task run = CreateWorker(transport, time, logger).RunAsync(deliveries.Reader, completions.Writer, stopping.Token);

        await transport.NextSendAsync(token);
        await stopping.CancelAsync();
        gate.SetResult();
        await run;

        completions.Reader.Count.ShouldBe(1);
        logger.Collector.GetSnapshot().ShouldContain(record => record.Level == LogLevel.Warning
            && record.Message == $"{Worker} stopped before the result of {claimed.DeliveryId} could be queued. Its lease will lapse");
    }

    private static DeliveryWorker CreateWorker(
        ScriptedTransport transport,
        FakeTimeProvider time,
        FakeLogger<DeliveryWorker>? logger = null,
        RetentionOptions? retention = null)
    {
        return new DeliveryWorker(
            transport, Policy, Options, retention ?? new RetentionOptions(), Worker, time, logger ?? new FakeLogger<DeliveryWorker>());
    }
}