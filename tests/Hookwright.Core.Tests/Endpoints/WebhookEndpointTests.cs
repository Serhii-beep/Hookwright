using Hookwright.Core.Endpoints;
using Hookwright.Core.Subscribers;

namespace Hookwright.Core.Tests.Endpoints;

public sealed class WebhookEndpointTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Uri ValidUrl = new("https://example.test/hooks");

    private static WebhookEndpoint CreateEndpoint() => WebhookEndpoint.Create(SubscriberId.New(), ValidUrl, "Primary", Now);

    [Fact]
    public void Create_GivenValidInput_InitialisesAHealthyUnverifiedEndpoint()
    {
        WebhookEndpoint endpoint = CreateEndpoint();

        endpoint.Url.ShouldBe(ValidUrl);
        endpoint.Description.ShouldBe("Primary");
        endpoint.Health.ShouldBe(EndpointHealth.Healthy);
        endpoint.IsEnabled.ShouldBeTrue();
        endpoint.IsVerified.ShouldBeFalse();
        endpoint.PartitionMode.ShouldBe(PartitionMode.None);
        endpoint.RateLimitPerSecond.ShouldBeNull();
        endpoint.ConsecutiveFailures.ShouldBe(0);
        endpoint.EventTypeFilter.ShouldBeEmpty();
        endpoint.CreatedAt.ShouldBe(Now);
        endpoint.UpdatedAt.ShouldBe(Now);
    }

    [Theory]
    [InlineData("ftp://example.test/hooks")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:pass@example.test/h")]
    [InlineData("https://example.test/h#fragment")]
    public void Create_GivenUnacceptableUrl_Throws(string url)
    {
        Should.Throw<ArgumentException>(() => WebhookEndpoint.Create(SubscriberId.New(), new Uri(url), null, Now));
    }

    [Fact]
    public void Create_GivenRelativeUrl_Throws()
    {
        Should.Throw<ArgumentException>(() => WebhookEndpoint.Create(SubscriberId.New(), new Uri("/hooks", UriKind.Relative), null, Now));
    }

    [Fact]
    public void ChangeUrl_Always_ResetsVerification()
    {
        WebhookEndpoint endpoint = CreateEndpoint();
        endpoint.MarkVerified(Now);

        endpoint.ChangeUrl(new Uri("https://elsewhere.test/hooks"), Now.AddMinutes(5));

        endpoint.IsVerified.ShouldBeFalse();
        endpoint.UpdatedAt.ShouldBe(Now.AddMinutes(5));
    }

    [Fact]
    public void ChangeUrl_GivenTheSameUrl_KeepsVerification()
    {
        WebhookEndpoint endpoint = CreateEndpoint();
        endpoint.MarkVerified(Now);

        endpoint.ChangeUrl(ValidUrl, Now.AddMinutes(5));

        endpoint.IsVerified.ShouldBeTrue();
    }

    [Fact]
    public void SetEventTypeFilter_GivenPatterns_NormalisesAndDeduplicates()
    {
        WebhookEndpoint endpoint = CreateEndpoint();

        endpoint.SetEventTypeFilter(["   order.created   ", "order.*", "order.created"], Now);

        endpoint.EventTypeFilter.ShouldBe(["order.created", "order.*"]);
    }

    [Theory]
    [InlineData("order..created")]
    [InlineData("order-created")]
    [InlineData("*.created")]
    [InlineData("")]
    public void SetEventTypeFilter_GivenMalformedPattern_Throws(string pattern)
    {
        WebhookEndpoint endpoint = CreateEndpoint();

        Should.Throw<ArgumentException>(() => endpoint.SetEventTypeFilter([pattern], Now));
    }

    [Fact]
    public void SetEventTypeFilter_GivenNull_SubscribesToEverything()
    {
        WebhookEndpoint endpoint = CreateEndpoint();
        endpoint.SetEventTypeFilter(["order.created"], Now);

        endpoint.SetEventTypeFilter(null, Now);

        endpoint.EventTypeFilter.ShouldBeEmpty();
    }

    [Fact]
    public void SetDeliverySettings_GivenNonPositiveRateLimit_Throws()
    {
        WebhookEndpoint endpoint = CreateEndpoint();

        Should.Throw<ArgumentOutOfRangeException>(() => endpoint.SetDeliverySettings(PartitionMode.None, 0, Now));
    }

    [Fact]
    public void Disable_GivenAnAlreadyDisabledEndpoint_KeepsTheOriginalReasonAndTimestamp()
    {
        WebhookEndpoint endpoint = CreateEndpoint();

        endpoint.Disable("Returned 410 Gone", Now.AddHours(1));
        endpoint.Disable("Disabled by owner", Now.AddHours(9));

        endpoint.Health.ShouldBe(EndpointHealth.Disabled);
        endpoint.IsEnabled.ShouldBeFalse();
        endpoint.DisabledReason.ShouldBe("Returned 410 Gone");
        endpoint.DisabledAt.ShouldBe(Now.AddHours(1));
    }

    [Fact]
    public void Disable_GivenNoReason_Throws()
    {
        WebhookEndpoint endpoint = CreateEndpoint();

        Should.Throw<ArgumentException>(() => endpoint.Disable("   ", Now));
    }

    [Fact]
    public void Enable_GivenADisabledEndpoint_ClearsFailureState()
    {
        WebhookEndpoint endpoint = CreateEndpoint();
        endpoint.Disable("Returned 410 Gone", Now);

        endpoint.Enable(Now.AddHours(1));

        endpoint.Health.ShouldBe(EndpointHealth.Healthy);
        endpoint.IsEnabled.ShouldBeTrue();
        endpoint.ConsecutiveFailures.ShouldBe(0);
        endpoint.DisabledReason.ShouldBeNull();
        endpoint.DisabledAt.ShouldBeNull();
    }
}