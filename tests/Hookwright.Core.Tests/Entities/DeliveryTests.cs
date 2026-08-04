using Hookwright.Core.Entities;
using Hookwright.Core.Identifiers;

namespace Hookwright.Core.Tests.Entities;

public sealed class DeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Delivery CreateDelivery(string? partitionKey = null) =>
        Delivery.Create(WebhookEventId.New(), WebhookEndpointId.New(), partitionKey, Now);

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
}