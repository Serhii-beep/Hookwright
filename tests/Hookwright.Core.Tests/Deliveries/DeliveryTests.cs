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
}