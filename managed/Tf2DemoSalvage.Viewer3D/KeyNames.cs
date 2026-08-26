using System;
using System.Linq;
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

        // **`CTRL+o` and friends, which are a deliberate SUPERSET of Source's vocabulary** (B214).
        // Source's `bind` takes one key and has no syntax for a modifier, so no real config can
        // contain such a name and nothing pasted is misparsed by this branch — it only adds names
        // that were previously unspellable.
        //
        // **The viewer needs them because TF2 leaves almost nothing free.** `config_default.cfg`
        // binds 64 keys, every letter except `o`, so a default on any other single key is taken away
        // the moment a real config loads. Modifier combinations are unreachable from a config and
        // are therefore the only safe home for this viewer's own actions.
        //
        // Split from the RIGHT, so the key itself may be `+` if it ever needs to be.
        int plus = name.LastIndexOf('+');

        if (plus > 0 && plus < name.Length - 1)
        {
            Keys modifiers = Keys.None;
            bool known = true;

            foreach (string part in name[..plus].Split('+', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (part.Trim().ToUpperInvariant())
                {
                    case "CTRL" or "CONTROL": modifiers |= Keys.Control; break;
                    case "SHIFT": modifiers |= Keys.Shift; break;
                    case "ALT": modifiers |= Keys.Alt; break;
                    default: known = false; break;
                }
            }

            if (known)
            {
                Keys bare = Resolve(name[(plus + 1)..]);

                // A combination naming an unresolvable key is `None`, not a bare modifier — which
                // would otherwise be a shortcut that fires on Ctrl alone.
                return bare == Keys.None ? Keys.None : bare | modifiers;
            }
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

            // **Source's spellings for the navigation cluster, which WinForms names differently.**
            // A config writes `bind "UPARROW" "+forward"` and `bind "PGDN" "invnext"`; WinForms calls
            // those `Up` and `PageDown`, so `Enum.TryParse` missed every one and they resolved to
            // `Keys.None` — bindings that read as correct in a file and did nothing.
            //
            // **This is also where the vocabulary stops being WinForms-shaped**, which matters for
            // more than correctness: the names in `KeyBindings` and `WidgetKeys` are the portable
            // half of the controls, so they should be Source's everywhere and translated only here.
            "UPARROW" => Keys.Up,
            "DOWNARROW" => Keys.Down,
            "LEFTARROW" => Keys.Left,
            "RIGHTARROW" => Keys.Right,
            "PGUP" => Keys.PageUp,
            "PGDN" => Keys.PageDown,
            "INS" => Keys.Insert,
            "DEL" => Keys.Delete,
            "BACKSPACE" => Keys.Back,
            "CAPSLOCK" => Keys.CapsLock,
            "SEMICOLON" => Keys.OemSemicolon,
            "[" or "LBRACKET" => Keys.OemOpenBrackets,
            "]" or "RBRACKET" => Keys.OemCloseBrackets,

            // **Mouse buttons are bound like keys by every Source game and are not members of
            // `Keys`.** They are resolved by the mouse handlers, through
            // <see cref="NameOf(System.Windows.Forms.MouseButtons)"/>, so this reports "not a key" rather than guessing.
            //
            // **These were `MOUSELEFT`/`MOUSERIGHT`/`MOUSEMIDDLE` until 2026-08-23**, which is .NET's
            // vocabulary and matches nothing a player ever typed. The defaults moved to Source's
            // spelling when D69 made configs land without translation, and these three were missed —
            // dead as written, and correct only by accident, since the live names fell through to
            // the `Enum.TryParse` fallback and also produced `Keys.None`. Two ways to be right for
            // different reasons is how a rename goes unnoticed.
            "MOUSE1" or "MOUSE2" or "MOUSE3" or "MOUSE4" or "MOUSE5" => Keys.None,

            // **Digits, and this was a live defect until 2026-08-26** (B214). `NameOf` spells
            // `Keys.D1` as `"1"`, exactly as a config does — `config_default.cfg` binds every digit
            // to `slot1`..`slot10` — but the fallback below could not read one back. Worse than
            // failing: `Enum.TryParse` accepts a NUMERIC string as an enum value, so `"1"` became
            // `Keys.LButton`, `"2"` became `Keys.RButton`, and `"0"` became `Keys.None`. A digit
            // binding was silently attached to a key that cannot be pressed.
            [var only] when char.IsAsciiDigit(only) => Keys.D0 + (only - '0'),

            // **The numeric guard closes the same hole for anything else**, such as a hand-typed
            // `"13"`. A name is a NAME; a number that happens to index the enum is not one, and
            // accepting it turns a typo into a plausible key rather than into a reported miss.
            _ => !name.Trim().All(char.IsAsciiDigit) &&
                 Enum.TryParse(name.Trim(), ignoreCase: true, out Keys parsed)
                ? parsed
                : Keys.None,
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

        // The same Source spellings coming back the other way, so a name written into a settings
        // file round-trips and `WidgetKeys` is asked about `PGDN` rather than about `PageDown`.
        Keys.Up => "UPARROW",
        Keys.Down => "DOWNARROW",
        Keys.Left => "LEFTARROW",
        Keys.Right => "RIGHTARROW",
        Keys.PageUp => "PGUP",
        Keys.PageDown => "PGDN",
        Keys.Insert => "INS",
        Keys.Delete => "DEL",
        Keys.Back => "BACKSPACE",
        Keys.CapsLock => "CAPSLOCK",
        Keys.OemSemicolon => "SEMICOLON",
        Keys.OemOpenBrackets => "[",
        Keys.OemCloseBrackets => "]",

        >= Keys.A and <= Keys.Z => ((char)('a' + (key & Keys.KeyCode) - Keys.A)).ToString(),
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key & Keys.KeyCode) - Keys.D0)).ToString(),

        Keys.None => string.Empty,
        _ => Enum.GetName(key & Keys.KeyCode) ?? string.Empty,
    };

    /// <summary>What a config would call this mouse button.</summary>
    /// <param name="button">The button the toolkit reported.</param>
    /// <returns>The Source name, or an empty string for anything else.</returns>
    /// <remarks>
    /// **Source numbers its mouse buttons and every config in existence uses those names**: `MOUSE1`
    /// is left, `MOUSE2` right, `MOUSE3` the wheel click, and `MOUSE4`/`MOUSE5` the side buttons.
    /// TF2's own `config_default.cfg` writes `bind "MOUSE1" "+attack"`.
    ///
    /// **This is the reason mouse buttons can be bound at all.** They are not members of
    /// <see cref="Keys"/>, so the key path cannot carry them; naming them here lets a click go into
    /// the same console as a keystroke and mean whatever the player's config says it means.
    ///
    /// **`XButton1`/`XButton2` map to `MOUSE4`/`MOUSE5`**, which is the correspondence Windows and
    /// Source both use for the two thumb buttons.
    /// </remarks>
    public static string NameOf(MouseButtons button) => button switch
    {
        MouseButtons.Left => "MOUSE1",
        MouseButtons.Right => "MOUSE2",
        MouseButtons.Middle => "MOUSE3",
        MouseButtons.XButton1 => "MOUSE4",
        MouseButtons.XButton2 => "MOUSE5",
        _ => string.Empty,
    };
}
