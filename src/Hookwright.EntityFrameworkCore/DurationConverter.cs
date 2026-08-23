using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hookwright.EntityFrameworkCore;

internal sealed class DurationConverter : ValueConverter<TimeSpan, long>
{
    public DurationConverter()
        : base(value => (long)value.TotalMilliseconds, milliseconds => TimeSpan.FromMilliseconds(milliseconds))
    {

    }
}