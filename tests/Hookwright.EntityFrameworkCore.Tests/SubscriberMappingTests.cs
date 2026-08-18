using Hookwright.Core.Subscribers;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Tests;

public sealed class SubscriberMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static HookwrightDbContext CreateContext(SqliteConnection connection)
    {
        return new HookwrightDbContext(new DbContextOptionsBuilder<HookwrightDbContext>().UseSqlite(connection).Options);
    }

    [Fact]
    public async Task Subscriber_Always_RoundTripsThroughTheDatabase()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        Subscriber subscriber = Subscriber.Create("org_test", "org_test_name", Now);
        subscriber.Disable(Now.AddDays(1));

        await using HookwrightDbContext write = CreateContext(connection);
        await write.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        write.Add(subscriber);
        await write.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using HookwrightDbContext read = CreateContext(connection);
        Subscriber loaded = await read.Set<Subscriber>().SingleAsync(TestContext.Current.CancellationToken);

        loaded.Id.ShouldBe(subscriber.Id);
        loaded.ExternalId.ShouldBe("org_test");
        loaded.Name.ShouldBe("org_test_name");
        loaded.CreatedAt.ShouldBe(Now);
        loaded.DisabledAt.ShouldBe(Now.AddDays(1));
        loaded.IsEnabled.ShouldBeFalse();
    }
}