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
    public static bool IsFlightKey(Keys key, KeyBindings? bindings = null)
    {
        KeyBindings bound = bindings ?? Default;

        foreach (ViewerAction action in FlightActions)
        {
            if (IsDown(new HashSet<Keys> { key }, KeyNames.Resolve(bound.KeyFor(action))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The actions that move the free camera.</summary>
    /// <remarks>
    /// Listed once so <see cref="IsFlightKey"/> and <see cref="Intent"/> cannot disagree about what
    /// counts as flight — a key tracked as held but not read, or read but never tracked, produces a
    /// camera that moves once and stops.
    /// </remarks>
    private static readonly ViewerAction[] FlightActions =
    [
        ViewerAction.FlyForward,
        ViewerAction.FlyBack,
        ViewerAction.FlyLeft,
        ViewerAction.FlyRight,
        ViewerAction.FlyUp,
        ViewerAction.FlyDown,
    ];

    /// <summary>The camera's movement for one frame.</summary>
    /// <param name="held">Keys currently down.</param>
    /// <param name="seconds">How long the frame lasted.</param>
    /// <param name="pitch">Camera pitch in degrees.</param>
    /// <param name="yaw">Camera yaw in degrees.</param>
    /// <param name="fast">Whether Shift is held.</param>
    /// <param name="bindings">Which key performs which action, or null for the defaults.</param>
    /// <returns>The world-space movement, zero when nothing is held.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="held"/> is null.</exception>
    /// <remarks>
    /// **Up and down are along the WORLD's up axis, not the camera's**, which is what every editor
    /// does: rising along a pitched view drifts sideways and reads as broken. Forward and right come
    /// from <c>AngleVectors</c>, the same pair the camera itself builds.
    ///
    /// The direction is normalised, so holding W and A is not faster than holding W alone — the
    /// mistake that makes diagonal movement quicker in a lot of homemade cameras.
    /// </remarks>
    public static (float X, float Y, float Z) Movement(
        IReadOnlySet<Keys> held,
        double seconds,
        float pitch,
        float yaw,
        bool fast,
        KeyBindings? bindings = null) =>
        FreeFlightPath.Movement(Intent(held, fast, bindings), seconds, pitch, yaw);

    /// <summary>Translates the keys currently down into an axis request.</summary>
    /// <param name="held">Keys currently down.</param>
    /// <param name="fast">Whether Shift is held.</param>
    /// <param name="bindings">Which key performs which action, or null for the defaults.</param>
    /// <returns>What the user is asking for, independent of the keyboard.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="held"/> is null.</exception>
    /// <remarks>
    /// **This is all that is left here, and it is the only part that is genuinely about a keyboard.**
    /// The geometry moved to <see cref="FreeFlightPath"/> in the presentation layer (D65), because
    /// it is trigonometry and had no business behind a WinForms type — while it took an
    /// <c>IReadOnlySet&lt;Keys&gt;</c>, exercising it meant constructing key sets, which is why a
    /// function of sines and cosines had no direct tests for weeks.
    ///
    /// Splitting them found a real defect immediately: forward and up cancel when looking straight
    /// down, and the original guard tested the resulting length for exactly zero. Floating point
    /// gives 4.4e-8 instead, which the normalisation scaled up to full speed.
    ///
    /// Rebinding these keys would change this method and nothing else, which is the test of whether
    /// the seam is in the right place.
    /// </remarks>
    public static FlightInput Intent(
        IReadOnlySet<Keys> held, bool fast, KeyBindings? bindings = null)
    {
        ArgumentNullException.ThrowIfNull(held);

        if (held.Count == 0)
        {
            return FlightInput.None;
        }

        KeyBindings bound = bindings ?? Default;

        return new FlightInput(
            Forward: Axis(held, bound, ViewerAction.FlyForward, ViewerAction.FlyBack),
            Right: Axis(held, bound, ViewerAction.FlyRight, ViewerAction.FlyLeft),
            Up: Axis(held, bound, ViewerAction.FlyUp, ViewerAction.FlyDown),
            Fast: fast);
    }

    /// <summary>Whether the key bound to an action is currently down.</summary>
    /// <remarks>
    /// **Control has three key codes and only one of them is what Windows reports.** A held Ctrl
    /// arrives as `ControlKey`, `LControlKey` or `RControlKey` depending on how it was read, so a
    /// binding of "Control" has to answer to all three or the camera never descends. The same is
    /// true of Shift and Alt, which is why this checks the sided variants rather than the bound key
    /// alone.
    /// </remarks>
    private static bool IsDown(IReadOnlySet<Keys> held, Keys key) => key switch
    {
        Keys.ControlKey => held.Contains(Keys.ControlKey) ||
                           held.Contains(Keys.LControlKey) ||
                           held.Contains(Keys.RControlKey),

        Keys.ShiftKey => held.Contains(Keys.ShiftKey) ||
                         held.Contains(Keys.LShiftKey) ||
                         held.Contains(Keys.RShiftKey),

        Keys.Menu => held.Contains(Keys.Menu) ||
                     held.Contains(Keys.LMenu) ||
                     held.Contains(Keys.RMenu),

        Keys.None => false,
        _ => held.Contains(key),
    };

    /// <summary>One axis: the positive action's key minus the negative one's.</summary>
    private static float Axis(
        IReadOnlySet<Keys> held, KeyBindings bindings, ViewerAction positive, ViewerAction negative)
    {
        float forward = IsDown(held, KeyNames.Resolve(bindings.KeyFor(positive))) ? 1f : 0f;
        float back = IsDown(held, KeyNames.Resolve(bindings.KeyFor(negative))) ? 1f : 0f;

        return forward - back;
    }

    /// <summary>The default bindings, so a caller with none still flies.</summary>
    private static readonly KeyBindings Default = new();
}
