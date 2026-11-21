using System.Threading.Channels;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;
using Hookwright.Core.Transport;

using Microsoft.Extensions.Logging;

namespace Hookwright.Dispatch;

/// <summary>
/// Turns claimed deliveries into attempts, and attempts into the results the store writes.
/// </summary>
/// <remarks>
/// One instance serves every worker loop. It holds nothing per delivery, so <see cref="RunAsync"/>
/// runs as many times at once as the configured concurrency says, every loop reading the same
/// channel. The retry policy is therefore called from many loops at once and has to cope. The
/// default one does when it is given <see cref="Random.Shared"/>. Before a delivery is sent, its
/// endpoint's circuit is asked: a delivery the circuit will not admit goes back to the queue for
/// later, unattempted, and a delivery whose endpoint has been disabled is cancelled.
/// </remarks>
public sealed class DeliveryWorker
{
    private readonly IDeliveryTransport _transport;
    private readonly IRetryPolicy _policy;
    private readonly EndpointCircuits _circuits;
    private readonly DeliveryOptions _delivery;
    private readonly RetentionOptions _retention;
    private readonly TimeProvider _time;
    private readonly ILogger<DeliveryWorker> _logger;

    /// <summary>
    /// Creates the worker.
    /// </summary>
    /// <param name="transport">Makes each attempt.</param>
    /// <param name="policy">Decides what follows each attempt.</param>
    /// <param name="circuits">This instance's circuits, one per endpoint.</param>
    /// <param name="deliveryOptions">
    /// The request timeout, which a lease must outlast, and the poll interval a deferral waits.
    /// </param>
    /// <param name="retentionOptions">How much of a failure's reason is recorded.</param>
    /// <param name="workerId">This worker's name, the one its leases are held under.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Used for logging.</param>
    public DeliveryWorker(
        IDeliveryTransport transport,
        IRetryPolicy policy,
        EndpointCircuits circuits,
        DeliveryOptions deliveryOptions,
        RetentionOptions retentionOptions,
        string workerId,
        TimeProvider timeProvider,
        ILogger<DeliveryWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(circuits);
        ArgumentNullException.ThrowIfNull(deliveryOptions);
        ArgumentNullException.ThrowIfNull(retentionOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _policy = policy;
        _circuits = circuits;
        _delivery = deliveryOptions;
        _retention = retentionOptions;
        WorkerId = workerId;
        _time = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Names this worker on every attempt it makes.
    /// </summary>
    public string WorkerId { get; }

    /// <summary>
    /// Attempts deliveries from <paramref name="deliveries"/> and queues each result on
    /// <paramref name="completions"/>, until <paramref name="stoppingToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// A stop is not a cancellation of the attempt in hand: a request that may already have reached
    /// its destination is left to finish, within the transport's own deadline, and its result is
    /// queued. Deliveries not yet taken stay where they are, for <see cref="DeliveryClaimer.ReleaseAsync"/>
    /// to hand back.
    /// </remarks>
    public async Task RunAsync(
        ChannelReader<ClaimedDelivery> deliveries,
        ChannelWriter<DeliveryCompletion> completions,
        CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(deliveries);
        ArgumentNullException.ThrowIfNull(completions);

        try
        {
            while (await deliveries.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                while (!stoppingToken.IsCancellationRequested && deliveries.TryRead(out ClaimedDelivery? claimed))
                {
                    DeliveryCompletion? completion = await AttemptAsync(claimed).ConfigureAwait(false);

                    if (completion is not null && !await QueueAsync(completions, completion, stoppingToken).ConfigureAwait(false))
                    {
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping is not an error.
        }
    }

    /// <summary>
    /// Makes one attempt, if the delivery's endpoint will take one, and says what the store
    /// should write: the result, a deferral, a cancellation, or nothing when the delivery
    /// is better left to the reaper.
    /// </summary>
    public async Task<DeliveryCompletion?> AttemptAsync(ClaimedDelivery claimed)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        DateTimeOffset started = _time.GetUtcNow();

        if (claimed.EndpointHealth is EndpointHealth.Disabled)
        {
            // Disabled since the delivery was queued, by its owner or by a result written just before
            // this claim. Nothing more goes to it.
            DispatchMetrics.Cancelled.Add(1);
            DispatchLog.Cancelled(_logger, WorkerId, claimed.DeliveryId, claimed.EndpointId);

            return DeliveryCompletion.Cancelled(claimed, started);
        }

        TimeSpan remaining = claimed.LeaseExpiresAt - started;

        if (remaining < _delivery.RequestTimeout)
        {
            // The request could outlive the lease. The reaper would hand the delivery to another
            // worker, this worker's result would be dropped as the stale one, and the consumer would
            // see the event twice.
            DispatchMetrics.Stale.Add(1);
            DispatchLog.LeftStale(_logger, WorkerId, claimed.DeliveryId, remaining);

            return null;
        }

        EndpointCircuit circuit = _circuits.For(claimed);
        Admission admission = circuit.TryAdmit(started);

        if (admission is Admission.Refused refused)
        {
            return Defer(claimed, circuit, refused, started);
        }

        return await SendAsync(claimed, circuit, (Admission.Granted)admission, started).ConfigureAwait(false);
    }

    private static string? Limit(string text, int length)
    {
        if (length == 0)
        {
            return null;
        }

        return text.Length <= length ? text : text[..length];
    }

    private async Task<DeliveryCompletion> SendAsync(
        ClaimedDelivery claimed,
        EndpointCircuit circuit,
        Admission.Granted admission,
        DateTimeOffset started)
    {
        DeliveryResult result;

        try
        {
            result = await TransmitAsync(claimed, started).ConfigureAwait(false);
        }
        catch
        {
            circuit.Release(admission);

            throw;
        }

        DateTimeOffset now = _time.GetUtcNow();
        CircuitChange change = circuit.Record(admission, result.Attempt.Outcome, now);
        RetryDecision decision = _policy.Decide(result.Attempt.Outcome, claimed.AttemptCount, result.RequestedDelay, now);

        DispatchMetrics.CountAttempt(result.Attempt.Outcome);
        DispatchLog.Attempted(_logger, WorkerId, claimed.DeliveryId, result.Attempt.Outcome, result.Attempt.Duration);
        Report(change, circuit, claimed.EndpointId);

        if (change is CircuitChange.Exhausted)
        {
            DateTimeOffset since = circuit.FailingSince ?? now;
            string reason = FormattableString.Invariant($"The endpoint has failed continuously since {since:u}.");

            DispatchLog.Retired(_logger, WorkerId, claimed.EndpointId, reason);

            return DeliveryCompletion.Retiring(claimed, result.Attempt, reason, now);
        }

        if (decision is RetryDecision.RetireEndpoint)
        {
            DispatchLog.Retired(_logger, WorkerId, claimed.EndpointId, DeliveryCompletion.GoneReason);
        }

        return DeliveryCompletion.From(claimed, result.Attempt, decision, now);
    }

    private async Task<DeliveryResult> TransmitAsync(ClaimedDelivery claimed, DateTimeOffset started)
    {
        try
        {
            return await _transport.SendAsync(claimed, WorkerId, started, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The transport refused before anything left the process, and would refuse again:
            // an endpoint with no active signing key, for example.
            DispatchLog.Unsendable(_logger, ex, WorkerId, claimed.DeliveryId, ex.Message);

            return Unsendable(claimed, ex, started);
        }
    }

    private DeliveryCompletion Defer(ClaimedDelivery claimed, EndpointCircuit circuit, Admission.Refused refused, DateTimeOffset now)
    {
        DateTimeOffset dueAt = (refused.ReopensAt ?? now) + _delivery.PollInterval * (0.5 + Random.Shared.NextDouble());

        DispatchMetrics.Deferred.Add(1);
        DispatchLog.Deferred(_logger, WorkerId, claimed.DeliveryId, dueAt, claimed.EndpointId, circuit.Health);

        return DeliveryCompletion.Deferred(claimed, dueAt);
    }

    private void Report(CircuitChange change, EndpointCircuit circuit, WebhookEndpointId endpointId)
    {
        switch (change)
        {
            case CircuitChange.Degraded:
                DispatchLog.CircuitDegraded(_logger, endpointId, circuit.ConsecutiveFailures, circuit.Limit);
                break;
            case CircuitChange.Opened:
                DispatchMetrics.CircuitsOpened.Add(1);
                DispatchLog.CircuitOpened(_logger, endpointId, circuit.ConsecutiveFailures, circuit.ReopensAt);
                break;
            case CircuitChange.Closed:
                DispatchMetrics.CircuitsClosed.Add(1);
                DispatchLog.CircuitClosed(_logger, endpointId);
                break;
            case CircuitChange.Recovered:
                DispatchLog.CircuitRecovered(_logger, endpointId);
                break;
            default:
                break;
        }
    }

    private DeliveryResult Unsendable(ClaimedDelivery claimed, Exception exception, DateTimeOffset started)
    {
        DeliveryAttempt attempt = DeliveryAttempt.FromFailure(
            claimed.DeliveryId,
            AttemptOutcome.Unsendable,
            Limit(exception.Message, _retention.ErrorDetailLength),
            started,
            _time.GetUtcNow() - started,
            WorkerId);

        return new DeliveryResult(attempt, requestedDelay: null);
    }

    private async Task<bool> QueueAsync(
        ChannelWriter<DeliveryCompletion> completions,
        DeliveryCompletion completion,
        CancellationToken stoppingToken)
    {
        if (completions.TryWrite(completion))
        {
            return true;
        }

        try
        {
            await completions.WriteAsync(completion, stoppingToken).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            DispatchLog.ResultAbandoned(_logger, WorkerId, completion.DeliveryId);

            return false;
        }
    }
}