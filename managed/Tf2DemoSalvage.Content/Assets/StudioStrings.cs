using System;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>Reads the null-terminated strings a studio model stores inline.</summary>
/// <remarks>
/// **Every index inside a studio structure is relative to that structure**, not to the file, which
/// is the convention that bites hardest because a file-relative read still lands on plausible
/// bytes and produces a plausible string. Callers add the structure's own offset before calling.
/// </remarks>
internal static class StudioStrings
{
    /// <summary>Longest name to read, as a guard against a missing terminator.</summary>
    private const int LongestName = 256;

    /// <summary>Reads a null-terminated name.</summary>
    /// <param name="file">The model's bytes.</param>
    /// <param name="at">Where the name starts, already made absolute.</param>
    /// <returns>The name, or empty when the offset is not inside the file.</returns>
    /// <remarks>
    /// **UTF-8 rather than ASCII.** Community models carry non-English names, and ASCII turns one
    /// into a different plausible name rather than failing.
    /// </remarks>
    public static string At(ReadOnlySpan<byte> file, int at)
    {
        if (at <= 0 || at >= file.Length)
        {
            return string.Empty;
        }

        ReadOnlySpan<byte> from = file[at..];
        int end = from.IndexOf((byte)0);

        if (end < 0)
        {
            end = Math.Min(from.Length, LongestName);
        }

        return Encoding.UTF8.GetString(from[..Math.Min(end, LongestName)]);
    }
}
