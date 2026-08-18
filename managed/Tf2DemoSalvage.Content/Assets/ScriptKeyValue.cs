using System;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// One key's value out of a KeyValues script, without parsing the rest of it.
/// </summary>
/// <remarks>
/// **Deliberately not a KeyValues parser**, for the reason <c>ClassScript</c> already gave: the
/// scripts nest deeply, a real reader needs a tree API, and nothing here wants the tree. Finding
/// the first quoted value after a quoted key is a smaller and more testable problem.
///
/// Shared because two callers now need exactly it — a class script's <c>model</c> and a weapon
/// script's <c>WeaponType</c> — and a second copy of a text scan is a second place for the quoting
/// rules to drift.
///
/// **Only the first match is taken**, which is the engine's own answer: a key repeated later in a
/// block overrides in some readers and is ignored in others, and agreeing with Valve's parser is
/// what matters here.
/// </remarks>
internal static class ScriptKeyValue
{
    /// <summary>The value of the first occurrence of a key.</summary>
    /// <param name="script">The script's bytes, already decrypted if it was encrypted.</param>
    /// <param name="key">The key to look for, without quotes.</param>
    /// <returns>The value, or <c>null</c> when the key is absent or unterminated.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    public static string? First(ReadOnlySpan<byte> script, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        string text = Encoding.UTF8.GetString(script);
        string quoted = '"' + key + '"';

        int at = text.IndexOf(quoted, StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return null;
        }

        int open = text.IndexOf('"', at + quoted.Length);

        if (open < 0)
        {
            return null;
        }

        int close = text.IndexOf('"', open + 1);

        return close < 0 ? null : text[(open + 1)..close];
    }
}
