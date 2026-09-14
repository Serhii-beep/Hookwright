using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hookwright.EntityFrameworkCore;

internal sealed class UtcOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcOffsetConverter()
        : base(value => value.ToUniversalTime(), value => value)
    {

    }
}