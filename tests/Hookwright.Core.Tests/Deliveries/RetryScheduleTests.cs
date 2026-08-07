using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Tests.Deliveries;

public sealed class RetryScheduleTests
{
    private static Random Seeded() => new(Seed: 20260807);

    [Fact]
    public void DelayAfter_GivenNoJitter_ReturnsTheLadderExactly()
    {
        RetrySchedule schedule = new(RetrySchedule.Default.Steps, jitterFactor: 0);

        for (int attempt = 1; attempt <= schedule.Steps.Count; attempt++)
        {
            schedule.DelayAfter(attempt, Seeded()).ShouldBe(schedule.Steps[attempt - 1]);
        }
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(0.25)]
    public void DelayAfter_GivenJitter_StaysWithinTheExpectedWindow(double factor)
    {
        RetrySchedule schedule = new([TimeSpan.FromHours(1)], factor);
        Random random = Seeded();

        for (int i = 0; i < 1000; i++)
        {
            TimeSpan delay = schedule.DelayAfter(1, random);

            delay.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromHours(1) * (1 - factor));
            delay.ShouldBeLessThanOrEqualTo(TimeSpan.FromHours(1));
        }
    }

    [Fact]
    public void DelayAfter_GivenTheSameSeed_IsReproductible()
    {
        RetrySchedule schedule = RetrySchedule.Default;

        TimeSpan[] first = [.. Enumerable.Range(0, 50).Select(_ => schedule.DelayAfter(1, Seeded()))];
        TimeSpan[] second = [.. Enumerable.Range(0, 50).Select(_ => schedule.DelayAfter(1, Seeded()))];

        first.ShouldBe(second);
    }

    [Fact]
    public void DelayAfter_GivenManyDeliveries_SpreadsThemAcrossTheWindow()
    {
        RetrySchedule schedule = new([TimeSpan.FromMinutes(60)], jitterFactor: 1.0);
        Random random = Seeded();

        double[] minutes = [.. Enumerable.Range(0, 1000).Select(_ => schedule.DelayAfter(1, random).TotalMinutes)];

        minutes.Distinct().Count().ShouldBeGreaterThan(minutes.Length * 9 / 10);
        minutes.Average().ShouldBeInRange(25, 35);
        minutes.Min().ShouldBeLessThan(5);
        minutes.Max().ShouldBeGreaterThan(55);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10)]
    public void DelayAfter_GivenAnAttemptOutsideTheLadder_Throws(int completedAttempts)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RetrySchedule.Default.DelayAfter(completedAttempts, Seeded()));
    }

    [Fact]
    public void Constructor_GivenAnEmptyLadder_Throws()
    {
        Should.Throw<ArgumentException>(() => new RetrySchedule([]));
    }

    [Fact]
    public void Constructor_GivenANegativeDelay_Throws()
    {
        Should.Throw<ArgumentException>(() => new RetrySchedule([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(-1)]));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Constructor_GivenAnInvalidJitterFactor_Throws(double factor)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new RetrySchedule([TimeSpan.FromSeconds(5)], factor));
    }

    [Fact]
    public void Steps_Always_IsIsolatedFromTheCaller()
    {
        List<TimeSpan> mutable = [TimeSpan.FromSeconds(5)];
        RetrySchedule schedule = new(mutable);

        mutable.Add(TimeSpan.FromHours(99));

        schedule.Steps.Count.ShouldBe(1);
    }
}