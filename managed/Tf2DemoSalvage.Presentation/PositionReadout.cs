using System;
using System.Collections.Generic;
using System.Globalization;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// TF2's <c>cl_showpos</c> readout: where the view is, which way it faces, and how fast.
/// </summary>
/// <param name="Mode">
/// <c>cl_showpos</c>'s value. Zero or below draws nothing; 2 reports the player; anything else
/// above zero reports the view.
/// </param>
/// <param name="Camera">The view's origin — Valve's <c>MainViewOrigin()</c>.</param>
/// <param name="CameraAngles">The view's angles — Valve's <c>MainViewAngles()</c>.</param>
/// <param name="Player">The watched player's <c>GetAbsOrigin()</c>, for mode 2.</param>
/// <param name="PlayerAngles">The watched player's <c>GetAbsAngles()</c>, for mode 2.</param>
/// <param name="Speed">
/// The player's speed — the LENGTH of <c>GetLocalVelocity()</c>, reported in both modes.
/// </param>
/// <remarks>
/// **Transcribed from <c>game/client/vgui_fpspanel.cpp:316</c>**, inside `CFPSPanel::Paint`:
///
/// <code>
///   int nShowPosMode = cl_showpos.GetInt();
///   if ( nShowPosMode &gt; 0 )
///   {
///       Vector vecOrigin = MainViewOrigin();
///       QAngle angles    = MainViewAngles();
///       if ( nShowPosMode == 2 ) { ...GetLocalPlayer()... }
///       "pos:  %.02f %.02f %.02f"
///       "ang:  %.02f %.02f %.02f"
///       vel = player ? player-&gt;GetLocalVelocity() : vec3_origin;
///       "vel:  %.2f"
///   }
/// </code>
///
/// **The velocity line is outside the mode branch**, so it is the player's speed whichever subject
/// `pos` and `ang` are showing. Folding it into the mode would report zero for a moving player
/// whenever the camera is the subject, which in this viewer is the ordinary case.
///
/// **It lives beside <see cref="ToolsPanel"/> because that is where Valve keeps it** — one panel,
/// `CFPSPanel`, draws the frame rate and this, and `ShouldDraw` returns true when EITHER convar is
/// on. Ours composes them into the same block for the same reason.
///
/// **The owner asked for it as an instrument**, to read positions off a screenshot: *"we should
/// display the position of the camera like cl_showpos in our viewer so we can SS that and you can
/// figure out the positions everything happens at"*. Numbers rounded differently from the game's
/// could not be compared with the game's, which is the whole use, so the formats are pinned by
/// `PositionReadoutConformanceTests` rather than chosen.
/// </remarks>
public readonly record struct PositionReadout(
    int Mode,
    (float X, float Y, float Z) Camera,
    (float Pitch, float Yaw, float Roll) CameraAngles,
    (float X, float Y, float Z) Player,
    (float Pitch, float Yaw, float Roll) PlayerAngles,
    float Speed)
{
    /// <summary>Drawn nothing — <c>cl_showpos 0</c>, and the convar's own default.</summary>
    public const int Hidden = 0;

    /// <summary>The view's own origin and angles — <c>cl_showpos 1</c>.</summary>
    public const int View = 1;

    /// <summary>The watched player's origin and angles — <c>cl_showpos 2</c>.</summary>
    public const int Player2 = 2;

    /// <summary>Whether anything is drawn at all.</summary>
    /// <remarks>
    /// `if ( nShowPosMode > 0 )` — greater than zero, so a negative value is off. That is not the
    /// same test as "non-zero", and a convar can hold a negative.
    /// </remarks>
    public bool Visible => Mode > Hidden;

    /// <summary>The lines to draw, top to bottom, or empty when hidden.</summary>
    /// <remarks>
    /// **Only mode 2 swaps the subject**, so `cl_showpos 3` draws the view rather than nothing —
    /// the outer test is `> 0` and the inner one is `== 2`. A reading that treated an unknown mode
    /// as invalid would make a mistyped convar silently blank, which the engine does not do.
    ///
    /// The separator is TWO spaces on all three lines. `%.02f` and `%.2f` print identically in C —
    /// the `0` is a width, and a width of 2 never pads a number of this size — so both become
    /// <c>F2</c> here; they are written differently in Valve's source and mean the same thing.
    /// </remarks>
    public IReadOnlyList<string> Lines
    {
        get
        {
            if (!Visible)
            {
                return [];
            }

            bool player = Mode == Player2;

            (float x, float y, float z) = player ? Player : Camera;
            (float pitch, float yaw, float roll) = player ? PlayerAngles : CameraAngles;

            return
            [
                $"pos:  {Fixed(x)} {Fixed(y)} {Fixed(z)}",
                $"ang:  {Fixed(pitch)} {Fixed(yaw)} {Fixed(roll)}",
                $"vel:  {Fixed(Speed)}",
            ];
        }
    }

    /// <summary>Two decimal places, in the invariant culture.</summary>
    /// <remarks>
    /// **Invariant deliberately.** The readout exists to be compared against the game's, and a
    /// machine whose locale writes <c>1802,00</c> could not be. Culture belongs to text a person
    /// reads as prose, never to a diagnostic that has to line up with another program's output.
    /// </remarks>
    private static string Fixed(float value) =>
        value.ToString("F2", CultureInfo.InvariantCulture);
}
