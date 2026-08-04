using System.Reflection;

using Hookwright.Core.Entities;

namespace Hookwright.Core.Tests.Architecture;

/// <summary>
/// Structural rules that are easy to state, easy to violate accidentally,
/// and invisible in a code review of any single file.
/// </summary>
public sealed class ArchitectureTests
{
    private const string EntitiesNamespace = "Hookwright.Core.Entities";

    private static readonly Assembly CoreAssembly = typeof(Subscriber).Assembly;

    [Fact]
    public void Core_Always_ReferencesNothingOutsideTheBaseClassLibrary()
    {
        string[] offenders =
        [
            .. CoreAssembly
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => !IsBaseClassLibrary(name))
                .OfType<string>()
                .Order(StringComparer.Ordinal)
        ];

        offenders.ShouldBeEmpty($"Hookwright.Core must depend only on the BCL, but references: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Entities_Always_AreSealed()
    {
        string[] unsealed =
        [
            .. EntityClasses()
                .Where(type => !type.IsSealed)
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal)
        ];

        unsealed.ShouldBeEmpty($"Entities must be sealed: {string.Join(", ", unsealed)}");
    }

    [Fact]
    public void Entities_Always_HaveNoPubliclyWritableProperties()
    {
        string[] writable =
        [
            .. EntityClasses()
                .SelectMany(type => type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(property => property.SetMethod is { IsPublic: true })
                    .Select(property => $"{type.Name}.{property.Name}"))
                .Order(StringComparer.Ordinal)
        ];

        writable.ShouldBeEmpty($"Entities must not expose public setters: {string.Join(", ", writable)}");
    }

    [Fact]
    public void EntityClasses_Always_DiscoversSomething()
    {
        EntityClasses().ShouldNotBeEmpty();
    }

    private static IEnumerable<Type> EntityClasses()
    {
        return CoreAssembly
            .GetExportedTypes()
            .Where(type => type.IsClass && type.Namespace == EntitiesNamespace);
    }

    private static bool IsBaseClassLibrary(string? name)
    {
        return name is not null
            && (name.StartsWith("System.", StringComparison.Ordinal)
                || name is "System" or "netstandard" or "mscorlib");
    }
}