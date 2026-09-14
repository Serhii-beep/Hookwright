using Hookwright.Store.Conformance;

namespace Hookwright.Sqlite.Tests;

public sealed class SqliteRetentionStoreConformance : RetentionStoreConformance
{
    public SqliteRetentionStoreConformance()
        : base(new SqliteStoreHarness())
    {

    }
}