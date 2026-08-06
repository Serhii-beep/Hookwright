using System.Collections.Frozen;

namespace Hookwright.Core.Deliveries;

/// <summary>
/// The legal moves a delivery may make, as data. Every state change goes
/// through here, so an illegal transition is impossible rather than unlikely.
/// </summary>
public static class DeliveryStateMachine
{
    private static readonly FrozenDictionary<DeliveryState, FrozenSet<DeliveryState>> Transitions =
        new Dictionary<DeliveryState, FrozenSet<DeliveryState>>
        {
            // Claimed by a worker, held behind an ordering partition, or
            // abandoned because the endpoint went away.
            [DeliveryState.Pending] = FrozenSet.ToFrozenSet(
            [
                DeliveryState.InFlight,
                DeliveryState.Blocked,
                DeliveryState.Cancelled
            ]),

            // Back to Pending covers two different events that need the same move: a
            // retryable failure with attempts remaining, and a lapsed lease being
            // reclaimed from a worker that died.
            [DeliveryState.InFlight] = FrozenSet.ToFrozenSet(
            [
                DeliveryState.Succeeded,
                DeliveryState.Failed,
                DeliveryState.Dead,
                DeliveryState.Pending
            ]),

            // The head of its partition completed, or the endpoint went away.
            [DeliveryState.Blocked] = FrozenSet.ToFrozenSet(
            [
                DeliveryState.Pending,
                DeliveryState.Cancelled
            ]),

            // Terminal states reopen only through an explicit operator retry. Re-sending
            // a delivery that already succeeded is unusual but legitimate.
            [DeliveryState.Succeeded] = FrozenSet.ToFrozenSet([DeliveryState.Pending]),
            [DeliveryState.Failed] = FrozenSet.ToFrozenSet([DeliveryState.Pending]),
            [DeliveryState.Dead] = FrozenSet.ToFrozenSet([DeliveryState.Pending]),

            // Terminal: the endpoint no longer exists, so there is nowhere to retry to.
            [DeliveryState.Cancelled] = []
        }.ToFrozenDictionary();


    private static readonly FrozenSet<DeliveryState> TerminalStates = FrozenSet.ToFrozenSet(
    [
        DeliveryState.Succeeded,
        DeliveryState.Failed,
        DeliveryState.Dead,
        DeliveryState.Cancelled
    ]);

    /// <summary>
    /// The states reachable in one step from <paramref name="from"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// No rule is defined for the state.
    /// </exception>
    public static IReadOnlySet<DeliveryState> AllowedTargets(DeliveryState from)
    {
        return Transitions.TryGetValue(from, out FrozenSet<DeliveryState>? targets)
            ? targets
            : throw new ArgumentOutOfRangeException(
                nameof(from),
                from,
                "No transition rule is defined for this delivery state.");
    }

    /// <summary>
    /// Whether a delivery may move directly from one state to another.
    /// </summary>
    public static bool CanTransition(DeliveryState from, DeliveryState to)
    {
        return AllowedTargets(from).Contains(to);
    }

    /// <summary>
    /// Verifies a move is legal, throwing if it is not.
    /// </summary>
    /// <exception cref="InvalidDeliveryTransitionException">The move is not permitted.</exception>
    public static void EnsureCanTransition(DeliveryState from, DeliveryState to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidDeliveryTransitionException(from, to);
        }
    }

    /// <summary>
    /// Whether a delivery in this state has finished and will not be attempted again.
    /// </summary>
    /// <remarks>
    /// This cannot be derived from <see cref="AllowedTargets(DeliveryState)"/>. Some
    /// of these states can still be reopened by an explicit operator retry.
    /// </remarks>
    public static bool IsTerminal(DeliveryState state)
    {
        return TerminalStates.Contains(state);
    }
}