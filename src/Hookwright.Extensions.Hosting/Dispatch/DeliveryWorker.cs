using System.Threading.Channels;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
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
/// default one does when it is given <see cref="Random.Shared"/>.
/// </remarks>
public sealed class DeliveryWorker
{
    private readonly IDeliveryTransport _transport;
    private readonly IRetryPolicy _policy;
    private readonly DeliveryOptions _delivery;
    private readonly RetentionOptions _retention;
    private readonly TimeProvider _time;
    private readonly ILogger<DeliveryWorker> _logger;

    /// <summary>
    /// Creates the worker.
    /// </summary>
    /// <param name="transport">Makes each attempt.</param>
    /// <param name="policy">Decides what follows each attempt.</param>
    /// <param name="deliveryOptions">The request timeout, which a lease must outlast.</param>
    /// <param name="retentionOptions">How much of a failure's reason is recorded.</param>
    /// <param name="workerId">This worker's name, the one its leases are held under.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Used for logging.</param>
    public DeliveryWorker(
        IDeliveryTransport transport,
        IRetryPolicy policy,
        DeliveryOptions deliveryOptions,
        RetentionOptions retentionOptions,
        string workerId,
        TimeProvider timeProvider,
        ILogger<DeliveryWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(deliveryOptions);
        ArgumentNullException.ThrowIfNull(retentionOptions);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _transport = transport;
        _policy = policy;
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
    /// Makes one attempt and says what the store should write, or nothing when the delivery is
    /// better left to the reaper.
    /// </summary>
    public async Task<DeliveryCompletion?> AttemptAsync(ClaimedDelivery claimed)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        DateTimeOffset started = _time.GetUtcNow();
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

        DeliveryResult result;

        try
        {
            result = await _transport.SendAsync(claimed, WorkerId, started, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The transport refused before anything left the process, and would refuse again:
            // an endpoint with no active signing key, for example.
            result = Unsendable(claimed, exception, started);
            DispatchLog.Unsendable(_logger, exception, WorkerId, claimed.DeliveryId, exception.Message);
        }

        DateTimeOffset now = _time.GetUtcNow();
        RetryDecision decision = _policy.Decide(result.Attempt.Outcome, claimed.AttemptCount, result.RequestedDelay, now);

        DispatchMetrics.CountAttempt(result.Attempt.Outcome);
        DispatchLog.Attempted(_logger, WorkerId, claimed.DeliveryId, result.Attempt.Outcome, result.Attempt.Duration);

        if (decision is RetryDecision.RetireEndpoint)
        {
            DispatchLog.Retired(_logger, WorkerId, claimed.EndpointId);
        }

        return DeliveryCompletion.From(claimed, result.Attempt, decision, now);
    }

    private static string? Limit(string text, int length)
    {
        if (length == 0)
        {
            return null;
        }

        return text.Length <= length ? text : text[..length];
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