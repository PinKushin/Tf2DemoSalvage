using System;

namespace Tf2DemoSalvage.Scene;

/// <summary>
/// The grenade launcher's chamber rotation, which the client animates (B348).
/// </summary>
/// <remarks>
/// **The same override family as the minigun's barrel and a different mechanism.** Where
/// `CTFMinigun` integrates a velocity, `CTFGrenadeLauncher::UpdateBarrelMovement`
/// (<c>tf_weapon_grenadelauncher.cpp:639</c>) runs a fixed-length keyframed animation whenever the
/// goal tube differs from the current one — over control points Valve says "match maya":
///
/// <code>
///   Vector( 0,       0,       0 ),
///   Vector( 0.7519f, 63.546f, 0 ),
///   Vector( 1.0f,    60,      0 )
/// </code>
///
/// **X is time as a fraction of `cProceduralBarrelRotationTime`, Y is degrees, Z is the slope at
/// Y** — the file's own comment (<c>:31</c>). Every slope is zero, so the Hermite reduces to its two
/// position terms; the tangent terms are kept anyway because dropping them would agree today and
/// diverge silently the moment a control point changed.
///
/// **The middle point OVERSHOOTS and that is the whole character of the motion.** The chamber swings
/// past 60° to 63.546° at three-quarters of the way through and settles back. A lerp from 0 to 60
/// is smooth, plausible and visibly wrong.
///
/// **Both tube numbers are on the wire** — `m_iCurrentTube` and `m_iGoalTube`
/// (<c>tf_weapon_grenadelauncher.cpp:55</c>) — so the only client-side state is WHEN the rotation
/// began, which `OnDataChanged` stamps the frame the two first differ (<c>:626</c>). Same shape as
/// the burn clock (B336) and the discontinuity stamp (B346).
/// </remarks>
public static class ChamberRotation
{
    /// <summary><c>cProceduralBarrelRotationTime</c> (<c>tf_weapon_grenadelauncher.cpp:44</c>).</summary>
    public const float RotationSeconds = 0.2666f;

    /// <summary><c>TF_TUBE_COUNT</c> (<c>:29</c>).</summary>
    public const int TubeCount = 6;

    /// <summary>Degrees between one tube and the next.</summary>
    /// <remarks>
    /// Not <c>360 / TubeCount</c> computed here: the engine writes the literal `60.0f` (<c>:679</c>)
    /// and the two agreeing is a coincidence worth not depending on.
    /// </remarks>
    public const float DegreesPerTube = 60f;

    /// <summary>Valve's control points: time fraction, degrees, slope.</summary>
    private static readonly (float X, float Y, float Z)[] Points =
    [
        (0f, 0f, 0f),
        (0.7519f, 63.546f, 0f),
        (1f, 60f, 0f),
    ];

    /// <summary>How far through the rotation, as the spline indexes it.</summary>
    /// <param name="elapsedSeconds">Since the rotation began.</param>
    /// <returns>The fraction, which the caller may read past one.</returns>
    /// <remarks>
    /// **Not clamped**, because the engine tests `tVal &lt; 1.0f` to decide whether the animation is
    /// still running at all (<c>:647</c>) — clamping here would erase that distinction and leave the
    /// chamber permanently mid-swing.
    /// </remarks>
    public static float Fraction(double elapsedSeconds) =>
        (float)(elapsedSeconds / RotationSeconds);

    /// <summary>Valve's <c>Hermite_Spline</c>, the scalar four-argument form.</summary>
    /// <param name="first">Value at the start.</param>
    /// <param name="second">Value at the end.</param>
    /// <param name="firstSlope">Tangent at the start.</param>
    /// <param name="secondSlope">Tangent at the end.</param>
    /// <param name="t">Position between them.</param>
    /// <returns>The interpolated value.</returns>
    /// <remarks>
    /// <c>mathlib_base.cpp:2477</c>, transcribed:
    ///
    /// <code>
    ///   float b1 = 2.0f*tCube-3.0f*tSqr+1.0f;
    ///   float b2 = 1.0f - b1;
    ///   float b3 = tCube-2*tSqr+t;
    ///   float b4 = tCube-tSqr;
    ///   output = p1*b1 + p2*b2 + d1*b3 + d2*b4;
    /// </code>
    ///
    /// **`b2` is written as `1 - b1` rather than `-2t³+3t²`**, which Valve notes are equal. Kept in
    /// the engine's form so the two can be compared line by line.
    /// </remarks>
    public static float Spline(float first, float second, float firstSlope, float secondSlope, float t)
    {
        float squared = t * t;
        float cubed = t * squared;

        float b1 = (2f * cubed) - (3f * squared) + 1f;
        float b2 = 1f - b1;
        float b3 = cubed - (2f * squared) + t;
        float b4 = cubed - squared;

        return (first * b1) + (second * b2) + (firstSlope * b3) + (secondSlope * b4);
    }

    /// <summary>The partial rotation at a point in the animation, in degrees.</summary>
    /// <param name="fraction">How far through, from <see cref="Fraction"/>.</param>
    /// <returns>Degrees past the current tube, or zero once the animation is over.</returns>
    /// <remarks>
    /// **Zero past the end, and that is not a clamp to 60.** The engine leaves
    /// `flPartialRotationDeg` at its initialised zero and advances `m_iCurrentTube` instead
    /// (<c>tf_weapon_grenadelauncher.cpp:687</c>) — the sixty degrees arrive through the base angle
    /// of the tube it has now reached. Returning 60 here would double-count them for one frame.
    ///
    /// **The span is found by walking forward to the first point past `fraction`**, which is Valve's
    /// loop rather than an index computed from the fraction: the points are not evenly spaced and
    /// the file asserts only that they increase.
    /// </remarks>
    public static float Degrees(float fraction)
    {
        if (fraction >= 1f)
        {
            return 0f;
        }

        for (int index = 1; index < Points.Length; index++)
        {
            if (fraction > Points[index].X)
            {
                continue;
            }

            (float X, float Y, float Z) first = Points[index - 1];
            (float X, float Y, float Z) second = Points[index];

            float within = (fraction - first.X) / (second.X - first.X);

            return Spline(first.Y, second.Y, first.Z, second.Z, within);
        }

        return 0f;
    }

    /// <summary>The chamber's angle in radians.</summary>
    /// <param name="tube">The tube it has reached — <c>m_iCurrentTube</c>.</param>
    /// <param name="partialDegrees">The partial rotation, from <see cref="Degrees"/>.</param>
    /// <returns>The angle, in radians.</returns>
    /// <remarks>
    /// `const float flBaseDeg = 60.0f * m_iCurrentTube; m_flBarrelAngle = DEG2RAD( flBaseDeg +
    /// flPartialRotationDeg );` (<c>tf_weapon_grenadelauncher.cpp:679</c>). Radians, because that is
    /// what reaches the bone.
    /// </remarks>
    public static float Angle(int tube, float partialDegrees) =>
        ((DegreesPerTube * tube) + partialDegrees) * (MathF.PI / 180f);
}
