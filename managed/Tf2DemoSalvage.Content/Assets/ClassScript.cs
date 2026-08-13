using System;
using System.Text;

namespace Tf2DemoSalvage.Content.Assets;

/// <summary>
/// The one value this project needs out of a player class script.
/// </summary>
/// <remarks>
/// A class script is KeyValues, the same brace-and-quoted-pairs text as a VMT or a BSP entity
/// lump. <c>tf_classdata.cpp</c> reads dozens of keys from it — speed, health, sounds, weapon
/// slots — and this reads one, because that is all a viewer needs to draw somebody.
///
/// **Deliberately not a general KeyValues parser.** One already exists for materials, and the
/// class scripts nest deeply enough that a shared reader would have to grow a tree API to serve
/// both. Finding a single top-level key is a smaller and more testable problem than parsing a
/// format nothing else here needs in full.
/// </remarks>
internal static class ClassScript
{
    /// <summary>The <c>model</c> key's value.</summary>
    /// <param name="script">The script's bytes.</param>
    /// <returns>The model path, or <c>null</c> when the script names none.</returns>
    /// <remarks>
    /// **Backslashes are normalised to forward.** Valve's own data mixes them —
    /// <c>models\player\scout.mdl</c> appears in scripts while the VPK indexes forward slashes —
    /// and a lookup with the wrong separator finds nothing and reports nothing.
    ///
    /// Only the first match is taken. A key repeated later in a KeyValues block is an override in
    /// some readers and ignored in others; the engine's own parser answers with the first, which
    /// is what matters for agreeing with it.
    /// </remarks>
    public static string? Model(ReadOnlySpan<byte> script)
    {
        string text = Encoding.UTF8.GetString(script);

        int at = text.IndexOf("\"model\"", StringComparison.OrdinalIgnoreCase);

        if (at < 0)
        {
            return null;
        }

        // The value is the next quoted run after the key.
        int open = text.IndexOf('"', at + "\"model\"".Length);

        if (open < 0)
        {
            return null;
        }

        int close = text.IndexOf('"', open + 1);

        return close < 0
            ? null
            : text[(open + 1)..close].Replace('\\', '/');
    }
}
