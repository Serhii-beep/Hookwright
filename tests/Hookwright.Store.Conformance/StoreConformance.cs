using Hookwright.Core.Endpoints;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.Signing;

namespace Hookwright.Store.Conformance;

public abstract class StoreConformance : IAsyncLifetime
{
    private readonly IStoreHarness _harness;

    protected static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    protected StoreConformance(IStoreHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);

        _harness = harness;
    }

    protected IStoreSession OpenSession()
    {
        return _harness.OpenSession();
    }

    protected static async Task<WebhookEndpoint> RegisterEndpointAsync(
        IStoreSession session,
        CancellationToken cancellationToken)
    {
        Subscriber subscriber = Subscriber.Create($"org_{Guid.CreateVersion7():N}", null, Now);
        await session.Subscribers.AddAsync(subscriber, cancellationToken);

        WebhookEndpoint endpoint = WebhookEndpoint.Create(
            subscriber.Id, new Uri("https://example.com/"), null, Now);

        await session.Endpoints.AddAsync(
            EndpointRegistration.Create(
                endpoint,
                [EndpointSecret.Create(endpoint.Id, "protected-key", SignatureAlgorithm.HmacSha256, Now)]),
            cancellationToken);

        return endpoint;
    }

    public async ValueTask InitializeAsync()
    {
        await _harness.InitialiseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.DisposeAsync();

        GC.SuppressFinalize(this);
    }
}