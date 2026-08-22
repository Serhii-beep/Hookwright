using Hookwright.Core.Endpoints;
using Hookwright.Core.Subscribers;
using Hookwright.Signing;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Tests;

public sealed class EndpointMappingTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Endpoint_Always_RoundTripsThroughTheDatabase()
    {
        await using SqliteConnection connection = TestDatabase.Open();

        Subscriber subscriber = Subscriber.Create("test_org", null, Now);
        WebhookEndpoint endpoint = WebhookEndpoint.Create(
            subscriber.Id, new Uri("https://example.com/"), "primary", Now);
        endpoint.SetEventTypeFilter(["order.created", "invoice.*"], Now);
        endpoint.SetDeliverySettings(PartitionMode.ByKey, 25, Now);
        endpoint.MarkVerified(Now.AddMinutes(5));

        await using HookwrightDbContext write = TestDatabase.CreateContext(connection);
        await write.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        write.Add(subscriber);
        write.Add(endpoint);
        await write.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using HookwrightDbContext read = TestDatabase.CreateContext(connection);
        WebhookEndpoint loaded = await read.Set<WebhookEndpoint>()
            .SingleAsync(TestContext.Current.CancellationToken);

        loaded.Id.ShouldBe(endpoint.Id);
        loaded.SubscriberId.ShouldBe(subscriber.Id);
        loaded.Url.ShouldBe(new Uri("https://example.com/"));
        loaded.Description.ShouldBe("primary");
        loaded.EventTypeFilter.ShouldBe(["order.created", "invoice.*"]);
        loaded.PartitionMode.ShouldBe(PartitionMode.ByKey);
        loaded.RateLimitPerSecond.ShouldBe(25);
        loaded.Health.ShouldBe(EndpointHealth.Healthy);
        loaded.IsVerified.ShouldBeTrue();
    }

    [Fact]
    public async Task Secrets_Always_RoundTripIncludingARotationOverlap()
    {
        await using SqliteConnection connection = TestDatabase.Open();

        Subscriber subscriber = Subscriber.Create("test_rog", null, Now);
        WebhookEndpoint endpoint = WebhookEndpoint.Create(
            subscriber.Id, new Uri("https://example.com/"), null, Now);

        EndpointSecret retiring = EndpointSecret.Create(
            endpoint.Id, "protected-old", SignatureAlgorithm.HmacSha256, Now);
        retiring.Retire(Now.AddHours(24));

        EndpointSecret current = EndpointSecret.Create(
            endpoint.Id, "protected-new", SignatureAlgorithm.HmacSha256, Now.AddHours(1));

        await using HookwrightDbContext write = TestDatabase.CreateContext(connection);
        await write.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        write.Add(subscriber);
        write.Add(endpoint);
        write.AddRange(retiring, current);
        await write.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using HookwrightDbContext read = TestDatabase.CreateContext(connection);
        List<EndpointSecret> loaded = await read.Set<EndpointSecret>()
            .ToListAsync(TestContext.Current.CancellationToken);

        EndpointSecret retired = loaded.Single(secret => secret.ProtectedKey == "protected-old");
        EndpointSecret live = loaded.Single(secret => secret.ProtectedKey == "protected-new");

        loaded.Count.ShouldBe(2);
        retired.ValidUntil.ShouldBe(Now.AddHours(24));
        retired.IsRetired.ShouldBeTrue();
        live.Algorithm.ShouldBe(SignatureAlgorithm.HmacSha256);
        live.IsRetired.ShouldBeFalse();

        loaded.Count(secret => secret.IsActiveAt(Now.AddHours(2))).ShouldBe(2);
    }
}