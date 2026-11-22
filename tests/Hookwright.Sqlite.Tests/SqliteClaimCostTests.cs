using Hookwright.Core.Deliveries;
using Hookwright.Core.Endpoints;
using Hookwright.Core.Events;
using Hookwright.Core.Storage;
using Hookwright.Core.Subscribers;
using Hookwright.EntityFrameworkCore;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using SQLitePCL;

namespace Hookwright.Sqlite.Tests;

public sealed class SqliteClaimCostTests
{
    private const int Shallow = 500;

    private const int Deep = 20_000;

    private const int Claims = 20;

    private const string Worker = "web-01:4242:a1b2c3";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task ClaimAsync_GivenABacklogFortyTimesDeeper_TakesNoMoreStepsToGetPastIt()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync(token);
        DbContextOptions<HookwrightDbContext> options =
            new DbContextOptionsBuilder<HookwrightDbContext>().UseSqlite(connection).Options;

        await using (HookwrightDbContext context = new(options))
        {
            await context.Database.EnsureCreatedAsync(token);
        }

        WebhookEndpoint backlogged = await RegisterAsync(options, token);
        WebhookEndpoint other = await RegisterAsync(options, token);
        await PublishAsync(options, backlogged, Shallow, Now.AddHours(-1), token);
        await PublishAsync(options, other, 4 * Claims, Now, token);

        ClaimRound passedOver = new(null, 8, new Dictionary<WebhookEndpointId, int> { [backlogged.Id] = 0 });
        ClaimRound served = ClaimRound.FromTheStart(8);

        long shallowPassedOver = await StepsPerClaimAsync(connection, options, 1, passedOver, token);
        long shallowServed = await StepsPerClaimAsync(connection, options, 9, served, token);
        await PublishAsync(options, backlogged, Deep - Shallow, Now.AddHours(-1), token);
        long deepPassedOver = await StepsPerClaimAsync(connection, options, 1, passedOver, token);
        long deepServed = await StepsPerClaimAsync(connection, options, 9, served, token);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Past a backlog of {Shallow} a claim took {shallowPassedOver} steps, and past one of {Deep}, {deepPassedOver}. " +
            $"Taking eight from it took {shallowServed} steps and {deepServed}.");

        deepPassedOver.ShouldBeLessThanOrEqualTo(shallowPassedOver + shallowPassedOver / 10);
        deepServed.ShouldBeLessThanOrEqualTo(shallowServed + shallowServed / 10);
    }

    private static async Task<long> StepsPerClaimAsync(
        SqliteConnection connection,
        DbContextOptions<HookwrightDbContext> options,
        int batchSize,
        ClaimRound round,
        CancellationToken token)
    {
        long steps = 0;
        raw.sqlite3_progress_handler(
            connection.Handle,
            1,
            _ =>
            {
                steps++;

                return 0;
            },
            null);

        try
        {
            await using HookwrightDbContext context = new(options);
            SqliteDeliveryLeaseStore store = new(context);

            for (int i = 0; i < Claims; i++)
            {
                (await store.ClaimAsync(Worker, batchSize, Lease, Now, round, token)).Deliveries.Count.ShouldBe(batchSize);
            }
        }
        finally
        {
            raw.sqlite3_progress_handler(connection.Handle, 0, null, null);
        }

        return steps / Claims;
    }

    private static async Task<WebhookEndpoint> RegisterAsync(DbContextOptions<HookwrightDbContext> options, CancellationToken token)
    {
        Subscriber subscriber = Subscriber.Create($"org_{Guid.CreateVersion7():N}", null, Now);
        WebhookEndpoint endpoint = WebhookEndpoint.Create(subscriber.Id, new Uri("https://example.com/"), null, Now);

        await using HookwrightDbContext context = new(options);
        context.AddRange(subscriber, endpoint);
        await context.SaveChangesAsync(token);

        return endpoint;
    }

    private static async Task PublishAsync(
        DbContextOptions<HookwrightDbContext> options,
        WebhookEndpoint endpoint,
        int count,
        DateTimeOffset at,
        CancellationToken token)
    {
        for (int first = 0; first < count; first += 1_000)
        {
            await using HookwrightDbContext context = new(options);

            for (int i = first; i < Math.Min(first + 1_000, count); i++)
            {
                WebhookEvent published = WebhookEvent.Create(endpoint.SubscriberId, "order.created", "{}", at);
                context.AddRange(published, Delivery.Create(published.Id, endpoint.Id, null, at));
            }

            await context.SaveChangesAsync(token);
        }
    }
}