using Hookwright.Core.Configuration;
using Hookwright.Core.Events;

namespace Hookwright.Core.Tests.Configuration;

public sealed class SecurityOptionsTests
{
    [Fact]
    public void Validate_GivenTheDefaults_ReportsNoProblems()
    {
        SecurityOptions options = new();

        options.RequireHttps.ShouldBeTrue();
        options.AllowPrivateNetwork.ShouldBeFalse();
        options.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void MaxEventHeaders_ByDefault_MatchesTheStructuralMaximum()
    {
        new SecurityOptions().MaxEventHeaders.ShouldBe(WebhookEvent.MaxHeaders);
    }

    [Fact]
    public void Validate_GivenATighterHeaderLimit_IsAccepted()
    {
        new SecurityOptions() { MaxEventHeaders = WebhookEvent.MaxHeaders - 1 }.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Validate_GivenAHeaderLimitAboveTheStructuralMaximum_Fails()
    {
        SecurityOptions options = new() { MaxEventHeaders = WebhookEvent.MaxHeaders + 1 };

        options.Validate().ShouldHaveSingleItem();
    }

    [Fact]
    public void Validate_GivenTooBigPayloadLimit_Fails()
    {
        SecurityOptions options = new() { MaxPayloadBytes = SecurityOptions.AbsolutePayloadBytes + 1 };

        options.Validate().ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_GivenANonPositivePayloadLimit_Fails(int bytes)
    {
        new SecurityOptions { MaxPayloadBytes = bytes }.Validate().ShouldNotBeEmpty();
    }
}