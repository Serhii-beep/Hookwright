using Hookwright.Store.Conformance;

namespace Hookwright.Sqlite.Tests;

public sealed class SqliteEndpointStoreConformance : EndpointStoreConformance
{
    public SqliteEndpointStoreConformance()
        : base(new SqliteStoreHarness())
    {

    }
}