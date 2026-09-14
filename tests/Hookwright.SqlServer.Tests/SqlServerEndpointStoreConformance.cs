using Hookwright.Store.Conformance;

namespace Hookwright.SqlServer.Tests;

public sealed class SqlServerEndpointStoreConformance : EndpointStoreConformance
{
    public SqlServerEndpointStoreConformance()
        : base(new SqlServerStoreHarness())
    {

    }
}