using Hookwright.Core.Configuration;

namespace Hookwright.Core.Tests.Configuration;

public sealed class CircuitOptionsTests
{
    [Fact]
    public void Validate_GivenTheDefaults_ReportsNoProblems()
    {
        new CircuitOptions().Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GivenNonPositiveDurations_ReportsEachOne()
    {
        CircuitOptions options = new()
        {
            Cooldown = TimeSpan.Zero,
            DisableAfter = TimeSpan.FromSeconds(-1)
        };

        IReadOnlyList<string> problems = options.Validate();

        problems.ShouldContain(p => p.StartsWith(nameof(CircuitOptions.Cooldown), StringComparison.Ordinal));
        problems.ShouldContain(p => p.StartsWith(nameof(CircuitOptions.DisableAfter), StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_GivenAnAllowanceNoLongerThanTheCooldown_Fails()
    {
        CircuitOptions options = new()
        {
            Cooldown = TimeSpan.FromMinutes(5),
            DisableAfter = TimeSpan.FromMinutes(5)
        };

        string problem = options.Validate().ShouldHaveSingleItem();

        problem.ShouldContain(nameof(CircuitOptions.DisableAfter));
        problem.ShouldContain(nameof(CircuitOptions.Cooldown));
    }
}