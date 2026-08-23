using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Hookwright.EntityFrameworkCore;

internal sealed class StringDictionaryComparer : ValueComparer<Dictionary<string, string>>
{
    public StringDictionaryComparer()
        : base(
            (left, right) => AreEqual(left, right),
            value => Hash(value),
            value => Copy(value))
    {

    }

    private static bool AreEqual(Dictionary<string, string>? left, Dictionary<string, string>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach ((string name, string value) in left)
        {
            if (!right.TryGetValue(name, out string? other) || !string.Equals(value, other, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int Hash(Dictionary<string, string> value)
    {
        int hash = 0;

        foreach ((string name, string item) in value)
        {
            hash ^= HashCode.Combine(
                name.GetHashCode(StringComparison.OrdinalIgnoreCase),
                item.GetHashCode(StringComparison.Ordinal));
        }

        return hash;
    }

    private static Dictionary<string, string> Copy(Dictionary<string, string> value)
    {
        return new Dictionary<string, string>(value, value.Comparer);
    }
}