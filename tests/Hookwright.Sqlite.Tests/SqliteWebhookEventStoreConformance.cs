using Hookwright.Store.Conformance;

namespace Hookwright.Sqlite.Tests;

public sealed class SqliteWebhookEventStoreConformance : WebhookEventStoreConformance
{
    public SqliteWebhookEventStoreConformance()
        : base(new SqliteStoreHarness())
    {

    }
}