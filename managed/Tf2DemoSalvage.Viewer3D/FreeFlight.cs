using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>
/// Turns the set of held keys and a frame's duration into a camera movement.
/// </summary>
/// <remarks>
/// **Movement per frame, not per keystroke, and the difference is the defect this replaces (B97).**
/// The free camera used to move once per <c>WM_KEYDOWN</c>, which means Windows' auto-repeat decided
/// how it flew: nothing for the repeat delay, then a fixed jump at the repeat rate, and never two
/// directions at once because auto-repeat only ever reports the last key. The owner's description was
/// "single clicks that repeat, like typing in notepad".
///
/// Three things follow from integrating instead, and only the first is the bug report:
///
/// - motion starts the moment a key goes down and is smooth for as long as it is held;
/// - diagonals work, because every held key contributes;
/// - speed is in units per SECOND, so it no longer depends on the machine's keyboard settings or
///   on the frame rate.
///
/// **Separated from the form so it can be tested.** The movement is a pure function of held keys,
/// elapsed time and the camera's angles; leaving it inside a message handler made it reachable only
/// by driving a real window.
/// </remarks>
internal static class FreeFlight
{
    /// <summary>World units per second the camera flies.</summary>
    /// <remarks>
    /// **A speed now, where it used to be a distance per keypress.** The old 32 units per press at
    /// Windows' default repeat rate of about 31 a second is a little over 900 units a second, and a
    /// player runs at 300 — so matching the old feel exactly would be three times a scout's sprint.
    /// 600 is fast enough to cross cp_process without waiting and slow enough to line up a shot,
    /// and Shift still quadruples it.
    ///
    /// **Forwarded rather than declared**, since the geometry moved to the presentation layer (D65)
    /// and the number belongs with the code that applies it. Two copies of a speed is two speeds
    /// waiting to disagree, and the disagreement would show as a camera that flies at one rate and
    /// reports another.
    /// </remarks>
    public const float SpeedPerSecond = FreeFlightPath.SpeedPerSecond;

    /// <summary>How much faster Shift flies.</summary>
    /// <inheritdoc cref="SpeedPerSecond" path="/remarks/para[last()]"/>
    public const float ShiftMultiplier = FreeFlightPath.FastMultiplier;

    /// <summary>Whether a key contributes to flight, so the caller knows to track it.</summary>
    /// <param name="key">The key, without modifiers.</param>
    /// <param name="bindings">Which key performs which action, or null for the defaults.</param>
    /// <returns>Whether it is a flight key.</returns>
    /// <remarks>
    /// **Compared by NAME rather than by key code, which is what collapses the sided modifiers.**
    /// Windows reports a held Shift as `ShiftKey`, `LShiftKey` or `RShiftKey` depending on how it
    /// was read, and a config binds one name — `SHIFT`. <see cref="KeyNames.NameOf"/> maps all three
    /// onto it, so this and the console agree by construction instead of by both remembering to
    /// special-case the same three codes. That special-casing used to live here, in an `IsDown`
    /// helper, and having it in two places is exactly how one side gains a key the other does not
    /// know about.
    /// </remarks>
    public static bool IsFlightKey(Keys key, KeyBindings? bindings = null)
    {
        KeyBindings bound = bindings ?? Default;
        string name = KeyNames.NameOf(key);

        if (name.Length == 0)
        {
            return false;
        }

        foreach (ViewerAction action in FlightActions)
        {
            if (string.Equals(name, bound.KeyFor(action), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The actions that move the free camera.</summary>
    /// <remarks>
    /// Listed once so <see cref="IsFlightKey"/> and the console cannot disagree about what counts as
    /// flight — a key swallowed but never pressed into the console, or pressed but never swallowed,
    /// produces a camera that moves once and stops.
    /// </remarks>
    private static readonly ViewerAction[] FlightActions =
    [
        ViewerAction.FlyForward,
        ViewerAction.FlyBack,
        ViewerAction.FlyLeft,
        ViewerAction.FlyRight,
        ViewerAction.FlyUp,
        ViewerAction.FlyDown,

        // **Shift belongs here now, and did not before (D69).** It used to be read straight off
        // `Control.ModifierKeys` on the grounds that a modifier's state is something WinForms
        // already knows. That stopped being true when the console took over the controls: `+speed`
        // is a bound command like any other, so Shift has to be pressed INTO the console or the
        // speed multiplier never fires — and it would fail silently, as a camera that simply never
        // goes fast.
        //
        // The consequence is that a bare Shift press is swallowed while the free camera is active,
        // which is the same treatment every other bound key gets.
        ViewerAction.FlyFast,
    ];

    // `Movement`, `Intent`, `Axis` and `IsDown` were removed here (D69). They turned a
    // `HashSet<Keys>` plus a binding table into an axis request, and `ConfigConsole` does that job
    // now — from a script rather than a lookup, which is what a real TF2 config requires. Eleven
    // tests went on passing against them for as long as they sat here unused, so they are gone
    // rather than kept "in case": dead code with live tests reads as covered.
    //
    // What remains is the one thing that is genuinely about this toolkit: deciding whether a
    // `Keys` value is a flight key at all, so `ProcessCmdKey` knows whether to swallow it.

    /// <summary>The default bindings, so a caller with none still flies.</summary>
    private static readonly KeyBindings Default = new();
}
