using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore;

internal static class ProviderConventions
{
    private const string Sqlite = "Microsoft.EntityFrameworkCore.Sqlite";

    internal static void Apply(ModelConfigurationBuilder configurationBuilder, string? providerName)
    {
        if (providerName != Sqlite)
        {
            return;
        }

        // SQLite cannot compare, order, or aggregate a DateTimeOffset column.
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcTicksConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveConversion<UtcTicksConverter>();
    }
}