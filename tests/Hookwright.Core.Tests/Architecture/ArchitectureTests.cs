using System.Reflection;

using Hookwright.Core.Subscribers;

namespace Hookwright.Core.Tests.Architecture;

/// <summary>
/// Structural rules that are easy to state, easy to violate accidentally,
/// and invisible in a code review of any single file.
/// </summary>
public sealed class ArchitectureTests
{
    private const string IdentifiersNamespace = "Hookwright.Core.Identifiers";

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
    public void DomainTypes_Always_AreSealed()
    {
        string[] unsealed =
        [
            .. DomainClasses()
                .Where(type => !type.IsSealed)
                .Select(type => type.Name)
                .Order(StringComparer.Ordinal)
        ];

        unsealed.ShouldBeEmpty($"Domain types must be sealed: {string.Join(", ", unsealed)}");
    }

    [Fact]
    public void DomainTypes_Always_HaveNoPubliclyWritableProperties()
    {
        string[] writable =
        [
            .. DomainClasses()
                .SelectMany(type => type
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(property => property.SetMethod is { IsPublic: true })
                    .Select(property => $"{type.Name}.{property.Name}"))
                .Order(StringComparer.Ordinal)
        ];

        writable.ShouldBeEmpty($"Domain types must not declare public setters: {string.Join(", ", writable)}");
    }

    [Fact]
    public void DomainClasses_Always_DiscoversSomething()
    {
        DomainClasses().ShouldNotBeEmpty();
    }

    private static IEnumerable<Type> DomainClasses()
    {
        return CoreAssembly
            .GetExportedTypes()
            .Where(type => type.IsClass && IsDomainNamespace(type.Namespace));
    }

    /// <summary>
    /// Everything in Core is domain except the shared identifier primitives. Framed as an
    /// exclusion so that a new aggregate namespace is covered automatically rather than
    /// quietly escaping these rules.
    /// </summary>
    private static bool IsDomainNamespace(string? candidate)
    {
        return candidate is not null
            && candidate.StartsWith("Hookwright.Core.", StringComparison.Ordinal)
            && candidate != IdentifiersNamespace;
    }

    private static bool IsBaseClassLibrary(string? name)
    {
        return name is not null
            && (name.StartsWith("System.", StringComparison.Ordinal)
                || name is "System" or "netstandard" or "mscorlib");
    }
}