using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;
using Hookwright.Dispatch;
using Hookwright.Extensions.Hosting.Tests.Dispatch.Helpers;

namespace Hookwright.Extensions.Hosting.Tests.Dispatch;

public sealed class EndpointCircuitsTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    private static readonly CircuitOptions Circuit = new()
    {
        Cooldown = TimeSpan.FromMinutes(1),
        DisableAfter = TimeSpan.FromDays(5)
    };

    [Fact]
    public void For_GivenAnEndpointNotSeenBefore_SeedsItsCircuitFromTheClaim()
    {
        EndpointCircuits circuits = Circuits(connectionsPerEndpoint: 8);
        ClaimedDelivery claimed = TestDeliveries.Claimed(
            Now + Lease, WebhookEndpointId.New(), consecutiveFailures: 5, failingSince: Now.AddMinutes(-10));

        EndpointCircuit circuit = circuits.For(claimed);

        circuit.ConsecutiveFailures.ShouldBe(5);
        circuit.FailingSince.ShouldBe(Now.AddMinutes(-10));
        circuit.Limit.ShouldBe(1);
        circuit.Health.ShouldBe(EndpointHealth.Degraded);
        circuits.Count.ShouldBe(1);
        circuits.Find(claimed.EndpointId).ShouldBeSameAs(circuit);
    }

    [Fact]
    public void For_GivenAnEndpointSeenBefore_ReturnsItsCircuitWhateverTheClaimSays()
    {
        EndpointCircuits circuits = Circuits(connectionsPerEndpoint: 8);
        WebhookEndpointId endpoint = WebhookEndpointId.New();
        EndpointCircuit first = circuits.For(TestDeliveries.Claimed(Now + Lease, endpoint));

        EndpointCircuit second = circuits.For(TestDeliveries.Claimed(Now + Lease, endpoint, consecutiveFailures: 12));

        second.ShouldBeSameAs(first);
        second.ConsecutiveFailures.ShouldBe(0);
        circuits.Count.ShouldBe(1);
    }

    [Fact]
    public void For_Always_CapsTheCircuitAtTheConnectionsPerEndpointNotThePool()
    {
        EndpointCircuits circuits = new(
            new DeliveryOptions { MaxConcurrency = 64, MaxConnectionsPerEndpoint = 8 }, Circuit);

        circuits.For(TestDeliveries.Claimed(Now + Lease)).Limit.ShouldBe(8);
    }

    [Fact]
    public void Refusing_Always_NamesTheEndpointsWhoseCircuitWouldRefuseADeliveryNow()
    {
        EndpointCircuits circuits = Circuits(connectionsPerEndpoint: 1);
        ClaimedDelivery busy = TestDeliveries.Claimed(Now + Lease);
        ClaimedDelivery open = TestDeliveries.Claimed(Now + Lease, WebhookEndpointId.New(), consecutiveFailures: 10, failingSince: Now.AddHours(-1));
        ClaimedDelivery idle = TestDeliveries.Claimed(Now + Lease);

        circuits.For(busy).TryAdmit(Now).ShouldBeOfType<Admission.Granted>();
        EndpointCircuit opened = circuits.For(open);
        opened.Record(opened.TryAdmit(Now).ShouldBeOfType<Admission.Granted>(), AttemptOutcome.ServerError, Now);
        circuits.For(idle);

        circuits.Refusing(Now).ShouldBe([busy.EndpointId, open.EndpointId], ignoreOrder: true);
        circuits.Refusing(Now + Circuit.Cooldown).ShouldBe([busy.EndpointId]);
    }

    [Fact]
    public void For_GivenAClaimThatShowsTheRunOfFailuresEnded_StopsTheCircuitsClock()
    {
        EndpointCircuits circuits = Circuits(connectionsPerEndpoint: 8);
        WebhookEndpointId endpoint = WebhookEndpointId.New();
        EndpointCircuit circuit = circuits.For(
            TestDeliveries.Claimed(Now + Lease, endpoint, EndpointHealth.Failing, consecutiveFailures: 12, failingSince: Now.AddDays(-6)));

        circuits.For(TestDeliveries.Claimed(Now + Lease, endpoint)).ShouldBeSameAs(circuit);

        circuit.FailingSince.ShouldBeNull();
        circuit.ConsecutiveFailures.ShouldBe(12);
        circuit.Health.ShouldBe(EndpointHealth.Failing);
    }

    [Fact]
    public void Refusing_GivenMoreEndpointsThanAClaimShouldName_NamesTheSaturatedOnesFirstAndStopsAtTheMost()
    {
        EndpointCircuits circuits = Circuits(connectionsPerEndpoint: 1);

        for (int i = 0; i < EndpointCircuits.MaxRefusing + 10; i++)
        {
            ClaimedDelivery failing = TestDeliveries.Claimed(
                Now + Lease, WebhookEndpointId.New(), EndpointHealth.Failing, consecutiveFailures: 10, failingSince: Now.AddHours(-1));
            circuits.For(failing).TryAdmit(Now).ShouldBe(new Admission.Granted(isProbe: true));
        }

        ClaimedDelivery busy = TestDeliveries.Claimed(Now + Lease);
        circuits.For(busy).TryAdmit(Now).ShouldBe(new Admission.Granted(isProbe: false));

        IReadOnlyCollection<WebhookEndpointId> refusing = circuits.Refusing(Now);

        refusing.Count.ShouldBe(EndpointCircuits.MaxRefusing);
        refusing.First().ShouldBe(busy.EndpointId);
    }

    [Fact]
    public void Find_GivenAnEndpointNotSeen_ReturnsNull()
    {
        Circuits(connectionsPerEndpoint: 8).Find(WebhookEndpointId.New()).ShouldBeNull();
    }

    private static EndpointCircuits Circuits(int connectionsPerEndpoint)
    {
        return new EndpointCircuits(new DeliveryOptions { MaxConnectionsPerEndpoint = connectionsPerEndpoint }, Circuit);
    }
}