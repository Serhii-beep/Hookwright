using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class AttemptOutcomesTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(202)]
    [InlineData(204)]
    [InlineData(299)]
    public void FromStatusCode_GivenSuccess_ReturnsSucceeded(int statusCode)
    {
        AttemptOutcomes.FromStatusCode(statusCode).ShouldBe(AttemptOutcome.Succeeded);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(502)]
    [InlineData(504)]
    public void FromStatusCode_GivenBackpressure_ReturnsThrottled(int statusCode)
    {
        AttemptOutcomes.FromStatusCode(statusCode).ShouldBe(AttemptOutcome.Throttled);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(599)]
    public void FromStatusCode_GivenServerFailure_ReturnsServerError(int statusCode)
    {
        AttemptOutcomes.FromStatusCode(statusCode).ShouldBe(AttemptOutcome.ServerError);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(422)]
    public void FromStatusCode_GivenRefusal_ReturnsRejected(int statusCode)
    {
        AttemptOutcomes.FromStatusCode(statusCode).ShouldBe(AttemptOutcome.Rejected);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    public void FromStatusCode_GivenARedirect_ReturnsRejected(int statusCode)
    {
        AttemptOutcomes.FromStatusCode(statusCode).ShouldBe(AttemptOutcome.Rejected);
    }

    [Fact]
    public void FromStatusCode_Given410_ReturnsGoneAndRetiresTheEndpoint()
    {
        AttemptOutcome outcome = AttemptOutcomes.FromStatusCode(410);

        outcome.ShouldBe(AttemptOutcome.Gone);
        AttemptOutcomes.RetiresEndpoint(outcome).ShouldBeTrue();
        AttemptOutcomes.IsRetryable(outcome).ShouldBeFalse();
    }

    [Fact]
    public void FromStatusCode_Never_ProducesATransportOnlyOutcome()
    {
        AttemptOutcome[] transportOnly =
        [
            AttemptOutcome.TimedOut,
            AttemptOutcome.ConnectionFailed,
            AttemptOutcome.Blocked
        ];

        for (int statusCode = 100; statusCode <= 599; statusCode++)
        {
            transportOnly.ShouldNotContain(AttemptOutcomes.FromStatusCode(statusCode), $"status {statusCode}");
        }
    }

    [Fact]
    public void IsRetryable_Always_DecidesEveryDefinedOutcome()
    {
        foreach (AttemptOutcome outcome in Enum.GetValues<AttemptOutcome>())
        {
            Should.NotThrow(() => AttemptOutcomes.IsRetryable(outcome));
        }
    }

    [Fact]
    public void IsRetryable_GivenAnUndefinedOutcome_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => AttemptOutcomes.IsRetryable((AttemptOutcome)short.MaxValue));
    }

    [Fact]
    public void IsRetryable_GivenSuccess_IsFalse()
    {
        AttemptOutcomes.IsRetryable(AttemptOutcome.Succeeded).ShouldBeFalse();
    }

    [Fact]
    public void RetiresEndpoint_Always_AppliesOnlyToGone()
    {
        foreach (AttemptOutcome outcome in Enum.GetValues<AttemptOutcome>())
        {
            AttemptOutcomes.RetiresEndpoint(outcome)
                .ShouldBe(outcome is AttemptOutcome.Gone, outcome.ToString());
        }
    }
}