using Hookwright.Store.Conformance;

namespace Hookwright.PostgreSql.Tests;

public sealed class PostgresRetentionStoreConformance : RetentionStoreConformance
{
    public PostgresRetentionStoreConformance()
        : base(new PostgresStoreHarness())
    {

    }
}