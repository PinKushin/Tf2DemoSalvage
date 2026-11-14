using System.Globalization;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The state keys a fixed networked array's elements arrive under — <c>prefix.000</c>, <c>prefix.001</c>, …</summary>
/// <remarks>
/// **Built once, not formatted per read** (B613). The timeline reads these arrays for every player, or every resource
/// slot, on every packet, and formatting the element number into a fresh key each time made strings one of the f12
/// decode's largest allocations. An element past <paramref name="count"/> is still answered, by formatting it then, so the
/// key a caller gets never depends on the cache's size.
/// </remarks>
/// <param name="prefix">The array's name, without the trailing dot.</param>
/// <param name="count">How many elements to build up front.</param>
internal sealed class ElementKeys(string prefix, int count)
{
    private readonly string[] _keys = Build(prefix, count);

    /// <summary>The key element <paramref name="element"/> arrives under.</summary>
    /// <param name="element">The element number.</param>
    public string this[int element] => (uint)element < (uint)_keys.Length ? _keys[element] : Format(prefix, element);

    private static string[] Build(string prefix, int count)
    {
        string[] keys = new string[count];

        for (int element = 0; element < count; element++)
        {
            keys[element] = Format(prefix, element);
        }

        return keys;
    }

    private static string Format(string prefix, int element) =>
        string.Create(CultureInfo.InvariantCulture, $"{prefix}.{element:D3}");
}
