using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;

namespace Hookwright.Core.Storage;

/// <summary>
/// What a worker concluded about one claimed delivery, expressed as the values a store should write.
/// </summary>
/// <remarks>
/// Usually the result of an attempt. Sometimes the decision not to make one: a delivery deferred
/// because its endpoint's circuit would not admit it, or cancelled because its endpoint has been
/// disabled.
/// </remarks>
public sealed class DeliveryCompletion
{
    /// <summary>
    /// Why an endpoint that answered <c>410 Gone</c> is disabled.
    /// </summary>
    public const string GoneReason = "The endpoint returned 410 Gone.";

    private DeliveryCompletion(
        DeliveryId deliveryId,
        WebhookEndpointId endpointId,
        DeliveryState state,
        DateTimeOffset? completedAt,
        DateTimeOffset? nextAttemptAt,
        DeliveryAttempt? attempt,
        bool givesBackAttempt,
        string? retirementReason)
    {
        DeliveryId = deliveryId;
        EndpointId = endpointId;
        State = state;
        CompletedAt = completedAt;
        NextAttemptAt = nextAttemptAt;
        Attempt = attempt;
        GivesBackAttempt = givesBackAttempt;
        RetirementReason = retirementReason;
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
    /// The attempt record to append, or <see langword="null"/> when no request was made.
    /// </summary>
    public DeliveryAttempt? Attempt { get; }

    /// <summary>
    /// Whether the attempt the claim counted is given back, because none was made and the
    /// delivery returns to the queue.
    /// </summary>
    public bool GivesBackAttempt { get; }

    /// <summary>
    /// Why the endpoint should be disabled as well, or <see langword="null"/> when it should not.
    /// </summary>
    public string? RetirementReason { get; }

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

        (DeliveryState State, DateTimeOffset? NextAttemptAt, string? RetirementReason) outcome = decision switch
        {
            RetryDecision.Succeeded => (DeliveryState.Succeeded, null, null),
            RetryDecision.Fail => (DeliveryState.Failed, null, null),
            RetryDecision.Exhausted => (DeliveryState.Dead, null, null),
            RetryDecision.RetireEndpoint => (DeliveryState.Failed, null, GoneReason),
            RetryDecision.Retry retry => (DeliveryState.Pending, retry.NextAttemptAt, null),
            _ => throw new ArgumentOutOfRangeException(
                nameof(decision), decision, "No completion is defined for this decision.")
        };

        DateTimeOffset? completedAt = DeliveryStateMachine.IsTerminal(outcome.State) ? now : null;

        return Create(
            claimed,
            outcome.State,
            completedAt,
            outcome.NextAttemptAt,
            attempt,
            givesBackAttempt: false,
            outcome.RetirementReason);
    }

    /// <summary>
    /// Returns the delivery to the queue unattempted, due at <paramref name="dueAt"/>, with the
    /// attempt the claim counted given back. For a delivery its endpoint's circuit would not admit.
    /// </summary>
    /// <param name="claimed">The delivery that was turned away.</param>
    /// <param name="dueAt">When it may be claimed again.</param>
    public static DeliveryCompletion Deferred(ClaimedDelivery claimed, DateTimeOffset dueAt)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        return Create(
            claimed, DeliveryState.Pending, completedAt: null, dueAt, attempt: null, givesBackAttempt: true, retirementReason: null);
    }

    /// <summary>
    /// Abandons the delivery unattempted, because its endpoint has been disabled since it was claimed.
    /// </summary>
    /// <param name="claimed">The delivery that will not be sent.</param>
    /// <param name="now">The current instant.</param>
    public static DeliveryCompletion Cancelled(ClaimedDelivery claimed, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(claimed);

        return Create(
            claimed, DeliveryState.Cancelled, now, nextAttemptAt: null, attempt: null, givesBackAttempt: false, retirementReason: null);
    }

    /// <summary>
    /// Records the attempt, abandons the delivery, and disables the endpoint for
    /// <paramref name="reason"/>: it has been failing for longer than it is allowed to.
    /// </summary>
    /// <param name="claimed">The delivery that was attempted.</param>
    /// <param name="attempt">What happened to the delivery.</param>
    /// <param name="reason">Why the endpoint is being disabled, as its owner will read it.</param>
    /// <param name="now">The current instant.</param>
    /// <exception cref="ArgumentException">No reason was given.</exception>
    public static DeliveryCompletion Retiring(
        ClaimedDelivery claimed,
        DeliveryAttempt attempt,
        string reason,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(claimed);
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return Create(claimed, DeliveryState.Cancelled, now, nextAttemptAt: null, attempt, givesBackAttempt: false, reason);
    }

    private static DeliveryCompletion Create(
        ClaimedDelivery claimed,
        DeliveryState state,
        DateTimeOffset? completedAt,
        DateTimeOffset? nextAttemptAt,
        DeliveryAttempt? attempt,
        bool givesBackAttempt,
        string? retirementReason)
    {
        DeliveryStateMachine.EnsureCanTransition(DeliveryState.InFlight, state);

        return new DeliveryCompletion(
            claimed.DeliveryId,
            claimed.EndpointId,
            state,
            completedAt,
            nextAttemptAt,
            attempt,
            givesBackAttempt,
            retirementReason);
    }
}