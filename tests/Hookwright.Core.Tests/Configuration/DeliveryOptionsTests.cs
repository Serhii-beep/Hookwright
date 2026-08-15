using Hookwright.Core.Configuration;

namespace Hookwright.Core.Tests.Configuration;

public sealed class DeliveryOptionsTests
{
    [Fact]
    public void Validate_GivenTheDefaults_ReportsNoProblems()
    {
        new DeliveryOptions().Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GivenALeaseShorterThanTheTimeout_Fails()
    {
        DeliveryOptions options = new()
        {
            RequestTimeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromSeconds(10)
        };

        string problem = options.Validate().ShouldHaveSingleItem();

        problem.ShouldContain(nameof(DeliveryOptions.LeaseDuration));
        problem.ShouldContain(nameof(DeliveryOptions.RequestTimeout));
    }

    [Fact]
    public void Validate_GivenALeaseEqualToTheTimeout_Fails()
    {
        DeliveryOptions options = new()
        {
            RequestTimeout = TimeSpan.FromSeconds(30),
            LeaseDuration = TimeSpan.FromSeconds(30)
        };

        options.Validate().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_GivenNonPositiveCounts_ReportsEachOne(int value)
    {
        DeliveryOptions options = new()
        {
            MaxConcurrency = value,
            BatchSize = value,
            MaxConnectionsPerEndpoint = value
        };

        IReadOnlyList<string> problems = options.Validate();

        problems.ShouldContain(p => p.Contains(nameof(DeliveryOptions.MaxConcurrency), StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains(nameof(DeliveryOptions.BatchSize), StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains(nameof(DeliveryOptions.MaxConnectionsPerEndpoint), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_GivenSeveralProblems_ReportsThemAll()
    {
        DeliveryOptions options = new()
        {
            MaxConcurrency = 0,
            PollInterval = TimeSpan.Zero,
            RequestTimeout = TimeSpan.FromMinutes(5),
            LeaseDuration = TimeSpan.FromSeconds(1)
        };

        options.Validate().Count.ShouldBe(3);
    }

    [Fact]
    public void Validate_GivenNonPositiveDurations_Fails()
    {
        new DeliveryOptions { PollInterval = TimeSpan.Zero }.Validate().ShouldNotBeEmpty();
        new DeliveryOptions { RequestTimeout = TimeSpan.Zero }.Validate().ShouldNotBeEmpty();
        new DeliveryOptions { PollInterval = TimeSpan.Zero }.Validate().ShouldNotBeEmpty();
    }
}