using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;

namespace Hookwright.Core.Tests.Configuration;

public sealed class RetentionOptionsTests
{
    [Fact]
    public void Validate_GivenTheDefaults_ReportsNoProblems()
    {
        new RetentionOptions().Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GivenAttemptsOutlivingEvents_Fails()
    {
        RetentionOptions options = new()
        {
            Events = TimeSpan.FromDays(7),
            Attempts = TimeSpan.FromDays(30)
        };

        options.Validate().ShouldHaveSingleItem();
    }

    [Fact]
    public void Validate_GivenEqualWindows_IsAccepted()
    {
        RetentionOptions options = new()
        {
            Events = TimeSpan.FromDays(30),
            Attempts = TimeSpan.FromDays(30)
        };

        options.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GivenZeroCaptureLength_IsAccepted()
    {
        RetentionOptions options = new()
        {
            ResponseBodySnippetLength = 0,
            ErrorDetailLength = 0
        };

        options.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GivenCaptureLengthsAboveTheStructuralMaximum_Fails()
    {
        RetentionOptions options = new()
        {
            ResponseBodySnippetLength = DeliveryAttempt.MaxResponseBodySnippetLength + 1,
            ErrorDetailLength = DeliveryAttempt.MaxErrorDetailLength + 1
        };

        options.Validate().Count.ShouldBe(2);
    }

    [Fact]
    public void Validate_GivenNonPositiveWindows_Fails()
    {
        new RetentionOptions { Events = TimeSpan.Zero }.Validate().ShouldNotBeEmpty();
        new RetentionOptions { Attempts = TimeSpan.FromDays(-1) }.Validate().ShouldNotBeEmpty();
    }
}