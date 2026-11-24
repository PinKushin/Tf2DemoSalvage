using System;
using System.Text;

using Tf2DemoSalvage.Core.Primitives;

namespace Tf2DemoSalvage.Presentation;

/// <summary>TF2's key names, and the lookup `bind` and `unbind` run on a name (B826).</summary>
/// <remarks>
/// **Read from `inputsystem.dll` x64 of the TF2 install (headless Ghidra, 2026-11-24).** `engine.dll` build 1007's `bind`
/// (FUN_1801b5ea0) and `unbind` (FUN_1801b6a30) call the input system through its interface (vtable +0xe8, the thunk
/// FUN_180003630), which calls FUN_180005880 with a member flag (byte +0x29c5) that admits the X-controller names too. That
/// lookup:
///
/// 1. an empty name is no key;
/// 2. a name starting `aux` (any case, `V_strnicmp`) is the legacy joystick spelling: `atoi` of the rest, below 29 the code
///    `n + 0x72` (`JOY1` at 114), 29 to 32 the code `n + 0x75` (`POV_UP` at 146), anything else no key;
/// 3. otherwise the first of the 635 names in the table at 0x180034430 (`ButtonCode_t` order) that equals it ignoring
///    ASCII case (`V_stricmp`, FUN_1800086f0: a byte of 0x80 or more must match exactly), its index the code;
/// 4. with the member flag set, also 48 X-controller names (`A_BUTTON` …). *Not modelled*: the flag is set with a game
///    controller configured, which this viewer never has.
///
/// The table repeats the controller names once per controller; only the first of each is reachable, so this keeps the
/// distinct ones. Only the positions `aux` can reach matter, and those are the first 171 entries, kept in order.
/// </remarks>
public static class ButtonCodeNames
{
    /// <summary>The table's first 171 entries, at their codes: `KEY_NONE` (""), the keys, mouse, joystick, POV and axes.</summary>
    private static readonly string[] Ordered =
    [
        "",
        "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
        "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m",
        "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
        "KP_INS", "KP_END", "KP_DOWNARROW", "KP_PGDN", "KP_LEFTARROW", "KP_5", "KP_RIGHTARROW", "KP_HOME", "KP_UPARROW",
        "KP_PGUP", "KP_SLASH", "KP_MULTIPLY", "KP_MINUS", "KP_PLUS", "KP_ENTER", "KP_DEL",
        "[", "]", "SEMICOLON", "'", "`", ",", ".", "/", "\\", "-", "=",
        "ENTER", "SPACE", "BACKSPACE", "TAB", "CAPSLOCK", "NUMLOCK", "ESCAPE", "SCROLLLOCK", "INS", "DEL", "HOME", "END",
        "PGUP", "PGDN", "PAUSE", "SHIFT", "RSHIFT", "ALT", "RALT", "CTRL", "RCTRL", "LWIN", "RWIN", "APP",
        "UPARROW", "LEFTARROW", "DOWNARROW", "RIGHTARROW",
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
        "CAPSLOCKTOGGLE", "NUMLOCKTOGGLE", "SCROLLLOCKTOGGLE",
        "MOUSE1", "MOUSE2", "MOUSE3", "MOUSE4", "MOUSE5", "MWHEELUP", "MWHEELDOWN",
        "JOY1", "JOY2", "JOY3", "JOY4", "JOY5", "JOY6", "JOY7", "JOY8", "JOY9", "JOY10", "JOY11", "JOY12", "JOY13", "JOY14",
        "JOY15", "JOY16", "JOY17", "JOY18", "JOY19", "JOY20", "JOY21", "JOY22", "JOY23", "JOY24", "JOY25", "JOY26",
        "JOY27", "JOY28", "JOY29", "JOY30", "JOY31", "JOY32",
        "POV_UP", "POV_RIGHT", "POV_DOWN", "POV_LEFT",
        "X AXIS POS", "X AXIS NEG", "Y AXIS POS", "Y AXIS NEG", "Z AXIS POS", "Z AXIS NEG", "R AXIS POS", "R AXIS NEG",
        "U AXIS POS", "U AXIS NEG", "V AXIS POS", "V AXIS NEG",
        "FALCON_NULL", "FALCON_1", "FALCON_2", "FALCON_3", "FALCON_4", "FALCON2_1", "FALCON2_2", "FALCON2_3", "FALCON2_4",
    ];

