using System.Globalization;
using System.Threading.Channels;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
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
        RequestTimeout = TimeSpan.FromSeconds(1),
        MaxConnectionsPerEndpoint = 8
    };

    private static readonly CircuitOptions Circuit = new()
    {
        Cooldown = TimeSpan.FromMinutes(1),
        DisableAfter = TimeSpan.FromDays(5)
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
        completion.Attempt.ShouldNotBeNull();
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
        completion.Attempt.ShouldNotBeNull();
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
        completion.RetirementReason.ShouldBe(DeliveryCompletion.GoneReason);
        logger.Collector.GetSnapshot().ShouldContain(record => record.Level == LogLevel.Information
            && record.Message == $"{Worker} retired endpoint {claimed.EndpointId}. {DeliveryCompletion.GoneReason}");
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
        completion.Attempt.ShouldNotBeNull();
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
        completion.Attempt.ShouldNotBeNull();
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
    public async Task AttemptAsync_GivenADisabledEndpoint_CancelsTheDeliveryWithoutSending()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new();
        FakeLogger<DeliveryWorker> logger = new();
        using MetricProbe cancelled = new("hookwright.deliveries.cancelled");
        ClaimedDelivery claimed = TestDeliveries.Claimed(time.GetUtcNow() + Lease, WebhookEndpointId.New(), EndpointHealth.Disabled);

        DeliveryCompletion? completion = await CreateWorker(transport, time, logger).AttemptAsync(claimed);

        completion.ShouldNotBeNull();
        completion.State.ShouldBe(DeliveryState.Cancelled);
        completion.CompletedAt.ShouldBe(time.GetUtcNow());
        completion.Attempt.ShouldBeNull();
        transport.SentCount.ShouldBe(0);
        cancelled.Total.ShouldBe(1);
        FakeLogRecord record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Information);
        record.Message.ShouldBe($"{Worker} cancelled {claimed.DeliveryId}. Endpoint {claimed.EndpointId} is disabled");
    }

    [Fact]
    public async Task AttemptAsync_GivenACircuitWithEverySlotTaken_DefersTheDeliveryForAPollIntervalOrSo()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        FakeTimeProvider time = new();
        TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ScriptedTransport transport = new() { Gate = gate.Task };
        FakeLogger<DeliveryWorker> logger = new();
        using MetricProbe deferred = new("hookwright.deliveries.deferred");
        WebhookEndpointId endpoint = WebhookEndpointId.New();
        DeliveryWorker worker = CreateWorker(transport, time, logger, options: new DeliveryOptions
        {
            MaxConnectionsPerEndpoint = 1,
            PollInterval = TimeSpan.FromSeconds(1),
            LeaseDuration = Lease,
            RequestTimeout = TimeSpan.FromSeconds(1)
        });

        Task<DeliveryCompletion?> first = worker.AttemptAsync(TestDeliveries.Claimed(time.GetUtcNow() + Lease, endpoint));
        await transport.NextSendAsync(token);
        ClaimedDelivery turnedAway = TestDeliveries.Claimed(time.GetUtcNow() + Lease, endpoint);

        DeliveryCompletion? completion = await worker.AttemptAsync(turnedAway);

        completion.ShouldNotBeNull();
        completion.State.ShouldBe(DeliveryState.Pending);
        completion.Attempt.ShouldBeNull();
        completion.GivesBackAttempt.ShouldBeTrue();
        completion.NextAttemptAt.ShouldNotBeNull();
        completion.NextAttemptAt.Value.ShouldBeInRange(time.GetUtcNow().AddSeconds(0.5), time.GetUtcNow().AddSeconds(1.5));
        deferred.Total.ShouldBe(1);
        logger.Collector.GetSnapshot().ShouldContain(record => record.Level == LogLevel.Debug
            && record.Message.StartsWith($"{Worker} deferred {turnedAway.DeliveryId} until ", StringComparison.Ordinal)
            && record.Message.EndsWith($". Endpoint {endpoint} is Healthy", StringComparison.Ordinal));

        gate.SetResult();
        (await first).ShouldNotBeNull().State.ShouldBe(DeliveryState.Succeeded);
        transport.SentCount.ShouldBe(1);
    }

    [Fact]
    public async Task AttemptAsync_GivenAnOpenCircuit_DefersTheDeliveryUntilItReopens()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new(_ => 500);
        WebhookEndpointId endpoint = WebhookEndpointId.New();
        EndpointCircuits circuits = new(Options, Circuit);
        DeliveryWorker worker = CreateWorker(transport, time, circuits: circuits);

        (await worker.AttemptAsync(TestDeliveries.Claimed(time.GetUtcNow() + Lease, endpoint, consecutiveFailures: 10, failingSince: time.GetUtcNow().AddMinutes(-5))))
            .ShouldNotBeNull().Attempt.ShouldNotBeNull();
        DateTimeOffset reopensAt = circuits.Find(endpoint).ShouldNotBeNull().ReopensAt.ShouldNotBeNull();
        reopensAt.ShouldBe(time.GetUtcNow() + Circuit.Cooldown);

        DeliveryCompletion? completion = await worker.AttemptAsync(TestDeliveries.Claimed(time.GetUtcNow() + Lease, endpoint));

        completion.ShouldNotBeNull();
        completion.State.ShouldBe(DeliveryState.Pending);
        completion.Attempt.ShouldBeNull();
        completion.NextAttemptAt.ShouldNotBeNull();
        completion.NextAttemptAt.Value.ShouldBeInRange(reopensAt.AddSeconds(0.5), reopensAt.AddSeconds(1.5));
        transport.SentCount.ShouldBe(1);
    }

    [Fact]
    public async Task AttemptAsync_GivenFailuresInARow_CutsTheConcurrencyOpensTheCircuitProbesAndClosesIt()
    {
        FakeTimeProvider time = new();
        int sent = 0;
        ScriptedTransport transport = new(_ => Interlocked.Increment(ref sent) <= 10 ? 500 : 200);
        FakeLogger<DeliveryWorker> logger = new();
        using MetricProbe opened = new("hookwright.circuits.opened");
        using MetricProbe closed = new("hookwright.circuits.closed");
        using MetricProbe deferred = new("hookwright.deliveries.deferred");
        WebhookEndpointId endpoint = WebhookEndpointId.New();
        EndpointCircuits circuits = new(Options, Circuit);
        DeliveryWorker worker = CreateWorker(transport, time, logger, circuits: circuits);
        List<int> limits = [];

        for (int i = 0; i < 10; i++)
        {
            (await worker.AttemptAsync(Claimed(time, endpoint))).ShouldNotBeNull().State.ShouldBe(DeliveryState.Pending);
            limits.Add(circuits.Find(endpoint).ShouldNotBeNull().Limit);
        }

        EndpointCircuit circuit = circuits.Find(endpoint).ShouldNotBeNull();
        limits.ShouldBe([8, 8, 4, 2, 1, 1, 1, 1, 1, 1]);
        circuit.Health.ShouldBe(EndpointHealth.Failing);
        DateTimeOffset reopensAt = circuit.ReopensAt.ShouldNotBeNull();
        reopensAt.ShouldBe(time.GetUtcNow() + Circuit.Cooldown);

        DeliveryCompletion turnedAway = (await worker.AttemptAsync(Claimed(time, endpoint))).ShouldNotBeNull();
        turnedAway.Attempt.ShouldBeNull();
        turnedAway.NextAttemptAt.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(reopensAt);
        transport.SentCount.ShouldBe(10);

        time.Advance(Circuit.Cooldown);

        (await worker.AttemptAsync(Claimed(time, endpoint))).ShouldNotBeNull().State.ShouldBe(DeliveryState.Succeeded);
        circuit.Health.ShouldBe(EndpointHealth.Degraded);
        circuit.Limit.ShouldBe(1);
        circuit.ReopensAt.ShouldBeNull();

        limits.Clear();

        for (int i = 0; i < 3; i++)
        {
            (await worker.AttemptAsync(Claimed(time, endpoint))).ShouldNotBeNull().State.ShouldBe(DeliveryState.Succeeded);
            limits.Add(circuit.Limit);
        }

        limits.ShouldBe([2, 4, 8]);
        circuit.Health.ShouldBe(EndpointHealth.Healthy);
        opened.Total.ShouldBe(1);
        closed.Total.ShouldBe(1);
        deferred.Total.ShouldBe(1);
        IReadOnlyList<FakeLogRecord> records = logger.Collector.GetSnapshot();
        records.ShouldContain(record => record.Level == LogLevel.Warning
            && record.Message == $"Endpoint {endpoint} has failed 3 times in a row. It takes 4 deliveries at once from now on");

        records.ShouldContain(record => record.Level == LogLevel.Warning
            && record.Message.StartsWith($"Endpoint {endpoint} has failed 10 times in a row. Nothing more goes to it before ", StringComparison.Ordinal));

        records.ShouldContain(record => record.Level == LogLevel.Information
            && record.Message == $"Endpoint {endpoint} answered its probe. Deliveries resume one at a time");
        records.ShouldContain(record => record.Level == LogLevel.Information
            && record.Message == $"Endpoint {endpoint} is back at full concurrency");
    }

    [Fact]
    public async Task AttemptAsync_GivenAnEndpointThatHasFailedForAsLongAsAllowed_RetiresIt()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new(_ => 503);
        FakeLogger<DeliveryWorker> logger = new();
        DateTimeOffset since = time.GetUtcNow() - Circuit.DisableAfter - TimeSpan.FromMinutes(1);
        ClaimedDelivery claimed = TestDeliveries.Claimed(
            time.GetUtcNow() + Lease, WebhookEndpointId.New(), consecutiveFailures: 4, failingSince: since);
        string reason = $"The endpoint has failed continuously since {since.ToString("u", CultureInfo.InvariantCulture)}.";

        DeliveryCompletion? completion = await CreateWorker(transport, time, logger).AttemptAsync(claimed);

        completion.ShouldNotBeNull();
        completion.State.ShouldBe(DeliveryState.Cancelled);
        completion.CompletedAt.ShouldBe(time.GetUtcNow());
        completion.Attempt.ShouldNotBeNull();
        completion.Attempt.Outcome.ShouldBe(AttemptOutcome.ServerError);
        completion.RetirementReason.ShouldBe(reason);
        logger.Collector.GetSnapshot().ShouldContain(record => record.Level == LogLevel.Information
            && record.Message == $"{Worker} retired endpoint {claimed.EndpointId}. {reason}");
    }

    [Fact]
    public async Task AttemptAsync_GivenACircuitTimingARunTheStoreSaysHasEnded_DoesNotRetireTheEndpoint()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new(_ => 503);
        WebhookEndpointId endpoint = WebhookEndpointId.New();
        EndpointCircuits circuits = new(Options, Circuit);
        DeliveryWorker worker = CreateWorker(transport, time, circuits: circuits);
        DateTimeOffset since = time.GetUtcNow() - Circuit.DisableAfter - TimeSpan.FromMinutes(1);

        circuits.For(TestDeliveries.Claimed(time.GetUtcNow() + Lease, endpoint, consecutiveFailures: 4, failingSince: since));

        DeliveryCompletion? completion = await worker.AttemptAsync(Claimed(time, endpoint));

        completion.ShouldNotBeNull();
        completion.State.ShouldBe(DeliveryState.Pending);
        completion.RetirementReason.ShouldBeNull();
        circuits.Find(endpoint).ShouldNotBeNull().FailingSince.ShouldBe(time.GetUtcNow());
    }

    [Fact]
    public async Task AttemptAsync_GivenASendThatIsCancelled_GivesTheSlotBackAndLearnsNothing()
    {
        FakeTimeProvider time = new();
        ScriptedTransport transport = new(_ => throw new OperationCanceledException());
        WebhookEndpointId endpoint = WebhookEndpointId.New();
        EndpointCircuits circuits = new(Options, Circuit);
        DeliveryWorker worker = CreateWorker(transport, time, circuits: circuits);

        await Should.ThrowAsync<OperationCanceledException>(() => worker.AttemptAsync(Claimed(time, endpoint)));

        EndpointCircuit circuit = circuits.Find(endpoint).ShouldNotBeNull();
        circuit.InFlight.ShouldBe(0);
        circuit.ConsecutiveFailures.ShouldBe(0);
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

    private static ClaimedDelivery Claimed(FakeTimeProvider time, WebhookEndpointId endpoint)
    {
        return TestDeliveries.Claimed(time.GetUtcNow() + Lease, endpoint);
    }

    private static DeliveryWorker CreateWorker(
        ScriptedTransport transport,
        FakeTimeProvider time,
        FakeLogger<DeliveryWorker>? logger = null,
        RetentionOptions? retention = null,
        DeliveryOptions? options = null,
        EndpointCircuits? circuits = null)
    {
        options ??= Options;

        return new DeliveryWorker(
            transport,
            Policy,
            circuits ?? new EndpointCircuits(options, Circuit),
            options,
            retention ?? new RetentionOptions(),
            Worker,
            time,
            logger ?? new FakeLogger<DeliveryWorker>());
    }
}