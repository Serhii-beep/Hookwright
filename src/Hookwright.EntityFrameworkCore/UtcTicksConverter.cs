using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hookwright.EntityFrameworkCore;

/// <summary>
/// Stores an instant as UTC ticks, for a provider that cannot query a native timestamp.
/// </summary>
internal sealed class UtcTicksConverter : ValueConverter<DateTimeOffset, long>
{
    public UtcTicksConverter()
        : base(value => value.UtcDateTime.Ticks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
    {

    }
}