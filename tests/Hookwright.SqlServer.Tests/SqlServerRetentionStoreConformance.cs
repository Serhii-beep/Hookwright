using Hookwright.Store.Conformance;

namespace Hookwright.SqlServer.Tests;

public sealed class SqlServerRetentionStoreConformance : RetentionStoreConformance
{
    public SqlServerRetentionStoreConformance()
        : base(new SqlServerStoreHarness())
    {

    }
}