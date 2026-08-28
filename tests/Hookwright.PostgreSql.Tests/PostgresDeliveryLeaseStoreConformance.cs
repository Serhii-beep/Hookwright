using Hookwright.Store.Conformance;

namespace Hookwright.PostgreSql.Tests;

public sealed class PostgresDeliveryLeaseStoreConformance : DeliveryLeaseStoreConformance
{
    public PostgresDeliveryLeaseStoreConformance()
        : base(new PostgresStoreHarness())
    {

    }
}