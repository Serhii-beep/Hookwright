using Hookwright.Core.Configuration;

namespace Hookwright.Core.Tests.Configuration;

public sealed class HookwrightOptionsTests
{
    [Fact]
    public void Validate_GivenTheDefaults_ReportsNoProblems()
    {
        new HookwrightOptions().Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Validate_Always_NamesTheSectionEachProblemCameFrom()
    {
        HookwrightOptions options = new();
        options.Delivery.MaxConcurrency = 0;

        options.Validate().ShouldHaveSingleItem().ShouldStartWith("Delivery: ");
    }

    [Fact]
    public void Validate_GivenProblemsInSeveralSections_ReportsThemAll()
    {
        HookwrightOptions options = new();
        options.Delivery.BatchSize = 0;
        options.Retry.MaxDelay = TimeSpan.Zero;
        options.Security.MaxPayloadBytes = 0;
        options.Retention.Events = TimeSpan.Zero;

        IReadOnlyList<string> problems = options.Validate();

        problems.Count.ShouldBeGreaterThanOrEqualTo(4);
        problems.ShouldContain(p => p.StartsWith("Delivery: ", StringComparison.Ordinal));
        problems.ShouldContain(p => p.StartsWith("Retry: ", StringComparison.Ordinal));
        problems.ShouldContain(p => p.StartsWith("Security: ", StringComparison.Ordinal));
        problems.ShouldContain(p => p.StartsWith("Retention: ", StringComparison.Ordinal));
    }
}