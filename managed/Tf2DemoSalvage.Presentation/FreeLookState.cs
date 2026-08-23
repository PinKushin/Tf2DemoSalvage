using System;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// Where the free camera is and which way it faces, and how the mouse and keyboard change that.
/// </summary>
/// <remarks>
/// **Lifted out of `MainForm` (D66), where it was three fields and a pair of event handlers.** It is
/// pure state and arithmetic — no viewport, no control, no logging — so it belongs on this side of
/// the boundary and can finally be tested.
///
/// **Pitch is clamped and yaw is not, and that asymmetry is the engine's.** Looking exactly along
/// the world's up axis makes the camera basis degenerate: forward becomes parallel to up and the
/// right vector is undefined. The engine clamps a player to ±89 for the same reason, so this does
/// too. Yaw has no such problem — it wraps around and every value is a legal heading — and forcing
/// it into a range would only introduce a discontinuity where none exists.
///
/// **The degeneracy is not theoretical here.** `FreeFlightPath` inherited a guard that tested a
/// cancelled movement vector for exactly zero; at pitch 90 the residue from `cos(90°)` is 4.4e-8
/// rather than 0, and normalising it sent the camera 300 units sideways (D65). Clamping at the
/// source is the other half of that fix.
/// </remarks>
public sealed class FreeLookState
{
    /// <summary>How far the camera turns per pixel of mouse drag.</summary>
    /// <remarks>
    /// A quarter of a degree, so a 360 needs about 1,440 pixels of travel — deliberate rather than
    /// twitchy, because this camera is used to line a shot up rather than to fight with.
    /// </remarks>
    public const float DegreesPerPixel = 0.25f;

    /// <summary>The steepest angle the camera may look, up or down.</summary>
    /// <remarks>
    /// **±89, matching what the engine clamps a player to**, and for the same reason: at exactly 90
    /// the forward vector is parallel to the world's up axis and the basis has no right vector.
    /// </remarks>
    public const float PitchLimit = 89f;

    /// <summary>Which way the camera faces, in degrees, pitch positive downwards.</summary>
    public (float Pitch, float Yaw) Angles { get; private set; } = (35f, 0f);

    /// <summary>Where the camera is, or null until it has been placed.</summary>
    /// <remarks>
    /// Null means "not yet placed", which is different from the origin: the camera is placed the
    /// first time it is needed by orbiting whatever the map view was centred on, so entering the
    /// free view does not move the subject.
    /// </remarks>
    public (float X, float Y, float Z)? Origin { get; private set; }

    /// <summary>Whether the camera has been placed.</summary>
    public bool IsPlaced => Origin is not null;

    /// <summary>Turns the camera by a mouse drag, in viewport pixels.</summary>
    /// <param name="deltaX">Pixels moved right.</param>
    /// <param name="deltaY">Pixels moved down.</param>
    /// <remarks>
    /// **Dragging right turns left**, which is the convention every first-person game uses: the
    /// world moves with the pointer, as though the mouse were dragging the scene rather than the
    /// head. Inverting this is the single most noticeable thing a camera can get wrong.
    /// </remarks>
    public void Drag(float deltaX, float deltaY) =>
        Angles = (
            Math.Clamp(Angles.Pitch + (deltaY * DegreesPerPixel), -PitchLimit, PitchLimit),
            Angles.Yaw - (deltaX * DegreesPerPixel));

    /// <summary>Places the camera outright, as an environment-supplied viewpoint does.</summary>
    /// <param name="origin">Where to put it.</param>
    /// <param name="pitch">Pitch in degrees; clamped.</param>
    /// <param name="yaw">Yaw in degrees.</param>
    /// <remarks>
    /// **The pitch is clamped here too, and that was a real defect.** `TF2DEMOSALVAGE_CAMERA` exists
    /// so a viewpoint can be copied straight out of the game's own `ang` readout for parity work —
    /// and 90 is an ordinary thing to copy. The original path applied it unclamped, which put the
    /// camera in the degenerate state the drag had always been careful to avoid (D65).
    /// </remarks>
    public void PlaceAt((float X, float Y, float Z) origin, float pitch, float yaw)
    {
        Origin = origin;
        Angles = (Math.Clamp(pitch, -PitchLimit, PitchLimit), yaw);
    }

    /// <summary>Places the camera without changing where it looks.</summary>
    /// <param name="origin">Where to put it.</param>
    public void PlaceAt((float X, float Y, float Z) origin) => Origin = origin;

    /// <summary>Flies the camera for one frame.</summary>
    /// <param name="input">What the user is asking for.</param>
    /// <param name="seconds">How long the frame took.</param>
    /// <returns>Whether the camera actually moved.</returns>
    /// <remarks>
    /// Returns whether anything changed so the host can skip a redraw it does not need. An idle
    /// frame is the common case — the keys are up most of the time — and repainting for it is the
    /// mistake that made the transport buttons sluggish once already.
    /// </remarks>
    public bool Fly(FlightInput input, double seconds)
    {
        if (Origin is not { } where)
        {
            // Nothing to fly from yet. The camera is placed on first use, and flying before that
            // would silently define the origin as (0,0,0) — the corner of the map.
            return false;
        }

        (float x, float y, float z) = FreeFlightPath.Movement(
            input, seconds, Angles.Pitch, Angles.Yaw);

        if ((x, y, z) == (0f, 0f, 0f))
        {
            return false;
        }

        Origin = (where.X + x, where.Y + y, where.Z + z);

        return true;
    }

    /// <summary>Forgets the placement, so the camera is placed afresh next time it is needed.</summary>
    /// <remarks>
    /// Used when a new demo or map is loaded: the previous position is somewhere in a world that no
    /// longer exists, and keeping it would drop the camera outside the new map.
    /// </remarks>
    public void Unplace() => Origin = null;
}
