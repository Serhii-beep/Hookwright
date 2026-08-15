using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Tests.Configuration;

public sealed class RetryOptionsTests
{
    [Fact]
    public void Validate_GivenTheDefaults_ReportsNoProblems()
    {
        RetryOptions options = new();

        options.Schedule.ShouldBeSameAs(RetrySchedule.Default);
        options.MaxDelay.ShouldBe(DefaultRetryPolicy.DefaultMaxDelay);
        options.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GivenANonPositiveMaxDelay_Fails()
    {
        new RetryOptions { MaxDelay = TimeSpan.Zero }.Validate().ShouldNotBeEmpty();
    }

    [Fact]
    public void Schedule_GivenAnImpossibleLadder_ThrowsAtAssignment()
    {
        Should.Throw<ArgumentException>(() => new RetryOptions { Schedule = new RetrySchedule([]) });
    }
}