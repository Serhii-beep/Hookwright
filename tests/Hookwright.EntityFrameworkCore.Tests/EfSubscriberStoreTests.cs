using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.EntityFrameworkCore.Stores;

using Microsoft.Data.Sqlite;

namespace Hookwright.EntityFrameworkCore.Tests;

public sealed class EfSubscriberStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_GivenASubscriber_MakesItFindableByEitherIdentifier()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        EfSubscriberStore store = new(context);
        Subscriber subscriber = Subscriber.Create("test_org", "test_name", Now);

        await store.AddAsync(subscriber, TestContext.Current.CancellationToken);

        (await store.FindAsync(subscriber.Id, TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .ExternalId.ShouldBe("test_org");

        (await store.FindByExternalIdAsync("test_org", TestContext.Current.CancellationToken))
            .ShouldNotBeNull()
            .Id.ShouldBe(subscriber.Id);
    }

    [Fact]
    public async Task AddAsync_GivenADuplicateExternalId_Conflicts()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        EfSubscriberStore store = new(context);
        await store.AddAsync(Subscriber.Create("test_org", "test_name", Now), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<StoreConflictException>(() =>
            store.AddAsync(Subscriber.Create("test_org", "another_name", Now), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FindByExternalIdAsync_GivenDifferentCasing_DoesNotMatch()
    {
        await using SqliteConnection connection = TestDatabase.Open();
        await using HookwrightDbContext context = await TestDatabase.CreateSchemaAsync(connection, TestContext.Current.CancellationToken);

        EfSubscriberStore store = new(context);
        await store.AddAsync(Subscriber.Create("test_org", null, Now), TestContext.Current.CancellationToken);

        (await store.FindByExternalIdAsync("Test_Org", TestContext.Current.CancellationToken))
            .ShouldBeNull();
    }
}