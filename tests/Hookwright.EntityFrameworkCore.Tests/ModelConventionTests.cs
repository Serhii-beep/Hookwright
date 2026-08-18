using Microsoft.EntityFrameworkCore;

namespace Hookwright.EntityFrameworkCore.Tests;

public sealed class ModelConventionTests
{
    private static HookwrightDbContext CreateContext()
    {
        return new HookwrightDbContext(
            new DbContextOptionsBuilder<HookwrightDbContext>()
                .UseSqlite("Data Source=:memory:")
                .Options);
    }

    [Fact]
    public void EveryColumn_Always_IsNamedInSnakeCase()
    {
        using HookwrightDbContext context = CreateContext();

        string[] offenders =
        [
            .. context.Model.GetEntityTypes()
                .SelectMany(entity => entity.GetProperties()
                    .Select(property => property.GetColumnName())
                    .Where(name => !IsSnakeCase(name))
                    .Select(name => $"{entity.ClrType.Name} -> {name}"))
        ];

        offenders.ShouldBeEmpty(string.Join(", ", offenders));
    }

    [Fact]
    public void EveryTable_Always_CarriesThePrefix()
    {
        using HookwrightDbContext context = CreateContext();

        string[] offenders =
        [
            .. context.Model.GetEntityTypes()
                .Select(entity => entity.GetTableName())
                .OfType<string>()
                .Where(name => !name.StartsWith(HookwrightTables.Prefix, StringComparison.Ordinal))
        ];

        offenders.ShouldBeEmpty(string.Join(", ", offenders));
    }

    private static bool IsSnakeCase(string? name)
    {
        return name is not null
            && name.Length > 0
            && name.All(ch =>
                char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch) || ch == '_');
    }
}