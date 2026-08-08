using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class DefaultRetryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static DefaultRetryPolicy CreatePolicy(TimeSpan? maxDelay = null) =>
        new(new RetrySchedule(RetrySchedule.Default.Steps, jitterFactor: 0), new Random(1), maxDelay);

    [Fact]
    public void Decide_GivenSuccess_ReturnsSucceeded()
    {
        CreatePolicy().Decide(AttemptOutcome.Succeeded, 1, null, Now)
            .ShouldBeOfType<RetryDecision.Succeeded>();
    }

    [Fact]
    public void Decide_GivenGone_RetiresTheEndpoint()
    {
        CreatePolicy().Decide(AttemptOutcome.Gone, 1, null, Now)
            .ShouldBeOfType<RetryDecision.RetireEndpoint>();
    }

    [Fact]
    public void Decide_GivenARefusal_Fails()
    {
        CreatePolicy().Decide(AttemptOutcome.Rejected, 1, null, Now)
            .ShouldBeOfType<RetryDecision.Fail>();
    }

    [Theory]
    [InlineData(AttemptOutcome.ServerError)]
    [InlineData(AttemptOutcome.Throttled)]
    [InlineData(AttemptOutcome.TimedOut)]
    [InlineData(AttemptOutcome.ConnectionFailed)]
    [InlineData(AttemptOutcome.Blocked)]
    public void Decide_GivenARetryableOutcomeWithAttemptsLeft_SchedulesTheNextAttempt(AttemptOutcome outcome)
    {
        RetryDecision decision = CreatePolicy().Decide(outcome, 1, null, Now);

        decision.ShouldBeOfType<RetryDecision.Retry>().NextAttemptAt.ShouldBe(Now + TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Decide_Always_WalksTheLadderInOrder()
    {
        DefaultRetryPolicy policy = CreatePolicy();
        IReadOnlyList<TimeSpan> steps = RetrySchedule.Default.Steps;

        for (int completed = 1; completed <= steps.Count; completed++)
        {
            policy.Decide(AttemptOutcome.ServerError, completed, null, Now)
                .ShouldBeOfType<RetryDecision.Retry>()
                .NextAttemptAt.ShouldBe(Now + steps[completed - 1], $"after {completed} attempts");
        }
    }

    [Fact]
    public void Decide_AtTheExhaustionBoundary_SwitchesFromRetryToExhausted()
    {
        DefaultRetryPolicy policy = CreatePolicy();

        RetrySchedule.Default.MaxAttempts.ShouldBe(10);

        policy.Decide(AttemptOutcome.ServerError, 9, null, Now).ShouldBeOfType<RetryDecision.Retry>();
        policy.Decide(AttemptOutcome.ServerError, 10, null, Now).ShouldBeOfType<RetryDecision.Exhausted>();
        policy.Decide(AttemptOutcome.ServerError, 99, null, Now).ShouldBeOfType<RetryDecision.Exhausted>();
    }

    [Fact]
    public void Decide_GivenALongerRequestedDelay_HonoursIt()
    {
        RetryDecision decision = CreatePolicy().Decide(
            AttemptOutcome.Throttled, 1, TimeSpan.FromMinutes(30), Now);

        decision.ShouldBeOfType<RetryDecision.Retry>()
            .NextAttemptAt.ShouldBe(Now + TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Decide_GivenAShorterRequestedDelay_IgnoresIt()
    {
        RetryDecision decision = CreatePolicy().Decide(AttemptOutcome.Throttled, 1, TimeSpan.FromMilliseconds(1), Now);

        decision.ShouldBeOfType<RetryDecision.Retry>().NextAttemptAt.ShouldBe(Now + TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Decide_GivenAnExcessiveRequestedDelay_CapsIt()
    {
        RetryDecision decision = CreatePolicy(TimeSpan.FromHours(6)).Decide(
            AttemptOutcome.Throttled, 1, TimeSpan.FromDays(365), Now);

        decision.ShouldBeOfType<RetryDecision.Retry>().NextAttemptAt.ShouldBe(Now + TimeSpan.FromHours(6));
    }

    [Fact]
    public void Decide_GivenASaturatedRequestedDelay_DoesNotOverflow()
    {
        RetryDecision decision = CreatePolicy().Decide(
            AttemptOutcome.Throttled, 1, TimeSpan.MaxValue, Now);

        decision.ShouldBeOfType<RetryDecision.Retry>()
            .NextAttemptAt.ShouldBe(Now + DefaultRetryPolicy.DefaultMaxDelay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Decide_GivenNoCompletedAttempts_Throws(int completedAttempts)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => CreatePolicy().Decide(AttemptOutcome.ServerError, completedAttempts, null, Now));
    }

    [Fact]
    public void Decide_Always_ProducesADecisionForEveryOutcome()
    {
        DefaultRetryPolicy policy = CreatePolicy();

        foreach (AttemptOutcome outcome in Enum.GetValues<AttemptOutcome>())
        {
            policy.Decide(outcome, 1, null, Now).ShouldNotBeNull(outcome.ToString());
        }
    }

    [Fact]
    public void Constructor_GivenANonPositiveCap_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new DefaultRetryPolicy(RetrySchedule.Default, new Random(1), TimeSpan.Zero));
    }
}