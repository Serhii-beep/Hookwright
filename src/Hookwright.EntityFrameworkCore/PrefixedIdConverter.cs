using Hookwright.Core.Identifiers;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hookwright.EntityFrameworkCore;

/// <summary>
/// Stores a prefixed identifier as the <see cref="Guid"/> it wraps.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
public sealed class PrefixedIdConverter<TId> : ValueConverter<TId, Guid>
    where TId : struct, IPrefixedId<TId>
{
    /// <summary>
    /// Creates the converter.
    /// </summary>
    public PrefixedIdConverter()
        : base(id => id.Value, value => Rehydrate(value))
    {

    }

    private static TId Rehydrate(Guid value)
    {
        return TId.FromGuid(value);
    }
}