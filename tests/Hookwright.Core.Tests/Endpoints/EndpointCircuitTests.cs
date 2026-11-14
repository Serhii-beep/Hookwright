using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;

namespace Hookwright.Core.Tests.Endpoints;

public sealed class EndpointCircuitTests
{
    private const int Concurrency = 64;

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DisableAfter = TimeSpan.FromDays(5);

    [Fact]
    public void Constructor_GivenAnEndpointNeverSeenFailing_StartsHealthyAtFullConcurrency()
    {
        EndpointCircuit circuit = Circuit();

        circuit.Health.ShouldBe(EndpointHealth.Healthy);
        circuit.Limit.ShouldBe(Concurrency);
        circuit.InFlight.ShouldBe(0);
        circuit.ConsecutiveFailures.ShouldBe(0);
        circuit.FailingSince.ShouldBeNull();
        circuit.ReopensAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(2, Concurrency, EndpointHealth.Healthy)]
    [InlineData(3, 32, EndpointHealth.Degraded)]
    [InlineData(5, 8, EndpointHealth.Degraded)]
    [InlineData(9, 1, EndpointHealth.Degraded)]
    public void Constructor_GivenPersistedFailures_StartsWhereTheEndpointWas(int failures, int limit, EndpointHealth health)
    {
        EndpointCircuit circuit = Circuit(failures: failures);

        circuit.ConsecutiveFailures.ShouldBe(failures);
        circuit.Limit.ShouldBe(limit);
        circuit.Health.ShouldBe(health);
    }

    [Fact]
    public void Constructor_GivenPersistedFailuresPastTheOpeningPoint_StartsOpenWithAProbeDueAtOnce()
    {
        EndpointCircuit circuit = Circuit(failures: 12, failingSince: Now.AddHours(-1));

        circuit.Health.ShouldBe(EndpointHealth.Failing);
        circuit.FailingSince.ShouldBe(Now.AddHours(-1));
        circuit.TryAdmit(Now).ShouldBe(new Admission.Granted(isProbe: true));
        circuit.TryAdmit(Now).ShouldBe(new Admission.Refused(null));
    }

