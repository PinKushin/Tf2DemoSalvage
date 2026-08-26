using System;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>Where the ears are, and which way is right from them.</summary>
/// <param name="Origin">The listener's position, in Source units.</param>
/// <param name="Right">A unit vector pointing to the listener's right.</param>
public readonly record struct Ears(
    (float X, float Y, float Z) Origin,
    (float X, float Y, float Z) Right);

/// <summary>Derives the listener's basis from whichever camera is active.</summary>
/// <remarks>
/// **This was the arithmetic inside <c>MainForm.PlaySounds</c>** (B188, D90). Turning a camera into
/// a listener is a fact about Source's coordinate convention, not about a window.
///
/// **Verified against `AngleVectors` rather than assumed** (`mathlib/mathlib_base.cpp:936`). Valve's
/// full formula carries roll:
///
/// <code>
/// right->x = (-1*sr*sp*cy + -1*cr*-sy);
/// right->y = (-1*sr*sp*sy + -1*cr*cy);
/// right->z = -1*sr*cp;
/// </code>
///
/// With roll zero — `sr = 0`, `cr = 1` — that reduces to `(sin yaw, -cos yaw, 0)`, which is exactly
/// what this computes. **Pitch drops out of `right` entirely when roll is zero**, so ignoring it is
/// correct rather than a simplification worth apologising for: `sp` appears only multiplied by `sr`.
///
/// **A viewer has no roll**, because neither camera produces one — the free camera clamps pitch and
/// never rolls, and a recorded view's roll is not read. If one ever is, this is where the full
/// formula goes, and the citation above is the whole of it.
/// </remarks>
public static class SoundListener
{
    /// <summary>Degrees to radians, since Valve's angles are degrees on the wire and in configs.</summary>
    private const float Radians = MathF.PI / 180f;

    /// <summary>The listener for a camera, or null when there is no camera to listen from.</summary>
    /// <param name="camera">The active camera, or null.</param>
    /// <returns>Where to hear from, or null.</returns>
    /// <remarks>
    /// **Null rather than a listener at the origin.** A viewer with no camera has no ears, and
    /// placing them at (0,0,0) would attenuate every sound by its distance from the world origin —
    /// audible, wrong, and impossible to tell from a broken falloff curve.
    /// </remarks>
    public static Ears? From(FreeCamera? camera)
    {
        if (camera is not { } ears)
        {
            return null;
        }

        float yaw = ears.Angles.Yaw * Radians;

        return new Ears(
            (ears.Origin.X, ears.Origin.Y, ears.Origin.Z),
            (MathF.Sin(yaw), -MathF.Cos(yaw), 0f));
    }
}
