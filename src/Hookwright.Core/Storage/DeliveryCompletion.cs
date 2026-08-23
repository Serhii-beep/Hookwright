using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;

namespace Hookwright.Core.Storage;

/// <summary>
/// The result of one attempt, expressed as the values a store should write.
/// </summary>
public sealed class DeliveryCompletion
{
    /// <summary>
    /// Why an endpoint is disabled when <see cref="RetireEndpoint"/> is set.
    /// </summary>
    public const string RetirementReason = "The endpoint returned 410 Gone.";

    private DeliveryCompletion(
        DeliveryId deliveryId,
        WebhookEndpointId endpointId,
        DeliveryState state,
        DateTimeOffset? completedAt,
        DateTimeOffset? nextAttemptAt,
        DeliveryAttempt attempt,
        bool retireEndpoint)
    {
        DeliveryId = deliveryId;
        EndpointId = endpointId;
        State = state;
        CompletedAt = completedAt;
        NextAttemptAt = nextAttemptAt;
        Attempt = attempt;
        RetireEndpoint = retireEndpoint;
    }

    /// <summary>
    /// The delivery this result applies to.
    /// </summary>
    public DeliveryId DeliveryId { get; }

    /// <summary>
    /// The endpoint this delivery was going to.
    /// </summary>
    public WebhookEndpointId EndpointId { get; }

    /// <summary>
    /// The state the delivery moves to.
    /// </summary>
    public DeliveryState State { get; }

    /// <summary>
    /// When the delivery finished, set only for terminal states.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; }

    /// <summary>
    /// When the delivery may next be attempted, set only when it returns to the queue.
    /// </summary>
    public DateTimeOffset? NextAttemptAt { get; }

    /// <summary>
    /// The attempt record to append.
    /// </summary>
    public DeliveryAttempt Attempt { get; }

    /// <summary>
    /// Whether the endpoint should also be disabled. Set only by a <c>410 Gone</c>.
    /// </summary>
    public bool RetireEndpoint { get; }

    /// <summary>
    /// Translates a retry decision into the values a store writes.
    /// </summary>
    /// <param name="claimed">The delivery that was attempted.</param>
    /// <param name="attempt">What happened to the delivery.</param>
    /// <param name="decision">What a retry policy concluded.</param>
    /// <param name="now">The current instant.</param>
    /// <exception cref="InvalidDeliveryTransitionException">
    /// The decision maps to a state a delivery cannot reach from <see cref="DeliveryState.InFlight"/>.
    /// </exception>
    public static DeliveryCompletion From(
        ClaimedDelivery claimed,
        DeliveryAttempt attempt,
        RetryDecision decision,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(decision);

        (DeliveryState State, DateTimeOffset? NextAttemptAt, bool RetireEndpoint) outcome = decision switch
        {
            RetryDecision.Succeeded => (DeliveryState.Succeeded, null, false),
            RetryDecision.Fail => (DeliveryState.Failed, null, false),
            RetryDecision.Exhausted => (DeliveryState.Dead, null, false),
            RetryDecision.RetireEndpoint => (DeliveryState.Failed, null, true),
            RetryDecision.Retry retry => (DeliveryState.Pending, retry.NextAttemptAt, false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(decision), decision, "No completion is defined for this decision.")
        };

        DeliveryStateMachine.EnsureCanTransition(DeliveryState.InFlight, outcome.State);

        DateTimeOffset? completedAt = DeliveryStateMachine.IsTerminal(outcome.State) ? now : null;

        return new DeliveryCompletion(
            claimed.DeliveryId,
            claimed.EndpointId,
            outcome.State,
            completedAt,
            outcome.NextAttemptAt,
            attempt,
            outcome.RetireEndpoint);
    }
}