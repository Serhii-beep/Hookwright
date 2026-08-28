using Hookwright.Store.Conformance;

namespace Hookwright.PostgreSql.Tests;

public sealed class PostgresEndpointStoreConformance : EndpointStoreConformance
{
    public PostgresEndpointStoreConformance()
        : base(new PostgresStoreHarness())
    {

    }
}