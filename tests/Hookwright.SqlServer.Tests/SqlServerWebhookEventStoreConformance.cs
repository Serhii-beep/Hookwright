using Hookwright.Store.Conformance;

namespace Hookwright.SqlServer.Tests;

public sealed class SqlServerWebhookEventStoreConformance : WebhookEventStoreConformance
{
    public SqlServerWebhookEventStoreConformance()
        : base(new SqlServerStoreHarness())
    {

    }
}