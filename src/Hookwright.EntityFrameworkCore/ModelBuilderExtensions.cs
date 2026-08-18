using System.Reflection;
using System.Text;

using Hookwright.Core.Subscribers;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Hookwright.EntityFrameworkCore;

internal static class ModelBuilderExtensions
{
    private static readonly Assembly DomainAssembly = typeof(Subscriber).Assembly;

    internal static void UseSnakeCaseColumnNames(this ModelBuilder modelBuilder)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.ClrType.Assembly != DomainAssembly)
            {
                continue;
            }

            foreach (IMutableProperty property in entityType.GetProperties())
            {
                if (property.FindAnnotation(RelationalAnnotationNames.ColumnName) is null)
                {
                    property.SetColumnName(ToSnakeCase(property.Name));
                }
            }
        }
    }

    private static string ToSnakeCase(string name)
    {
        ReadOnlySpan<char> source = name.AsSpan().TrimStart('_');
        StringBuilder sb = new(source.Length + 8);

        for (int i = 0; i < source.Length; i++)
        {
            char character = source[i];

            if (char.IsUpper(character))
            {
                if (i > 0)
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(character));
            }
            else
            {
                sb.Append(character);
            }
        }

        return sb.ToString();
    }
}