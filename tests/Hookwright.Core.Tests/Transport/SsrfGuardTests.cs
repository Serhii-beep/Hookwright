using System.Net;

using Hookwright.Core.Configuration;
using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Tests.Transport.Helpers;
using Hookwright.Core.Transport;
using Hookwright.Signing;

namespace Hookwright.Core.Tests.Transport;

public sealed class SsrfGuardTests
{
    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly HmacSha256Signer Signer = new();

    private static readonly PlaintextSecretProtector Protector = new();

    [Fact]
    public async Task ConnectAsync_GivenANameThatRebinds_ConnectsOnlyToTheAddressItChecked()
    {
        ReceivedRequest? received = null;

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(async context =>
        {
            received = await ReceivedRequest.ReadAsync(context.Request);
        });

        StubHostResolver resolver = new([IPAddress.Loopback], [IPAddress.Parse("169.254.169.254")]);
        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(new DeliveryOptions(), Guards.Development(resolver));
        using HttpDeliveryTransport transport = Transport(handler);

        Uri destination = new UriBuilder(endpoint.Url) { Host = "rebinding.test" }.Uri;

        DeliveryResult result = await transport.SendAsync(Delivery(destination), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Succeeded);
        resolver.Lookups.ShouldBe(1);
        received.ShouldNotBeNull();
        received.Headers["host"].ShouldBe($"rebinding.test:{endpoint.Url.Port}");
    }

    [Fact]
    public async Task ConnectAsync_GivenANameWithOneRefusedAddressAmongAllowedOnes_Blocks()
    {
        bool reached = false;

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(_ =>
        {
            reached = true;

            return Task.CompletedTask;
        });

        StubHostResolver resolver = new([IPAddress.Loopback, IPAddress.Parse("10.0.0.5")]);
        SecurityOptions security = new()
        {
            RequireHttps = false,
            AllowedNetworks = ["127.0.0.0/8"],
            AllowedPorts = [endpoint.Url.Port]
        };
        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(new DeliveryOptions(), Guards.Strict(resolver, security));
        using HttpDeliveryTransport transport = Transport(handler);

        Uri destination = new UriBuilder(endpoint.Url) { Host = "mixed.test" }.Uri;

        DeliveryResult result = await transport.SendAsync(Delivery(destination), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Blocked);
        result.Attempt.ErrorDetail.ShouldBe("mixed.test resolves to a private network address, which deliveries may not reach.");

        reached.ShouldBeFalse();
    }

    [Fact]
    public async Task ConnectAsync_GivenLoopbackWithoutADevelopmentAllowance_Blocks()
    {
        bool reached = false;

        await using LoopbackEndpoint endpoint = await LoopbackEndpoint.StartAsync(_ =>
        {
            reached = true;

            return Task.CompletedTask;
        });

        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(
            new DeliveryOptions(), Guards.Strict(security: new SecurityOptions
            {
                RequireHttps = false,
                AllowedPorts = [endpoint.Url.Port]
            }));
        using HttpDeliveryTransport transport = Transport(handler);

        DeliveryResult result = await transport.SendAsync(Delivery(endpoint.Url), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Blocked);
        result.Attempt.ErrorDetail.ShouldBe("127.0.0.1 resolves to a loopback address, which deliveries may not reach.");

        reached.ShouldBeFalse();
    }

    [Fact]
    public async Task ConnectAsync_GivenPlainHttpUnderTheDefaults_BlocksBeforeResolving()
    {
        StubHostResolver resolver = new([IPAddress.Loopback]);
        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(new DeliveryOptions(), Guards.Strict(resolver));
        using HttpDeliveryTransport transport = Transport(handler);

        DeliveryResult result = await transport.SendAsync(
            Delivery(new Uri("http://example.test/hooks")), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Blocked);
        result.Attempt.ErrorDetail.ShouldNotBeNull().ShouldContain(nameof(SecurityOptions.RequireHttps));
        resolver.Lookups.ShouldBe(0);
    }

    [Fact]
    public async Task ConnectAsync_GivenAnotherPortUnderTheDefaults_BlocksBeforeResolving()
    {
        StubHostResolver resolver = new([IPAddress.Loopback]);
        using SocketsHttpHandler handler = DeliveryHttpHandler.Create(new DeliveryOptions(), Guards.Strict(resolver));
        using HttpDeliveryTransport transport = Transport(handler);

        DeliveryResult result = await transport.SendAsync(
            Delivery(new Uri("https://example.test:6379/hooks")), Worker, Now, TestContext.Current.CancellationToken);

        result.Attempt.Outcome.ShouldBe(AttemptOutcome.Blocked);
        result.Attempt.ErrorDetail.ShouldNotBeNull().ShouldContain("port 6379");
        resolver.Lookups.ShouldBe(0);
    }

    private static HttpDeliveryTransport Transport(SocketsHttpHandler handler)
    {
        return new HttpDeliveryTransport(handler, Signer, Protector, new DeliveryOptions(), new RetentionOptions());
    }

    private static ClaimedDelivery Delivery(Uri url)
    {
        WebhookEndpointId endpointId = WebhookEndpointId.New();

        return new ClaimedDelivery
        {
            DeliveryId = DeliveryId.New(),
            AttemptCount = 1,
            LeaseExpiresAt = Now.AddMinutes(1),
            EventId = WebhookEventId.New(),
            EventType = "order.created",
            Payload = "{}",
            Headers = new Dictionary<string, string>(),
            EndpointId = endpointId,
            Url = url,
            Secrets =
            [
                EndpointSecret.Create(endpointId, Protector.Protect(WebhookSecret.Generate()), SignatureAlgorithm.HmacSha256, Now.AddDays(-1))
            ]
        };
    }
}