    /// <summary>The rest of the table's names, each once: the Steam Controller's buttons, axes and function keys.</summary>
    private static readonly string[] Controller =
    [
        "SC_A", "SC_B", "SC_X", "SC_Y", "SC_DPAD_UP", "SC_DPAD_RIGHT", "SC_DPAD_DOWN", "SC_DPAD_LEFT",
        "SC_LEFT_BUMPER", "SC_RIGHT_BUMPER", "SC_LEFT_TRIGGER", "SC_RIGHT_TRIGGER", "SC_LEFT_GRIP", "SC_RIGHT_GRIP",
        "SC_LEFT_PAD_TOUCH", "SC_RIGHT_PAD_TOUCH", "SC_LEFT_PAD_CLICK", "SC_RIGHT_PAD_CLICK",
        "SC_LPAD_UP", "SC_LPAD_RIGHT", "SC_LPAD_DOWN", "SC_LPAD_LEFT", "SC_RPAD_UP", "SC_RPAD_RIGHT", "SC_RPAD_DOWN",
        "SC_RPAD_LEFT", "SC_SELECT", "SC_START", "SC_STEAM", "SC_NULL",
        "SC_LPAD_AXIS_RIGHT", "SC_LPAD_AXIS_LEFT", "SC_LPAD_AXIS_DOWN", "SC_LPAD_AXIS_UP", "SC_AXIS_L_TRIGGER",
        "SC_AXIS_R_TRIGGER", "SC_RPAD_AXIS_RIGHT", "SC_RPAD_AXIS_LEFT", "SC_RPAD_AXIS_DOWN", "SC_RPAD_AXIS_UP",
        "SC_GYRO_AXIS_PITCH_POSITIVE", "SC_GYRO_AXIS_PITCH_NEGATIVE", "SC_GYRO_AXIS_ROLL_POSITIVE",
        "SC_GYRO_AXIS_ROLL_NEGATIVE", "SC_GYRO_AXIS_YAW_POSITIVE", "SC_GYRO_AXIS_YAW_NEGATIVE",
        "SC_F1", "SC_F2", "SC_F3", "SC_F4", "SC_F5", "SC_F6", "SC_F7", "SC_F8", "SC_F9", "SC_F10", "SC_F11", "SC_F12",
    ];

    /// <summary>The modifiers a chord may name, as the view's `KeyNames` reads them (above Valve).</summary>
    private static readonly string[] Modifiers = ["CTRL", "CONTROL", "SHIFT", "ALT"];

    /// <summary>The key a name names, in the table's own spelling, or null where TF2 says `"%s" isn't a valid key`.</summary>
    /// <param name="name">The name as a config or console line wrote it.</param>
    /// <returns>The table's name; or, for a chord, the chord as written.</returns>
    /// <remarks>
    /// **A chord is above Valve** (`docs/ABOVE-VALVE.md`): TF2 has none, so no TF2 config holds one, and the viewer's own
    /// defaults and menu use them. One or more modifiers and `+` before a name the table knows; kept as written, since the
    /// view resolves it.
    /// </remarks>
    public static string? KeyOf(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        // TF2's own lookup first (B826 review 1, Fable, Grok): `aux+1` is `aux` and `atoi("+1")`, `JOY2`, not a chord.
        if (TableKey(name) is { } key)
        {
            return key;
        }

        // The chord's key in the table's spelling (B826 review 1, Fable), so `CTRL+aux1` is `CTRL+JOY2` and the view can
        // resolve it; its modifiers as written.
        return name.LastIndexOf('+') is var plus and > 0 &&
               IsModifiers(name.AsSpan(0, plus)) && TableKey(name[(plus + 1)..]) is { } chordKey
            ? name[..(plus + 1)] + chordKey
            : null;
    }

    /// <summary>The input system's own lookup, without the chord extension.</summary>
    private static string? TableKey(string name)
    {
        if (name.Length == 0)
        {
            return null;
        }

        if (name.Length >= 3 && Ascii.EqualsIgnoreCase(name.AsSpan(0, 3), "aux"))
        {
            int n = CStdlib.Atoi(name.AsSpan(3));

            if (n >= 33)
            {
                return null;
            }

            long code = n < 29 ? (long)n + 0x72 : n + 0x75;

            // A code below 1 is `KEY_NONE` or past the table's start, which TF2 would bind or read out of bounds; no key here
            // (*interpolated*: only `aux-114` and below reach it).
            return code is > 0 and < 171 ? Ordered[code] : null;
        }

        foreach (string key in Ordered)
        {
            if (key.Length > 0 && Ascii.EqualsIgnoreCase(key, name))
            {
                return key;
            }
        }

        foreach (string key in Controller)
        {
            if (Ascii.EqualsIgnoreCase(key, name))
            {
                return key;
            }
        }

        return null;
    }

    /// <summary>Whether text is one or more chord modifiers joined by `+`.</summary>
    private static bool IsModifiers(ReadOnlySpan<char> text)
    {
        foreach (Range part in text.Split('+'))
        {
            bool known = false;

            foreach (string modifier in Modifiers)
            {
                known |= Ascii.EqualsIgnoreCase(text[part], modifier);
            }

            if (!known)
            {
                return false;
            }
        }

        return true;
    }
}
