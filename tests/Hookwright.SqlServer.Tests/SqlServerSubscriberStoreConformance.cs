using Hookwright.Store.Conformance;

namespace Hookwright.SqlServer.Tests;

public sealed class SqlServerSubscriberStoreConformance : SubscriberStoreConformance
{
    public SqlServerSubscriberStoreConformance()
        : base(new SqlServerStoreHarness())
    {

    }
}