using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.Signing;

namespace Hookwright.Core.Tests.Storage;

public sealed class EndpointsRegistrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static WebhookEndpoint CreateEndpoint()
    {
        return WebhookEndpoint.Create(SubscriberId.New(), new Uri("https://example.com"), null, Now);
    }

    private static EndpointSecret CreateSecret(WebhookEndpointId endpointId)
    {
        return EndpointSecret.Create(endpointId, "protected-key", SignatureAlgorithm.HmacSha256, Now);
    }

    [Fact]
    public void Create_GivenAnEndpointAndItsKey_KeepsBoth()
    {
        WebhookEndpoint endpoint = CreateEndpoint();
        EndpointSecret secret = CreateSecret(endpoint.Id);

        EndpointRegistration registration = EndpointRegistration.Create(endpoint, [secret]);

        registration.Endpoint.ShouldBeSameAs(endpoint);
        registration.Secrets.ShouldHaveSingleItem().ShouldBeSameAs(secret);
    }

    [Fact]
    public void Create_GivenAnOverlappingPair_KeepsBoth()
    {
        WebhookEndpoint endpoint = CreateEndpoint();

        EndpointRegistration registration = EndpointRegistration.Create(
            endpoint,
            [CreateSecret(endpoint.Id), CreateSecret(endpoint.Id)]);

        registration.Secrets.Count.ShouldBe(2);
    }

    [Fact]
    public void Create_GivenNoKeys_Throws()
    {
        Should.Throw<ArgumentException>(() => EndpointRegistration.Create(CreateEndpoint(), []));
    }

    [Fact]
    public void Create_GivenAKeyForAnotherEndpoint_Throws()
    {
        EndpointSecret stray = CreateSecret(WebhookEndpointId.New());

        Should.Throw<ArgumentException>(() => EndpointRegistration.Create(CreateEndpoint(), [stray]));
    }
}