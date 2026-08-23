using Hookwright.Core.Events;
using Hookwright.Core.Subscribers;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Tests;

public sealed class EventMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Event_Always_RoundTripsIncludingItsHeaders()
    {
        await using SqliteConnection connection = TestDatabase.Open();

        Subscriber subscriber = Subscriber.Create("test_org", null, Now);
        WebhookEvent published = WebhookEvent.Create(
            subscriber.Id,
            "order.created",
            """{"id":1,"total":42.5}""",
            Now,
            partitionKey: "order-1",
            idempotencyKey: "idem-1",
            headers: new Dictionary<string, string> { ["Content-Type"] = "application/json" });

        await using HookwrightDbContext write = TestDatabase.CreateContext(connection);
        await write.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        write.Add(subscriber);
        write.Add(published);
        await write.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using HookwrightDbContext read = TestDatabase.CreateContext(connection);
        WebhookEvent loaded = await read.Set<WebhookEvent>().SingleAsync(TestContext.Current.CancellationToken);

        loaded.Id.ShouldBe(published.Id);
        loaded.Type.ShouldBe("order.created");
        loaded.Payload.ShouldBe("""{"id":1,"total":42.5}""");
        loaded.PartitionKey.ShouldBe("order-1");
        loaded.IdempotencyKey.ShouldBe("idem-1");
        loaded.CreatedAt.ShouldBe(Now);

        loaded.Headers["content-type"].ShouldBe("application/json");
    }
}