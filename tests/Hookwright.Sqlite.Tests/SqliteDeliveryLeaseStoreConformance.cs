using Hookwright.Store.Conformance;

namespace Hookwright.Sqlite.Tests;

public sealed class SqliteDeliveryLeaseStoreConformance : DeliveryLeaseStoreConformance
{
    public SqliteDeliveryLeaseStoreConformance()
        : base(new SqliteStoreHarness())
    {

    }
}