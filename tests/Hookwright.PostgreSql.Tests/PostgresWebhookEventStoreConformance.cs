using Hookwright.Store.Conformance;

namespace Hookwright.PostgreSql.Tests;

public sealed class PostgresWebhookEventStoreConformance : WebhookEventStoreConformance
{
    public PostgresWebhookEventStoreConformance()
        : base(new PostgresStoreHarness())
    {

    }
}