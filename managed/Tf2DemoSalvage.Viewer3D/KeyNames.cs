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

    /// <summary>What a config would call this key.</summary>
    /// <param name="key">The key the toolkit reported.</param>
    /// <returns>The Source name, or an empty string when there is no sensible one.</returns>
    /// <remarks>
    /// **The direction this file's summary always claimed and did not have.** It is needed now that
    /// keys are fed to <c>ConfigConsole</c>, which deals only in names: a binding table can be
    /// consulted key-first, but a console has to be *told* that `w` went down.
    ///
    /// **The sided modifiers collapse onto one name, and that is the whole reason this is not
    /// <c>Enum.GetName</c>.** A held Control arrives as `ControlKey`, `LControlKey` or
    /// `RControlKey` depending on how it was read, so all three have to answer to the `CTRL` a
    /// config binds — otherwise the camera never descends and nothing reports why. The same trap
    /// exists for Shift and Alt. This replaced the equivalent special-casing that used to live in
    /// `FreeFlight.IsDown`, so there is one place that knows it rather than two.
    ///
    /// **Letters are lower case and digits drop their `D`**, because that is how a config spells
    /// them: `bind "w" "+forward"`, not `bind "W"`. Lookups are case-insensitive either way, but the
    /// name is also what a settings file ends up containing and what a user reads back.
    /// </remarks>
    public static string NameOf(Keys key) => (key & Keys.KeyCode) switch
    {
        Keys.Space => "SPACE",
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey => "CTRL",
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey => "SHIFT",
        Keys.Menu or Keys.LMenu or Keys.RMenu => "ALT",
        Keys.Enter => "ENTER",
        Keys.Escape => "ESCAPE",
        Keys.OemQuotes => "'",
        Keys.OemQuestion => "/",

        >= Keys.A and <= Keys.Z => ((char)('a' + (key & Keys.KeyCode) - Keys.A)).ToString(),
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key & Keys.KeyCode) - Keys.D0)).ToString(),

        Keys.None => string.Empty,
        _ => Enum.GetName(key & Keys.KeyCode) ?? string.Empty,
    };
}
