using Hookwright.Core.Events;
using Hookwright.Core.Subscribers;

namespace Hookwright.Core.Tests.Events;

public sealed class WebhookEventTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Payload = """{"order_id":"o_1","total":75.3}""";

    private static WebhookEvent CreateEvent(
        string? partitionKey = null,
        string? idempotencyKey = null,
        IReadOnlyDictionary<string, string>? headers = null) =>
            WebhookEvent.Create(SubscriberId.New(), "order.created", Payload, Now, partitionKey, idempotencyKey, headers);

    [Fact]
    public void Create_GivenValidInput_InitialisesTheEvent()
    {
        WebhookEvent webhookEvent = CreateEvent();

        webhookEvent.Id.Value.ShouldNotBe(Guid.Empty);
        webhookEvent.Type.ShouldBe("order.created");
        webhookEvent.Payload.ShouldBe(Payload);
        webhookEvent.CreatedAt.ShouldBe(Now);
        webhookEvent.PartitionKey.ShouldBeNull();
        webhookEvent.IdempotencyKey.ShouldBeNull();
        webhookEvent.Headers.ShouldBeEmpty();
    }

    [Fact]
    public void Create_Always_StoresThePayloadVerbatism()
    {
        const string payload = """{ "a" : 1,"b":[2,   3] }""";

        WebhookEvent webhookEvent = WebhookEvent.Create(SubscriberId.New(), "order.created", payload, Now);

        webhookEvent.Payload.ShouldBe(payload);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_GivenMissingPayload_Throws(string? payload)
    {
        Should.Throw<ArgumentException>(() => WebhookEvent.Create(SubscriberId.New(), "order.created", payload!, Now));
    }

    [Theory]
    [InlineData("order..created")]
    [InlineData("order-created")]
    [InlineData("")]
    public void Create_GivenMalformedType_Throws(string type)
    {
        Should.Throw<ArgumentException>(() => WebhookEvent.Create(SubscriberId.New(), type, Payload, Now));
    }

    [Fact]
    public void Create_GivenPaddedKeys_TrimsThem()
    {
        WebhookEvent webhookEvent = CreateEvent(partitionKey: "   o_1   ", idempotencyKey: " k_1");

        webhookEvent.PartitionKey.ShouldBe("o_1");
        webhookEvent.IdempotencyKey.ShouldBe("k_1");
    }

    [Fact]
    public void Create_GivenOverlongPartitionKey_Throws()
    {
        string partitionKey = new('a', WebhookEvent.MaxPartitionKeyLength + 1);

        Should.Throw<ArgumentException>(() => CreateEvent(partitionKey: partitionKey));
    }

    [Fact]
    public void Create_GivenOverlongIdempotencyKey_Throws()
    {
        string idempotencyKey = new('a', WebhookEvent.MaxIdempotencyKeyLength + 1);

        Should.Throw<ArgumentException>(() => CreateEvent(idempotencyKey: idempotencyKey));
    }

    [Fact]
    public void Headers_Always_MatchNamesCaseInsensitively()
    {
        WebhookEvent webhookEvent = CreateEvent(
            headers: new Dictionary<string, string> { ["X-Tenant"] = "acme" });

        webhookEvent.Headers["x-tenant"].ShouldBe("acme");
    }

    [Theory]
    [InlineData("acme\r\nX-Admin: true")]
    [InlineData("acme\nX-Admin: true")]
    [InlineData("acme\ttab")]
    [InlineData("acme\0null")]
    public void Create_GivenControlCharactersInHeaderValue_Throws(string value)
    {
        Should.Throw<ArgumentException>(() => CreateEvent(
            headers: new Dictionary<string, string> { ["X-Tenant"] = value }));
    }

    [Theory]
    [InlineData("X Tenant")]
    [InlineData("X:Tenant")]
    [InlineData("X\r\nTenant")]
    [InlineData("")]
    public void Create_GivenMalformedHeaderName_Throws(string name)
    {
        Should.Throw<ArgumentException>(() => CreateEvent(
            headers: new Dictionary<string, string> { [name] = "acme" }));
    }

    [Fact]
    public void Create_GivenTooManyHeaders_Throws()
    {
        Dictionary<string, string> headers = Enumerable
            .Range(0, WebhookEvent.MaxHeaders + 1)
            .ToDictionary(index => $"X-Header-{index}", _ => "value");

        Should.Throw<ArgumentException>(() => CreateEvent(headers: headers));
    }
}