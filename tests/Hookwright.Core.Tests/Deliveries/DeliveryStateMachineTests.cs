using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class DeliveryStateMachineTests
{
    private static readonly (DeliveryState From, DeliveryState To)[] LegalTransitions =
    [
        (DeliveryState.Pending, DeliveryState.InFlight),
        (DeliveryState.Pending, DeliveryState.Blocked),
        (DeliveryState.Pending, DeliveryState.Cancelled),

        (DeliveryState.InFlight, DeliveryState.Succeeded),
        (DeliveryState.InFlight, DeliveryState.Failed),
        (DeliveryState.InFlight, DeliveryState.Dead),
        (DeliveryState.InFlight, DeliveryState.Pending),

        (DeliveryState.Blocked, DeliveryState.Pending),
        (DeliveryState.Blocked, DeliveryState.Cancelled),

        (DeliveryState.Succeeded, DeliveryState.Pending),
        (DeliveryState.Failed, DeliveryState.Pending),
        (DeliveryState.Dead, DeliveryState.Pending)
    ];

    [Fact]
    public void CanTransition_Always_MatchesTheDeclaredMatrix()
    {
        foreach (DeliveryState from in Enum.GetValues<DeliveryState>())
        {
            foreach (DeliveryState to in Enum.GetValues<DeliveryState>())
            {
                bool expected = LegalTransitions.Contains((from, to));

                DeliveryStateMachine.CanTransition(from, to).ShouldBe(expected, $"{from} -> {to}");
            }
        }
    }

    [Fact]
    public void AllowedTargets_Always_DefinesARuleForEveryState()
    {
        foreach (DeliveryState state in Enum.GetValues<DeliveryState>())
        {
            Should.NotThrow(() => DeliveryStateMachine.AllowedTargets(state));
        }
    }

    [Fact]
    public void AllowedTargets_GivenAnUndefinedState_Throws()
    {
        DeliveryState undefined = (DeliveryState)short.MaxValue;

        Should.Throw<ArgumentOutOfRangeException>(() => DeliveryStateMachine.AllowedTargets(undefined));
    }

    [Fact]
    public void CanTransition_GivenAStateToItself_IsAlwaysFalse()
    {
        foreach (DeliveryState state in Enum.GetValues<DeliveryState>())
        {
            DeliveryStateMachine.CanTransition(state, state).ShouldBeFalse($"{state} -> {state}");
        }
    }

    [Fact]
    public void AllowedTargets_GivenCancelled_IsEmpty()
    {
        DeliveryStateMachine.AllowedTargets(DeliveryState.Cancelled).ShouldBeEmpty();
    }

    [Fact]
    public void EnsureCanTransition_GivenAnIllegalMove_ThrowsWithBothStates()
    {
        InvalidDeliveryTransitionException exception = Should.Throw<InvalidDeliveryTransitionException>(
            () => DeliveryStateMachine.EnsureCanTransition(DeliveryState.Succeeded, DeliveryState.InFlight));

        exception.From.ShouldBe(DeliveryState.Succeeded);
        exception.To.ShouldBe(DeliveryState.InFlight);
        exception.Message.ShouldContain("Succeeded");
        exception.Message.ShouldContain("InFlight");
    }

    [Fact]
    public void EnsureCanTransition_GivenALegalMove_DoesNotThrow()
    {
        foreach ((DeliveryState from, DeliveryState to) in LegalTransitions)
        {
            Should.NotThrow(() => DeliveryStateMachine.EnsureCanTransition(from, to));
        }
    }
}