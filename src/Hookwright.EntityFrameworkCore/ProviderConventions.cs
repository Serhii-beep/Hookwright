using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore;

internal static class ProviderConventions
{
    private const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";

    private const string Npgsql = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private const string SqlServer = "Microsoft.EntityFrameworkCore.SqlServer";

    internal static void Apply(ModelConfigurationBuilder configurationBuilder, string? providerName)
    {
        switch (providerName)
        {
            case Sqlite:
                // SQLite cannot compare, order, or aggregate a DateTimeOffset
                configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
                configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcTicksConverter>();
                break;
            case Npgsql:
                // PostgreSQL writes timestamptz only from a DateTimeOffset whose offset is zero
                configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcOffsetConverter>();
                configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcOffsetConverter>();
                break;
            case SqlServer:
                // SQL Server's default collation compares text case-insensitively. Every Hookwright
                // contract compares it ordinally, and so do other providers.
                configurationBuilder.Properties<string>().UseCollation("Latin1_General_100_BIN2");
                break;
        }
    }
}