using Hookwright.Store.Conformance;

namespace Hookwright.Sqlite.Tests;

public sealed class SqliteSubscriberStoreConformance : SubscriberStoreConformance
{
    public SqliteSubscriberStoreConformance()
        : base(new SqliteStoreHarness())
    {

    }
}