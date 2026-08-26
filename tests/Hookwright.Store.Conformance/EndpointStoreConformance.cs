using Hookwright.Core.Endpoints;

namespace Hookwright.Store.Conformance;

public abstract class EndpointStoreConformance : StoreConformance
{
    protected EndpointStoreConformance(IStoreHarness harness)
        : base(harness)
    {

    }

    [Fact]
    public async Task AddAsync_MakesTheEndpointFindable()
    {
        await using IStoreSession session = OpenSession();

        WebhookEndpoint endpoint = await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);

        (await session.Endpoints.FindAsync(endpoint.Id, TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .Url.ShouldBe(new Uri("https://example.com/"));
    }

    [Fact]
    public async Task AddAsync_CommitsWithoutAFurtherCall()
    {
        WebhookEndpoint endpoint;

        await using IStoreSession writer = OpenSession();
        endpoint = await RegisterEndpointAsync(writer, TestContext.Current.CancellationToken);

        await using IStoreSession reader = OpenSession();
        (await reader.Endpoints.FindAsync(endpoint.Id, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task ListForSubscriberAsync_ReturnsOnlyThatSubscribersEndpoints()
    {
        await using IStoreSession session = OpenSession();

        WebhookEndpoint first = await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);
        WebhookEndpoint second = await RegisterEndpointAsync(session, TestContext.Current.CancellationToken);

        IReadOnlyList<WebhookEndpoint> found = await session.Endpoints.ListForSubscriberAsync(
            first.SubscriberId, TestContext.Current.CancellationToken);

        found.ShouldHaveSingleItem().Id.ShouldBe(first.Id);
    }

    [Fact]
    public async Task FindAsync_GivenAnUnknownEndpoint_ReturnsNull()
    {
        await using IStoreSession session = OpenSession();

        (await session.Endpoints.FindAsync(WebhookEndpointId.New(), TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }
}