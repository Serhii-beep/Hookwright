using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class DeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Worker = "web-01:4242:a1b2c3";

    private static Delivery CreateDelivery(string? partitionKey = null) =>
        Delivery.Create(WebhookEventId.New(), WebhookEndpointId.New(), partitionKey, Now);

    private static Delivery CreateClaimedDelivery()
    {
        Delivery delivery = CreateDelivery();
        delivery.Claim(Worker, Now.AddSeconds(60), Now);
        return delivery;
    }

    private static Delivery CreateDeliveryInState(DeliveryState state)
    {
        Delivery delivery = CreateDelivery();

        switch (state)
        {
            case DeliveryState.Pending:
                break;
            case DeliveryState.Blocked:
                delivery.Block();
                break;
            case DeliveryState.Cancelled:
                delivery.Cancel(Now);
                break;
            case DeliveryState.InFlight:
                delivery.Claim(Worker, Now.AddSeconds(60), Now);
                break;
            case DeliveryState.Succeeded:
                delivery.Claim(Worker, Now.AddSeconds(60), Now);
                delivery.MarkSucceeded(Now);
                break;
            case DeliveryState.Failed:
                delivery.Claim(Worker, Now.AddSeconds(60), Now);
                delivery.MarkFailed(Now);
                break;
            case DeliveryState.Dead:
                delivery.Claim(Worker, Now.AddSeconds(60), Now);
                delivery.MarkDead(Now);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, "No path to this state.");
        }

        delivery.State.ShouldBe(state);
        return delivery;
    }

    [Fact]
    public void Create_Always_ProducesAPendingDeliveryDueImmediately()
    {
        Delivery delivery = CreateDelivery();

        delivery.State.ShouldBe(DeliveryState.Pending);
        delivery.AttemptCount.ShouldBe(0);
        delivery.NextAttemptAt.ShouldBe(Now);
        delivery.CreatedAt.ShouldBe(Now);
        delivery.LeaseOwner.ShouldBeNull();
        delivery.LeaseExpiresAt.ShouldBeNull();
        delivery.CompletedAt.ShouldBeNull();
        delivery.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void Create_GivenAPartitionKey_CopiesIt()
    {
        CreateDelivery("o_1").PartitionKey.ShouldBe("o_1");
    }

    [Fact]
    public void IsDue_GivenTheExactDueInstant_ReturnsTrue()
    {
        Delivery delivery = CreateDelivery();

        delivery.IsDue(Now.AddTicks(-1)).ShouldBeFalse();
        delivery.IsDue(Now).ShouldBeTrue();
        delivery.IsDue(Now.AddHours(1)).ShouldBeTrue();
    }

    [Fact]
    public void IsLeaseExpired_GivenAFreshDelivery_ReturnsFalse()
    {
        CreateDelivery().IsLeaseExpired(Now.AddYears(1)).ShouldBeFalse();
    }

    [Fact]
    public void Claim_GivenAPendingDelivery_TakesTheLeaseAndCountsTheAttempt()
    {
        Delivery delivery = CreateClaimedDelivery();

        delivery.State.ShouldBe(DeliveryState.InFlight);
        delivery.LeaseOwner.ShouldBe(Worker);
        delivery.LeaseExpiresAt.ShouldBe(Now.AddSeconds(60));
        delivery.AttemptCount.ShouldBe(1);
        delivery.CompletedAt.ShouldBeNull();
        delivery.IsTerminal.ShouldBeFalse();
    }

    [Fact]
    public void CLaim_GivenAnAlreadyClaimedDelivery_Throws()
    {
        Delivery delivery = CreateClaimedDelivery();

        Should.Throw<InvalidDeliveryTransitionException>(() => delivery.Claim("web-02:9:zzz", Now.AddSeconds(60), Now));
    }

    [Fact]
    public void Claim_GivenALeaseThatHasAlreadyLapsed_ThrowsAndChangesNothing()
    {
        Delivery delivery = CreateDelivery();

        Should.Throw<ArgumentOutOfRangeException>(() => delivery.Claim(Worker, Now, Now));

        delivery.State.ShouldBe(DeliveryState.Pending);
        delivery.AttemptCount.ShouldBe(0);
        delivery.LeaseOwner.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Claim_GivenNoLeaseOwner_Throws(string? leaseOwner)
    {
        Delivery delivery = CreateDelivery();

        Should.Throw<ArgumentException>(() => delivery.Claim(leaseOwner!, Now.AddSeconds(60), Now));

        delivery.State.ShouldBe(DeliveryState.Pending);
    }

    [Fact]
    public void MarkSucceeded_GivenAnInFlightDelivery_CompletesAndReleasesTheLease()
    {
        Delivery delivery = CreateClaimedDelivery();

        delivery.MarkSucceeded(Now.AddSeconds(2));

        delivery.State.ShouldBe(DeliveryState.Succeeded);
        delivery.CompletedAt.ShouldBe(Now.AddSeconds(2));
        delivery.IsTerminal.ShouldBeTrue();
        delivery.LeaseOwner.ShouldBeNull();
        delivery.LeaseExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void MarkFailed_GivenAnInFlightDelivery_CompletesAsFailed()
    {
        Delivery delivery = CreateClaimedDelivery();

        delivery.MarkFailed(Now.AddSeconds(2));

        delivery.State.ShouldBe(DeliveryState.Failed);
        delivery.CompletedAt.ShouldBe(Now.AddSeconds(2));
        delivery.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void MarkDead_GivenAnInFlightDelivery_CompletesAsDead()
    {
        Delivery delivery = CreateClaimedDelivery();

        delivery.MarkDead(Now.AddSeconds(2));

        delivery.State.ShouldBe(DeliveryState.Dead);
        delivery.CompletedAt.ShouldBe(Now.AddSeconds(2));
        delivery.IsTerminal.ShouldBeTrue();
    }

    [Fact]
    public void MarkSucceeded_GivenADeliveryThatWasNeverClaimed_Throws()
    {
        Delivery delivery = CreateDelivery();

        Should.Throw<InvalidDeliveryTransitionException>(() => delivery.MarkSucceeded(Now));
    }

    [Fact]
    public void MarkSucceeded_GivenAnAlreadyCompletedDelivery_Throws()
    {
        Delivery delivery = CreateClaimedDelivery();
        delivery.MarkSucceeded(Now.AddSeconds(2));

        Should.Throw<InvalidDeliveryTransitionException>(() => delivery.MarkSucceeded(Now.AddSeconds(3)));
    }

    [Fact]
    public void IsLeaseExpired_GivenAClaimedDelivery_TurnsTrueOnlyAfterTheLeaseLapses()
    {
        Delivery delivery = CreateClaimedDelivery();

        delivery.IsLeaseExpired(Now.AddSeconds(59)).ShouldBeFalse();
        delivery.IsLeaseExpired(Now.AddSeconds(60)).ShouldBeTrue();
        delivery.IsLeaseExpired(Now.AddSeconds(61)).ShouldBeTrue();
    }

    [Fact]
    public void IsDue_GivenAClaimedDelivery_IsFalse()
    {
        CreateClaimedDelivery().IsDue(Now.AddYears(1)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(DeliveryState.Pending, false)]
    [InlineData(DeliveryState.InFlight, false)]
    [InlineData(DeliveryState.Blocked, false)]
    [InlineData(DeliveryState.Succeeded, true)]
    [InlineData(DeliveryState.Failed, true)]
    [InlineData(DeliveryState.Dead, true)]
    [InlineData(DeliveryState.Cancelled, true)]
    public void IsTerminal_Always_ReflectsTheState(DeliveryState state, bool expected)
    {
        CreateDeliveryInState(state).IsTerminal.ShouldBe(expected);
    }

    [Fact]
    public void EveryTransition_Always_KeepsCompletedAtInStepWithIsTerminal()
    {
        (string Move, Delivery Delivery, Action<Delivery> Act)[] transitions =
        [
              ("Pending -> InFlight",   CreateDeliveryInState(DeliveryState.Pending),   d => d.Claim(Worker, Now.AddSeconds(60), Now)),
              ("Pending -> Blocked",    CreateDeliveryInState(DeliveryState.Pending),   d => d.Block()),
              ("Pending -> Cancelled",  CreateDeliveryInState(DeliveryState.Pending),   d => d.Cancel(Now)),
              ("InFlight -> Succeeded", CreateDeliveryInState(DeliveryState.InFlight),  d => d.MarkSucceeded(Now)),
              ("InFlight -> Failed",    CreateDeliveryInState(DeliveryState.InFlight),  d => d.MarkFailed(Now)),
              ("InFlight -> Dead",      CreateDeliveryInState(DeliveryState.InFlight),  d => d.MarkDead(Now)),
              ("InFlight -> Pending",   CreateDeliveryInState(DeliveryState.InFlight),  d => d.ScheduleRetry(Now.AddMinutes(5))),
              ("Blocked -> Pending",    CreateDeliveryInState(DeliveryState.Blocked),   d => d.Unblock()),
              ("Blocked -> Cancelled",  CreateDeliveryInState(DeliveryState.Blocked),   d => d.Cancel(Now)),
              ("Succeeded -> Pending",  CreateDeliveryInState(DeliveryState.Succeeded), d => d.Reopen(Now)),
              ("Failed -> Pending",     CreateDeliveryInState(DeliveryState.Failed),    d => d.Reopen(Now)),
              ("Dead -> Pending",       CreateDeliveryInState(DeliveryState.Dead),      d => d.Reopen(Now)),
        ];

        foreach ((string move, Delivery delivery, Action<Delivery> act) in transitions)
        {
            act(delivery);

            (delivery.CompletedAt is not null).ShouldBe(delivery.IsTerminal, move);
        }

        transitions.Length.ShouldBe(
            Enum.GetValues<DeliveryState>().Sum(from => DeliveryStateMachine.AllowedTargets(from).Count));
    }

    [Fact]
    public void ScheduleRetry_GivenAnInFlightDelivery_RequeuesWithoutResettingTheAttemptCount()
    {
        Delivery delivery = CreateDeliveryInState(DeliveryState.InFlight);

        delivery.ScheduleRetry(Now.AddMinutes(5));

        delivery.State.ShouldBe(DeliveryState.Pending);
        delivery.NextAttemptAt.ShouldBe(Now.AddMinutes(5));
        delivery.AttemptCount.ShouldBe(1);
        delivery.LeaseOwner.ShouldBeNull();
        delivery.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void ClaimAndScheduleRetry_Repeated_AccumulatesAttempts()
    {
        Delivery delivery = CreateDelivery();

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            delivery.Claim(Worker, Now.AddSeconds(60), Now);
            delivery.AttemptCount.ShouldBe(attempt);
            delivery.ScheduleRetry(Now.AddMinutes(attempt));
        }
    }

    [Fact]
    public void ScheduleRetry_GivenABlockedDelivery_Throws()
    {
        Delivery delivery = CreateDeliveryInState(DeliveryState.Blocked);

        Should.Throw<InvalidOperationException>(() => delivery.ScheduleRetry(Now));
    }

    [Fact]
    public void Reclaim_GivenALapsedLease_RequeuesImmediately()
    {
        Delivery delivery = CreateDeliveryInState(DeliveryState.InFlight);

        delivery.Reclaim(Now.AddSeconds(90));

        delivery.State.ShouldBe(DeliveryState.Pending);
        delivery.NextAttemptAt.ShouldBe(Now.AddSeconds(90));
        delivery.LeaseOwner.ShouldBeNull();
        delivery.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public void Reclaim_GivenALeaseStillHeld_Throws()
    {
        Delivery delivery = CreateDeliveryInState(DeliveryState.InFlight);

        Should.Throw<InvalidOperationException>(() => delivery.Reclaim(Now.AddSeconds(30)));

        delivery.State.ShouldBe(DeliveryState.InFlight);
        delivery.LeaseOwner.ShouldBe(Worker);
    }

    [Fact]
    public void BlockThenUnblock_RoundTrips()
    {
        Delivery delivery = CreateDeliveryInState(DeliveryState.Blocked);

        delivery.Unblock();

        delivery.State.ShouldBe(DeliveryState.Pending);
        delivery.IsDue(Now).ShouldBeTrue();
    }

    [Fact]
    public void Unblock_GivenAPendingDelivery_Throws()
    {
        Should.Throw<InvalidOperationException>(() => CreateDeliveryInState(DeliveryState.Pending).Unblock());
    }

    [Fact]
    public void Cancel_GivenAnInFlightDelivery_Throws()
    {
        Should.Throw<InvalidDeliveryTransitionException>(() => CreateDeliveryInState(DeliveryState.InFlight).Cancel(Now));
    }

    [Theory]
    [InlineData(DeliveryState.Succeeded)]
    [InlineData(DeliveryState.Failed)]
    [InlineData(DeliveryState.Dead)]
    public void Reopen_GivenACompletedDelivery_ResetsTheAttemptBudget(DeliveryState state)
    {
        Delivery delivery = CreateDeliveryInState(state);

        delivery.Reopen(Now.AddMinutes(1));

        delivery.State.ShouldBe(DeliveryState.Pending);
        delivery.AttemptCount.ShouldBe(0);
        delivery.CompletedAt.ShouldBeNull();
        delivery.NextAttemptAt.ShouldBe(Now.AddMinutes(1));
    }

    [Fact]
    public void Reopen_GivenACancelledDelivery_Throws()
    {
        Should.Throw<InvalidDeliveryTransitionException>(() => CreateDeliveryInState(DeliveryState.Cancelled).Reopen(Now));
    }

    [Fact]
    public void Reopen_GivenAPendingDelivery_Throws()
    {
        Should.Throw<InvalidOperationException>(() => CreateDeliveryInState(DeliveryState.Pending).Reopen(Now));
    }
}