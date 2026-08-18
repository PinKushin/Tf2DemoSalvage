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
        // The scan itself lives in ScriptKeyValue, because the weapon scripts need the same thing
        // for their WeaponType and one copy of the quoting rules is enough.
        return ScriptKeyValue.First(script, "model")?.Replace('\\', '/');
    }
}
