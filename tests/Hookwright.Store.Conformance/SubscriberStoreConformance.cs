using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;

namespace Hookwright.Store.Conformance;

public abstract class SubscriberStoreConformance : StoreConformance
{
    protected SubscriberStoreConformance(IStoreHarness harness)
        : base(harness)
    {

    }

    [Fact]
    public async Task AddAsync_MakesTheSubscriberFindableByEitherIdentifier()
    {
        await using IStoreSession session = OpenSession();
        Subscriber subscriber = Subscriber.Create("test_org", "test_name", Now);

        await session.Subscribers.AddAsync(subscriber, TestContext.Current.CancellationToken);

        (await session.Subscribers.FindAsync(subscriber.Id, TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .ExternalId.ShouldBe("test_org");

        (await session.Subscribers.FindByExternalIdAsync("test_org", TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .Id.ShouldBe(subscriber.Id);
    }

    [Fact]
    public async Task AddAsync_Always_CommitsWithoutAFurtherCall()
    {
        Subscriber subscriber = Subscriber.Create("test_org", null, Now);

        await using IStoreSession writer = OpenSession();
        await writer.Subscribers.AddAsync(subscriber, TestContext.Current.CancellationToken);

        await using IStoreSession reader = OpenSession();
        (await reader.Subscribers.FindAsync(subscriber.Id, TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task AddAsync_GivenAnInstantWithAnOffset_StoresTheSameInstant()
    {
        DateTimeOffset createdAt = Now.ToOffset(TimeSpan.FromHours(2));

        await using IStoreSession writer = OpenSession();
        await writer.Subscribers.AddAsync(
            Subscriber.Create("test_org", null, createdAt), TestContext.Current.CancellationToken);

        await using IStoreSession reader = OpenSession();
        Subscriber found = (await reader.Subscribers.FindByExternalIdAsync("test_org", TestContext.Current.CancellationToken))
            .ShouldNotBeNull();

        found.CreatedAt.ShouldBe(createdAt);
    }

    [Fact]
    public async Task AddAsync_GivenADuplicateExternalId_Conflicts()
    {
        await using IStoreSession session = OpenSession();
        await session.Subscribers.AddAsync(
            Subscriber.Create("test_org", null, Now), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<StoreConflictException>(() =>
            session.Subscribers.AddAsync(Subscriber.Create("test_org", null, Now), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindByExternalIdAsync_GivenDifferentCasing_DoesNotMatch()
    {
        await using IStoreSession session = OpenSession();
        await session.Subscribers.AddAsync(
            Subscriber.Create("test_org", null, Now), TestContext.Current.CancellationToken);

        (await session.Subscribers.FindByExternalIdAsync("Test_Org", TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_GivenAnUnknownSubscriber_ReturnsNull()
    {
        await using IStoreSession session = OpenSession();

        (await session.Subscribers.FindAsync(SubscriberId.New(), TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }
}