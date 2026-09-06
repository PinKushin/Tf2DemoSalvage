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
    /// entirely**, because `sp` appears only multiplied by `sr`. Passing pitch zero below is
    /// therefore lossless and not a simplification — the value cannot reach the result.
    ///
    /// **Nothing the CAMERA does rolls** — the free camera clamps pitch and never rolls, and a
    /// recorded view's roll is not read. Detail props do (B360), which is why the three-argument
    /// form beneath this exists and why this one now delegates to it rather than carrying a second
    /// copy of the arithmetic.
    /// </remarks>
    public static (float X, float Y, float Z) Right(float yaw) => Right(0f, yaw, 0f);

    /// <summary>Which way is right, for a basis that may be rolled.</summary>
    /// <param name="pitch">Pitch in degrees.</param>
    /// <param name="yaw">Yaw in degrees.</param>
    /// <param name="roll">Roll in degrees.</param>
    /// <returns>The basis's right vector.</returns>
    /// <remarks>
    /// **`mathlib_base.cpp:938`, transcribed** — the three lines quoted on the type, with no
    /// rearrangement. `-1*cr*-sy` is Valve's own double negation and is left as it is written.
    ///
    /// **This exists because detail props roll and the camera does not.** `vbsp` builds a
    /// non-upright detail's orientation from the ground's surface normal — an arbitrary
    /// perpendicular basis, then a random spin about it (<c>detailobjects.cpp:568-600</c>) — so
    /// pitch and roll are both filled in for anything standing on a slope. Measured on
    /// `koth_harvest_final`: 27,686 of 28,699 detail props carry both.
    /// </remarks>
    public static (float X, float Y, float Z) Right(float pitch, float yaw, float roll)
    {
        (float sinPitch, float cosPitch) = MathF.SinCos(pitch * Radians);
        (float sinYaw, float cosYaw) = MathF.SinCos(yaw * Radians);
        (float sinRoll, float cosRoll) = MathF.SinCos(roll * Radians);

        return (
            (-1f * sinRoll * sinPitch * cosYaw) + (-1f * cosRoll * -sinYaw),
            (-1f * sinRoll * sinPitch * sinYaw) + (-1f * cosRoll * cosYaw),
            -1f * sinRoll * cosPitch);
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
    /// unlike <see cref="Right(float)"/>, PITCH does not drop out: it is the whole of the first two
    /// components. So this needs both angles where that one needs only yaw, which is the reason the
    /// two have different signatures rather than an oversight.
    ///
    /// The camera does not roll, as recorded on <see cref="Right(float)"/>; detail props do, so this
    /// delegates to the three-argument form rather than keeping a second copy of the arithmetic.
    /// </remarks>
    public static (float X, float Y, float Z) Up(float pitch, float yaw) => Up(pitch, yaw, 0f);

    /// <summary>The UP vector of a basis that may be rolled.</summary>
    /// <param name="pitch">Pitch in degrees.</param>
    /// <param name="yaw">Yaw in degrees.</param>
    /// <param name="roll">Roll in degrees.</param>
    /// <returns>The basis's up vector.</returns>
    /// <remarks>
    /// **`mathlib_base.cpp:944`, transcribed.** See <see cref="Right(float, float, float)"/> for why
    /// a rolled basis is needed at all.
    /// </remarks>
    public static (float X, float Y, float Z) Up(float pitch, float yaw, float roll)
    {
        (float sinPitch, float cosPitch) = MathF.SinCos(pitch * Radians);
        (float sinYaw, float cosYaw) = MathF.SinCos(yaw * Radians);
        (float sinRoll, float cosRoll) = MathF.SinCos(roll * Radians);

        return (
            (cosRoll * sinPitch * cosYaw) + (-sinRoll * -sinYaw),
            (cosRoll * sinPitch * sinYaw) + (-sinRoll * cosYaw),
            cosRoll * cosPitch);
    }

    /// <summary>Which angles a direction describes — Valve's <c>VectorAngles</c>.</summary>
    /// <param name="x">The direction, east-west. Need not be normalised.</param>
    /// <param name="y">The direction, north-south.</param>
    /// <param name="z">The direction, vertically.</param>
    /// <returns>Pitch and yaw in <b>[0, 360)</b>, and a roll of zero.</returns>
    /// <remarks>
    /// **`mathlib_base.cpp:535`**, transcribed including the range:
    ///
    /// <code>
    ///   if (forward[1] == 0 &amp;&amp; forward[0] == 0)
    ///   {
    ///       yaw = 0;
    ///       if (forward[2] &gt; 0) pitch = 270; else pitch = 90;
    ///   }
    ///   else
    ///   {
    ///       yaw = (atan2(forward[1], forward[0]) * 180 / M_PI);
    ///       if (yaw &lt; 0) yaw += 360;
    ///       tmp = sqrt (forward[0]*forward[0] + forward[1]*forward[1]);
    ///       pitch = (atan2(-forward[2], tmp) * 180 / M_PI);
    ///       if (pitch &lt; 0) pitch += 360;
    ///   }
    /// </code>
    ///
    /// **Both results are wrapped into [0, 360) rather than left signed**, so a direction rising at
    /// 45 degrees reports a pitch of 315. Nothing downstream of <see cref="Forward"/> can tell the
    /// two apart — sine and cosine agree — but a clamp or a comparison can, so the wrap is kept
    /// rather than tidied away.
    ///
    /// **The vertical branch has no `atan2` in it**, and it is not an optimisation: a direction with
    /// no horizontal component has no yaw to compute, and the general case would take
    /// <c>atan2(0, 0)</c>. It answers 270 for straight up, which is the same angle as −90 and looks
    /// like a mistake.
    ///
    /// **Pitch is measured from <c>-z</c>**, which is Source's convention throughout: a positive
    /// pitch looks DOWN.
    /// </remarks>
    public static (float Pitch, float Yaw, float Roll) Angles(float x, float y, float z)
    {
#pragma warning disable S1244 // Valve's own exact comparison; a direction is vertical or it is not.
        if (y == 0f && x == 0f)
#pragma warning restore S1244
        {
            return (z > 0f ? 270f : 90f, 0f, 0f);
        }

        float yaw = MathF.Atan2(y, x) / Radians;

        if (yaw < 0f)
        {
            yaw += 360f;
        }

        float flat = MathF.Sqrt((x * x) + (y * y));
        float pitch = MathF.Atan2(-z, flat) / Radians;

        if (pitch < 0f)
        {
            pitch += 360f;
        }

        return (pitch, yaw, 0f);
    }
}
