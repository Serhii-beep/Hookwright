using System.Text.Json;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hookwright.EntityFrameworkCore;

internal sealed class HeaderDictionaryConverter : ValueConverter<Dictionary<string, string>, string>
{
    public HeaderDictionaryConverter()
        : base(value => Write(value), json => Read(json))
    {

    }

    private static string Write(Dictionary<string, string> value)
    {
        return JsonSerializer.Serialize(value);
    }

    private static Dictionary<string, string> Read(string json)
    {
        Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        return parsed is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
    }
}