namespace Tf2DemoSalvage.Presentation;

/// <summary>FNV-1a over a sequence of strings, stable across processes — unlike `string.GetHashCode`, which is randomised.</summary>
public static class SequenceHash
{
    /// <summary>The hash of no strings.</summary>
    public const ulong Empty = 14695981039346656037UL;

    private const ulong Prime = 1099511628211UL;

    /// <summary>The hash with one more string on the end.</summary>
    /// <param name="hash">The hash so far.</param>
    /// <param name="next">The string.</param>
    /// <returns>The new hash; a separator keeps "ab","c" apart from "a","bc".</returns>
    public static ulong Add(ulong hash, string next)
    {
        foreach (char c in next ?? string.Empty)
        {
            hash = (hash ^ c) * Prime;
        }

        return (hash ^ 0xFFFF) * Prime;
    }
}
