using Hookwright.Store.Conformance;

namespace Hookwright.PostgreSql.Tests;

public sealed class PostgresSubscriberStoreConformance : SubscriberStoreConformance
{
    public PostgresSubscriberStoreConformance()
        : base(new PostgresStoreHarness())
    {

    }
}