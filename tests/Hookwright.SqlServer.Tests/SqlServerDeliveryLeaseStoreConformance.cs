using Hookwright.Store.Conformance;

namespace Hookwright.SqlServer.Tests;

public sealed class SqlServerDeliveryLeaseStoreConformance : DeliveryLeaseStoreConformance
{
    public SqlServerDeliveryLeaseStoreConformance()
        : base(new SqlServerStoreHarness())
    {

    }
}