    [Theory]
    [InlineData(0, 60, 5, 0)]
    [InlineData(64, 0, 5, 0)]
    [InlineData(64, 60, 0, 0)]
    [InlineData(64, 60, 5, -1)]
    public void Constructor_GivenAnImpossibleArgument_Throws(int concurrency, int cooldownSeconds, int disableAfterDays, int failures)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new EndpointCircuit(
            concurrency, TimeSpan.FromSeconds(cooldownSeconds), TimeSpan.FromDays(disableAfterDays), failures, null));
    }

    [Fact]
    public void TryAdmit_Always_GrantsNoMoreThanTheLimitAtOnce()
    {
        EndpointCircuit circuit = Circuit(concurrency: 3);

        Admission.Granted first = circuit.TryAdmit(Now).ShouldBeOfType<Admission.Granted>();
        circuit.TryAdmit(Now).ShouldBe(new Admission.Granted(isProbe: false));
        circuit.TryAdmit(Now).ShouldBe(new Admission.Granted(isProbe: false));
        circuit.TryAdmit(Now).ShouldBe(new Admission.Refused(null));
        circuit.InFlight.ShouldBe(3);

        circuit.Record(first, AttemptOutcome.Succeeded, Now);

        circuit.InFlight.ShouldBe(2);
        circuit.TryAdmit(Now).ShouldBe(new Admission.Granted(isProbe: false));
    }

    [Fact]
    public void Record_GivenFailuresInARow_HalvesTheLimitFromTheThirdToAFloorOfOne()
    {
        EndpointCircuit circuit = Circuit();
        List<int> limits = [];
        List<CircuitChange> changes = [];

        for (int i = 0; i < 9; i++)
        {
            changes.Add(Fail(circuit, Now.AddSeconds(i)));
            limits.Add(circuit.Limit);
        }

        limits.ShouldBe([64, 64, 32, 16, 8, 4, 2, 1, 1]);
        changes.ShouldBe(
        [
            CircuitChange.None,
            CircuitChange.None,
            CircuitChange.Degraded,
            CircuitChange.None,
            CircuitChange.None,
            CircuitChange.None,
            CircuitChange.None,
            CircuitChange.None,
            CircuitChange.None
        ]);
        circuit.Health.ShouldBe(EndpointHealth.Degraded);
        circuit.ConsecutiveFailures.ShouldBe(9);
        circuit.FailingSince.ShouldBe(Now);
    }

    [Fact]
    public void Record_GivenTheTenthFailure_OpensTheCircuitForTheCooldown()
    {
        EndpointCircuit circuit = Circuit(failures: 9);

        Fail(circuit, Now).ShouldBe(CircuitChange.Opened);

        circuit.Health.ShouldBe(EndpointHealth.Failing);
        circuit.ReopensAt.ShouldBe(Now + Cooldown);
        circuit.TryAdmit(Now + Cooldown - TimeSpan.FromTicks(1)).ShouldBe(new Admission.Refused(Now + Cooldown));
    }

    [Fact]
    public void TryAdmit_OnceTheCooldownIsOver_GrantsExactlyOneProbe()
    {
        EndpointCircuit circuit = Open();
        DateTimeOffset reopened = Now + Cooldown;

        circuit.TryAdmit(reopened).ShouldBe(new Admission.Granted(isProbe: true));

        circuit.Health.ShouldBe(EndpointHealth.Recovering);
        circuit.InFlight.ShouldBe(1);
        circuit.TryAdmit(reopened).ShouldBe(new Admission.Refused(null));
    }

    [Fact]
    public void Record_GivenAProbeThatIsAnswered_ClosesTheCircuitAtALimitOfOne()
    {
        EndpointCircuit circuit = Open();
        DateTimeOffset reopened = Now + Cooldown;
        Admission.Granted probe = circuit.TryAdmit(reopened).ShouldBeOfType<Admission.Granted>();

        circuit.Record(probe, AttemptOutcome.Succeeded, reopened).ShouldBe(CircuitChange.Closed);

        circuit.Health.ShouldBe(EndpointHealth.Degraded);
        circuit.Limit.ShouldBe(1);
        circuit.ReopensAt.ShouldBeNull();
        circuit.ConsecutiveFailures.ShouldBe(0);
        circuit.FailingSince.ShouldBeNull();
    }

    [Fact]
    public void Record_GivenAnswersAfterAClose_DoublesTheLimitUntilTheEndpointHasRecovered()
    {
        EndpointCircuit circuit = Closed();
        List<int> limits = [];
        List<CircuitChange> changes = [];

        for (int i = 0; i < 6; i++)
        {
            changes.Add(Answer(circuit, Now.AddMinutes(2).AddSeconds(i)));
            limits.Add(circuit.Limit);
        }

        limits.ShouldBe([2, 4, 8, 16, 32, 64]);
        changes.ShouldBe(
        [
            CircuitChange.None,
            CircuitChange.None,
            CircuitChange.None,
            CircuitChange.None,
            CircuitChange.None,
            CircuitChange.Recovered
        ]);
        circuit.Health.ShouldBe(EndpointHealth.Healthy);
    }

    [Fact]
    public void Record_GivenAProbeThatFails_ReopensTheCircuitForAnotherCooldown()
    {
        EndpointCircuit circuit = Open();
        DateTimeOffset reopened = Now + Cooldown;
        Admission.Granted probe = circuit.TryAdmit(reopened).ShouldBeOfType<Admission.Granted>();

        circuit.Record(probe, AttemptOutcome.ServerError, reopened).ShouldBe(CircuitChange.Opened);

        circuit.Health.ShouldBe(EndpointHealth.Failing);
        circuit.ReopensAt.ShouldBe(reopened + Cooldown);
        circuit.ConsecutiveFailures.ShouldBe(11);
        circuit.TryAdmit(reopened).ShouldBe(new Admission.Refused(reopened + Cooldown));
    }

    [Fact]
    public void Record_GivenAProbeThatCouldNotBeSent_LeavesTheCircuitOpenForTheNextProbe()
    {
        EndpointCircuit circuit = Open();
        DateTimeOffset reopened = Now + Cooldown;
        Admission.Granted probe = circuit.TryAdmit(reopened).ShouldBeOfType<Admission.Granted>();

        circuit.Record(probe, AttemptOutcome.Unsendable, reopened).ShouldBe(CircuitChange.None);

        circuit.Health.ShouldBe(EndpointHealth.Failing);
        circuit.TryAdmit(reopened).ShouldBe(new Admission.Granted(isProbe: true));
    }

    [Fact]
    public void Record_GivenARefusal_CountsItAsAnAnswer()
    {
        EndpointCircuit circuit = Circuit(failures: 5, failingSince: Now.AddMinutes(-5));

        Answer(circuit, Now, AttemptOutcome.Rejected);

        circuit.ConsecutiveFailures.ShouldBe(0);
        circuit.FailingSince.ShouldBeNull();
        circuit.Limit.ShouldBe(16);
    }

    [Theory]
    [InlineData(AttemptOutcome.Gone)]
    [InlineData(AttemptOutcome.Unsendable)]
    public void Record_GivenAnOutcomeThatSaysNothingAboutTheEndpoint_OnlyFreesTheSlot(AttemptOutcome outcome)
    {
        EndpointCircuit circuit = Circuit(failures: 4, failingSince: Now.AddMinutes(-1));
        Admission.Granted granted = circuit.TryAdmit(Now).ShouldBeOfType<Admission.Granted>();

        circuit.Record(granted, outcome, Now).ShouldBe(CircuitChange.None);

        circuit.InFlight.ShouldBe(0);
        circuit.ConsecutiveFailures.ShouldBe(4);
        circuit.FailingSince.ShouldBe(Now.AddMinutes(-1));
        circuit.Limit.ShouldBe(16);
    }

    [Fact]
    public void Record_GivenAResultAdmittedBeforeTheCircuitOpened_LetsOnlyTheProbeSpeak()
    {
        EndpointCircuit circuit = Circuit();
        Admission.Granted[] admitted =
        [
            .. Enumerable.Range(0, 11).Select(_ => circuit.TryAdmit(Now).ShouldBeOfType<Admission.Granted>())
        ];

        for (int i = 0; i < 10; i++)
        {
            circuit.Record(admitted[i], AttemptOutcome.ServerError, Now);
        }

        circuit.Health.ShouldBe(EndpointHealth.Failing);

        circuit.Record(admitted[10], AttemptOutcome.Succeeded, Now.AddSeconds(1)).ShouldBe(CircuitChange.None);

        circuit.Health.ShouldBe(EndpointHealth.Failing);
        circuit.InFlight.ShouldBe(0);
        circuit.ConsecutiveFailures.ShouldBe(10);
    }

    [Fact]
    public void Release_Always_FreesTheSlotAndLearnsNothing()
    {
        EndpointCircuit circuit = Circuit(concurrency: 1);
        Admission.Granted granted = circuit.TryAdmit(Now).ShouldBeOfType<Admission.Granted>();
        circuit.TryAdmit(Now).ShouldBe(new Admission.Refused(null));

        circuit.Release(granted);

        circuit.InFlight.ShouldBe(0);
        circuit.ConsecutiveFailures.ShouldBe(0);
        circuit.TryAdmit(Now).ShouldBe(new Admission.Granted(isProbe: false));
    }

    [Fact]
    public void Record_GivenAFailureAfterFailingForAsLongAsAllowed_ReportsExhaustion()
    {
        EndpointCircuit circuit = Circuit(failures: 4, failingSince: Now - DisableAfter);

        Fail(circuit, Now).ShouldBe(CircuitChange.Exhausted);

        circuit.ConsecutiveFailures.ShouldBe(5);
        circuit.FailingSince.ShouldBe(Now - DisableAfter);
    }

    [Fact]
    public void Record_GivenAFailureJustShortOfTheAllowance_DoesNotReportExhaustion()
    {
        EndpointCircuit circuit = Circuit(failures: 4, failingSince: Now - DisableAfter + TimeSpan.FromTicks(1));

        Fail(circuit, Now).ShouldBe(CircuitChange.None);
    }

    [Fact]
    public void Record_GivenAProbeThatFailsAfterTheAllowance_ReportsExhaustionRatherThanReopening()
    {
        EndpointCircuit circuit = Circuit(failures: 12, failingSince: Now - DisableAfter);
        Admission.Granted probe = circuit.TryAdmit(Now).ShouldBeOfType<Admission.Granted>();

        circuit.Record(probe, AttemptOutcome.TimedOut, Now).ShouldBe(CircuitChange.Exhausted);
    }

    [Fact]
    public void Record_GivenTheFirstFailure_StartsTheFailingClockWhichAnAnswerStops()
    {
        EndpointCircuit circuit = Circuit();

        Fail(circuit, Now);
        circuit.FailingSince.ShouldBe(Now);
        Fail(circuit, Now.AddSeconds(1));
        circuit.FailingSince.ShouldBe(Now);

        Answer(circuit, Now.AddSeconds(2));

        circuit.FailingSince.ShouldBeNull();
        circuit.ConsecutiveFailures.ShouldBe(0);
        circuit.Health.ShouldBe(EndpointHealth.Healthy);
    }

    [Fact]
    public void Health_GivenAConcurrencyOfOne_ShowsDegradedByTheFailureCountAlone()
    {
        EndpointCircuit circuit = Circuit(concurrency: 1);

        for (int i = 0; i < 3; i++)
        {
            Fail(circuit, Now.AddSeconds(i));
        }

        circuit.Limit.ShouldBe(1);
        circuit.Health.ShouldBe(EndpointHealth.Degraded);
    }

    [Fact]
    public void TryAdmit_Always_HoldsTheLimitAcrossThreads()
    {
        EndpointCircuit circuit = Circuit(concurrency: 8);
        int inFlight = 0;
        int peak = 0;

        Parallel.For(0, 10_000, _ =>
        {
            if (circuit.TryAdmit(Now) is not Admission.Granted granted)
            {
                return;
            }

            RaiseTo(ref peak, Interlocked.Increment(ref inFlight));
            Interlocked.Decrement(ref inFlight);
            circuit.Record(granted, AttemptOutcome.Succeeded, Now);
        });

        peak.ShouldBeLessThanOrEqualTo(8);
        circuit.InFlight.ShouldBe(0);
    }

    private static EndpointCircuit Circuit(int concurrency = Concurrency, int failures = 0, DateTimeOffset? failingSince = null)
    {
        return new EndpointCircuit(concurrency, Cooldown, DisableAfter, failures, failingSince);
    }

    private static EndpointCircuit Open()
    {
        EndpointCircuit circuit = Circuit(failures: 9, failingSince: Now.AddMinutes(-1));
        Fail(circuit, Now);

        return circuit;
    }

    private static EndpointCircuit Closed()
    {
        EndpointCircuit circuit = Open();
        DateTimeOffset reopened = Now + Cooldown;
        circuit.Record(circuit.TryAdmit(reopened).ShouldBeOfType<Admission.Granted>(), AttemptOutcome.Succeeded, reopened);

        return circuit;
    }

    private static CircuitChange Fail(EndpointCircuit circuit, DateTimeOffset at)
    {
        return circuit.Record(circuit.TryAdmit(at).ShouldBeOfType<Admission.Granted>(), AttemptOutcome.ServerError, at);
    }

    private static CircuitChange Answer(EndpointCircuit circuit, DateTimeOffset at, AttemptOutcome outcome = AttemptOutcome.Succeeded)
    {
        return circuit.Record(circuit.TryAdmit(at).ShouldBeOfType<Admission.Granted>(), outcome, at);
    }

    private static void RaiseTo(ref int peak, int seen)
    {
        int current = Volatile.Read(ref peak);

        while (current < seen)
        {
            int previous = Interlocked.CompareExchange(ref peak, seen, current);

            if (previous == current)
            {
                return;
            }

            current = previous;
        }
    }
}