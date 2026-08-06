using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Subscribers;

namespace Hookwright.Core.Tests.Architecture;

public sealed class PersistedEnumTests
{
    private static readonly Type[] PersistedEnums =
    [
        .. typeof(DeliveryState).Assembly
            .GetExportedTypes()
            .Where(type => type.IsEnum && type.Namespace?.StartsWith("Hookwright.Core.", StringComparison.Ordinal) is true)
            .OrderBy(type => type.Name, StringComparer.Ordinal)
    ];

    [Theory]
    [InlineData(DeliveryState.Pending, 0)]
    [InlineData(DeliveryState.InFlight, 1)]
    [InlineData(DeliveryState.Succeeded, 2)]
    [InlineData(DeliveryState.Failed, 3)]
    [InlineData(DeliveryState.Dead, 4)]
    [InlineData(DeliveryState.Cancelled, 5)]
    [InlineData(DeliveryState.Blocked, 6)]
    public void DeliveryState_Always_KeepsItsPersistedNumber(DeliveryState state, short expected)
    {
        ((short)state).ShouldBe(expected);
    }

    [Theory]
    [InlineData(EndpointHealth.Healthy, 0)]
    [InlineData(EndpointHealth.Degraded, 1)]
    [InlineData(EndpointHealth.Failing, 2)]
    [InlineData(EndpointHealth.Recovering, 3)]
    [InlineData(EndpointHealth.Disabled, 4)]
    public void EndpointHealth_Always_KeepsItsPersistedNumber(EndpointHealth health, short expected)
    {
        ((short)health).ShouldBe(expected);
    }

    [Theory]
    [InlineData(PartitionMode.None, 0)]
    [InlineData(PartitionMode.ByKey, 1)]
    [InlineData(PartitionMode.ByEndpoint, 2)]
    public void PartitionMode_Always_KeepsItsPersistedNumber(PartitionMode mode, short expected)
    {
        ((short)mode).ShouldBe(expected);
    }

    [Theory]
    [InlineData(AttemptOutcome.Succeeded, 0)]
    [InlineData(AttemptOutcome.Rejected, 1)]
    [InlineData(AttemptOutcome.ServerError, 2)]
    [InlineData(AttemptOutcome.Throttled, 3)]
    [InlineData(AttemptOutcome.Gone, 4)]
    [InlineData(AttemptOutcome.TimedOut, 5)]
    [InlineData(AttemptOutcome.ConnectionFailed, 6)]
    [InlineData(AttemptOutcome.Blocked, 7)]
    public void AttemptOutcome_Always_KeepsItsPersistedNumber(AttemptOutcome outcome, short expected)
    {
        ((short)outcome).ShouldBe(expected);
    }

    [Theory]
    [InlineData(SignatureAlgorithm.HmacSha256, 0)]
    [InlineData(SignatureAlgorithm.Ed25519, 1)]
    public void SignatureAlgorithm_Always_KeepsItsPersistedNumber(SignatureAlgorithm algorithm, short expected)
    {
        ((short)algorithm).ShouldBe(expected);
    }

    [Fact]
    public void PersistedEnums_Always_UseShortAsUnderlyingType()
    {
        foreach (Type type in PersistedEnums)
        {
            Enum.GetUnderlyingType(type).ShouldBe(typeof(short), $"{type.Name} must stay smallint-backed");
        }
    }

    [Fact]
    public void PersistedEnums_Always_HaveDistinctValues()
    {
        foreach (Type type in PersistedEnums)
        {
            Array values = Enum.GetValuesAsUnderlyingType(type);

            values.Cast<short>().Distinct().Count().ShouldBe(values.Length, $"{type.Name} has duplicate values.");
        }
    }

    [Fact]
    public void PersistedEnums_Always_DefineZero()
    {
        foreach (Type type in PersistedEnums)
        {
            Enum.IsDefined(type, (short)0).ShouldBeTrue($"{type.Name} must define a zero value");
        }
    }

    [Fact]
    public void PersistedEnums_Always_DiscoversSomething()
    {
        // Without this a renamed namespace or a typo in the filter would
        // leave every test iterating an empty sequence and always passing
        PersistedEnums.ShouldNotBeEmpty();
    }
}