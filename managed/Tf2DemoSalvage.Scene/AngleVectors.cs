using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>The basis a Source angle triple describes — Valve's <c>AngleVectors</c>.</summary>
/// <remarks>
/// **Valve has one function for this and we had four copies of it** (B204). Found while auditing
/// `MainForm` for domain knowledge: the mouse-wheel handler was computing a forward vector inline,
/// and grepping for the arithmetic turned up three more sites —
///
/// | site | what it inlined |
/// |---|---|
/// | `FlightInput.Direction` | forward **and** right |
/// | `FreeCamera.Orbiting` | forward |
/// | `MainForm.OnViewportWheel` | forward |
/// | `SoundListener.From` | right |
///
/// Four copies of one formula is four chances to fix a sign in one of them, and the symptom of a
/// disagreement is not a crash — it is a camera that flies slightly wrong in one mode.
///
/// **The citation is `mathlib/mathlib_base.cpp:906-947`:**
///
/// <code>
/// SinCos( DEG2RAD( angles[YAW] ), &amp;sy, &amp;cy );
/// SinCos( DEG2RAD( angles[PITCH] ), &amp;sp, &amp;cp );
///
/// forward->x = cp*cy;
/// forward->y = cp*sy;
/// forward->z = -sp;
///
/// right->x = (-1*sr*sp*cy + -1*cr*-sy);
/// right->y = (-1*sr*sp*sy + -1*cr*cy);
/// right->z = -1*sr*cp;
/// </code>
///
/// **This is deliberately NOT a general vector library.** It is one Valve function, reproduced
/// exactly, because the project's first principle is parity (D89) and a "nicer" formulation is a
/// place for a divergence to hide.
/// </remarks>
public static class AngleVectors
{
    /// <summary>Degrees to radians. Source's angles are degrees on the wire and in configs.</summary>
    private const float Radians = MathF.PI / 180f;

    /// <summary>Where an angle pair is looking.</summary>
    /// <param name="pitch">Pitch in degrees; positive looks DOWN.</param>
    /// <param name="yaw">Yaw in degrees.</param>
    /// <returns>A unit vector.</returns>
    /// <remarks>
    /// **`Z` is `-sin(pitch)`, and the sign is the trap.** A positive pitch looks down in Source.
    /// Flipping it leaves all horizontal motion perfect and inverts every look, which reads as a
    /// preference setting rather than as a maths error.
    ///
    /// **Roll does not enter `forward` at all**, in Valve's formula or here — it is absent from all
    /// three components, not merely negligible.
    /// </remarks>
    public static (float X, float Y, float Z) Forward(float pitch, float yaw)
    {
        (float sinPitch, float cosPitch) = MathF.SinCos(pitch * Radians);
        (float sinYaw, float cosYaw) = MathF.SinCos(yaw * Radians);

        return (cosPitch * cosYaw, cosPitch * sinYaw, -sinPitch);
    }

    /// <summary>Which way is right, for a view with no roll.</summary>
    /// <param name="yaw">Yaw in degrees.</param>
    /// <returns>A unit vector in the XY plane.</returns>
    /// <remarks>
    /// **Valve's `right` reduced at roll zero, which is exact rather than approximate.** With
    /// `sr = 0` and `cr = 1` the published formula collapses to `(sy, -cy, 0)`: **pitch drops out
    /// entirely**, because `sp` appears only multiplied by `sr`. Ignoring pitch here is therefore
    /// correct and not a simplification.
    ///
    /// **Nothing in this viewer rolls** — the free camera clamps pitch and never rolls, and a
    /// recorded view's roll is not read. If one ever is, the full three-line form above is what
    /// replaces this, and it needs a `roll` parameter rather than a correction.
    /// </remarks>
    public static (float X, float Y, float Z) Right(float yaw)
    {
        (float sinYaw, float cosYaw) = MathF.SinCos(yaw * Radians);

        return (sinYaw, -cosYaw, 0f);
    }

    /// <summary>The UP vector of an angle pair — <c>AngleVectors</c>' third output.</summary>
    /// <param name="pitch">Pitch in degrees.</param>
    /// <param name="yaw">Yaw in degrees.</param>
    /// <returns>The basis's up vector.</returns>
    /// <remarks>
    /// **<c>mathlib_base.cpp:953</c>**, written for a general roll:
    ///
    /// <code>
    ///   up-&gt;x = (cr*sp*cy + -sr*-sy);
    ///   up-&gt;y = (cr*sp*sy + -sr*cy);
    ///   up-&gt;z = cr*cp;
    /// </code>
    ///
    /// **With <c>sr = 0</c> and <c>cr = 1</c> that collapses to <c>(sp·cy, sp·sy, cp)</c>** — and
    /// unlike <see cref="Right"/>, PITCH does not drop out: it is the whole of the first two
    /// components. So this needs both angles where that one needs only yaw, which is the reason the
    /// two have different signatures rather than an oversight.
    ///
    /// Nothing in this viewer rolls, as recorded on <see cref="Right"/>. If one ever does, the
    /// three-line form above is what replaces this.
    /// </remarks>
    public static (float X, float Y, float Z) Up(float pitch, float yaw)
    {
        (float sinPitch, float cosPitch) = MathF.SinCos(pitch * Radians);
        (float sinYaw, float cosYaw) = MathF.SinCos(yaw * Radians);

        return (sinPitch * cosYaw, sinPitch * sinYaw, cosPitch);
    }
}
