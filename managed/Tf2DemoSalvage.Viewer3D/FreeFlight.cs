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
    // **Two forwarded constants were here until 2026-08-26** (B188, D90):
    // `SpeedPerSecond` and `ShiftMultiplier`, both `= FreeFlightPath.<the same thing>`.
    //
    // **`SpeedPerSecond` was read by NOTHING** — not production, not a test. A public constant in a
    // view, forwarding a number nobody ever asked this type for. `ShiftMultiplier` had one reader,
    // a test, which asks `FreeFlightPath` directly now.
    //
    // Their own documentation argued the case against them: *"Two copies of a speed is two speeds
    // waiting to disagree"*. That is exactly right, and forwarding is a second copy — the name is
    // duplicated even where the value is not, so a reader has two places to look and a rename has
    // two places to reach.
    //
    // The reasoning about the SPEED itself belongs with the speed and is in `FreeFlightPath`: 600
    // units a second, against a player's 300, because the old 32-units-per-keypress at Windows'
    // ~31/second repeat rate worked out to over 900 — three times a scout's sprint.

    /// <summary>Whether a key contributes to flight, so the caller knows to track it.</summary>
    /// <param name="key">The key, without modifiers.</param>
    /// <param name="bindings">Which key performs which action, or null for the defaults.</param>
    /// <returns>Whether it is a flight key.</returns>
    /// <remarks>
    /// **Compared by NAME rather than by key code, which is what collapses the sided modifiers.**
    /// Windows reports a held Shift as `ShiftKey`, `LShiftKey` or `RShiftKey` depending on how it
    /// was read, and a config binds one name — `SHIFT`. <see cref="KeyNames.NameOf(System.Windows.Forms.Keys)"/> maps all three
    /// onto it, so this and the console agree by construction instead of by both remembering to
    /// special-case the same three codes. That special-casing used to live here, in an `IsDown`
    /// helper, and having it in two places is exactly how one side gains a key the other does not
    /// know about.
    /// </remarks>
    public static bool IsFlightKey(Keys key, KeyBindings? bindings = null)
        =>
        // **What is left is the only part that is genuinely this toolkit's**: turning a WinForms
        // `Keys` value into a Source key name. Deciding what that name MEANS was a loop over
        // `ConfigConsole.HeldActions` compared against `KeyBindings.KeyFor` — two Presentation
        // tables joined in a view — and is `ConfigConsole.IsHeldKey` now (B188, D90).
        ConfigConsole.IsHeldKey(KeyNames.NameOf(key), bindings ?? Default);

    // **`FlightActions` was here until 2026-08-26** (B208), and its own comment was the finding: it
    // claimed the actions were "listed once so `IsFlightKey` and the console cannot disagree about
    // what counts as flight" — while listing all seven a second time, in a second project, beside
    // `ConfigConsole.HeldActions`. They could disagree, and nothing would have said so.
    //
    // The failure that comment describes is why it mattered: a key swallowed but never pressed into
    // the console, or pressed but never swallowed, produces a camera that moves once and stops.
    //
    // `IsFlightKey` reads `ConfigConsole.HeldActions` now, so the two questions — "does the console
    // hold this action down" and "must this window swallow the key" — are answered from one list.
    // The Shift/`+speed` reasoning (D69) went with it.

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
