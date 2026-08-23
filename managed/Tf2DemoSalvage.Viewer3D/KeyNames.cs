using System;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Turns a bound key's NAME into the key this toolkit means by it, and back.
/// </summary>
/// <remarks>
/// **The other half of the binding seam (D68).** `KeyBindings` lives in the presentation layer and
/// cannot reference <c>System.Windows.Forms.Keys</c> — that is the boundary D54 exists to enforce —
/// so a binding is stored as a name and this is what resolves it.
///
/// **Names rather than integers**, because the bindings end up in a settings file a person edits by
/// hand, exactly as TF2's own <c>config.cfg</c> does. `bind "SPACE" "+jump"` is readable and
/// survives a version change; a numeric virtual-key code is neither.
///
/// **Only the names the viewer actually binds are mapped**, not all 200-odd members of
/// <see cref="Keys"/>. An exhaustive table would be mostly dead and would still miss whatever
/// somebody typed; the fallback parses anything else by <see cref="Keys"/>'s own names, so a user
/// binding "F7" or "NumPad3" works without this file knowing about it.
/// </remarks>
internal static class KeyNames
{
    /// <summary>The key a bound name refers to, or <c>Keys.None</c> when it names nothing.</summary>
    /// <param name="name">The name as a settings file spells it.</param>
    /// <returns>The key, or <c>Keys.None</c>.</returns>
    /// <remarks>
    /// **`Keys.None` rather than an exception**, because the name comes from a file a person typed
    /// into. A misspelt binding should cost that one control and say so in the log, not stop the
    /// viewer from starting.
    /// </remarks>
    public static Keys Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Keys.None;
        }

        // The handful with names of their own, either because the toolkit's spelling is unfriendly
        // (ControlKey, ShiftKey) or because they are mouse buttons, which are not Keys at all.
        return name.Trim().ToUpperInvariant() switch
        {
            "SPACE" => Keys.Space,
            "CONTROL" or "CTRL" => Keys.ControlKey,
            "SHIFT" => Keys.ShiftKey,
            "ALT" => Keys.Menu,
            "ENTER" or "RETURN" => Keys.Enter,
            "ESC" or "ESCAPE" => Keys.Escape,

            // **TF2's own vertical-movement keys**, which `config_default.cfg` binds as
            // `bind "'" "+moveup"` and `bind "/" "+movedown"`. WinForms names them after the OEM
            // scan codes rather than after the character printed on them, so neither resolves by
            // `Enum.TryParse` and both would otherwise silently become `Keys.None` — a binding that
            // reads as correct in a settings file and does nothing.
            "'" or "APOSTROPHE" or "QUOTE" => Keys.OemQuotes,
            "/" or "SLASH" => Keys.OemQuestion,

            // Mouse buttons are bound like keys by every game and are not members of Keys. They are
            // resolved by the mouse handlers rather than here, so this reports "not a key" for them
            // instead of guessing.
            "MOUSELEFT" or "MOUSERIGHT" or "MOUSEMIDDLE" => Keys.None,

            _ => Enum.TryParse(name.Trim(), ignoreCase: true, out Keys parsed) ? parsed : Keys.None,
        };
    }
}
