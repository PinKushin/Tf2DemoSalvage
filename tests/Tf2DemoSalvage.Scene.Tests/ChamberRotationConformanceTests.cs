using System;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The grenade launcher's chamber turns on a keyframed spline, not a constant rate (B348).
/// </summary>
/// <remarks>
/// **A different mechanism from the minigun's, in the same override family.**
/// `CTFGrenadeLauncher::UpdateBarrelMovement` (<c>tf_weapon_grenadelauncher.cpp:639</c>) does not
/// integrate a velocity — it runs a fixed-length animation whenever the goal tube differs from the
/// current one, over control points Valve says "match maya" (<c>:35</c>):
///
/// <code>
///   Vector( 0,       0,      0 ),
///   Vector( 0.7519f, 63.546f,0 ),
///   Vector( 1.0f,    60,     0 )
/// </code>
///
/// **X is time as a fraction of `cProceduralBarrelRotationTime` (0.2666s), Y is degrees, Z is the
/// slope at Y** — the file's own comment. Every slope is zero here, so the Hermite reduces to its
/// two position terms.
///
/// **The middle point is an OVERSHOOT and it is the whole character of the motion**: the chamber
/// swings past 60° to 63.546° at three-quarters of the way through, then settles back. A
/// implementation that lerped from 0 to 60 would be smooth, plausible, and visibly wrong.
/// </remarks>
public sealed class ChamberRotationConformanceTests
{
    /// <remarks>
    /// **`Hermite_Spline(p1, p2, d1, d2, t)`** (<c>mathlib_base.cpp:2477</c>), whose basis is
    /// <c>b1 = 2t³-3t²+1</c>, <c>b2 = 1-b1</c>, <c>b3 = t³-2t²+t</c>, <c>b4 = t³-t²</c>. With both
    /// tangents zero the endpoints are exact and the middle is the smoothstep of the two.
    /// </remarks>
    [Test]
    public void Spline_AtItsEndpoints_ReturnsThemExactly()
    {
        ChamberRotation.Spline(0f, 60f, 0f, 0f, 0f).ShouldBe(0f, 1e-5f);
        ChamberRotation.Spline(0f, 60f, 0f, 0f, 1f).ShouldBe(60f, 1e-5f);
    }

    /// <remarks>
    /// **The tangent terms are not decoration.** All three of Valve's points carry a slope of zero,
    /// so an implementation dropping `d1`/`d2` would agree here — and would diverge the moment
    /// anyone edited a control point, silently. Asserted with non-zero tangents so the terms are
    /// exercised: at the midpoint <c>b3</c> and <c>b4</c> are 0.125 and -0.125.
    /// </remarks>
    [Test]
    public void Spline_WithNonZeroTangents_UsesBothOfThem()
    {
        ChamberRotation.Spline(0f, 0f, 8f, 0f, 0.5f).ShouldBe(1f, 1e-5f);
        ChamberRotation.Spline(0f, 0f, 0f, 8f, 0.5f).ShouldBe(-1f, 1e-5f);
    }

    /// <remarks>
    /// **This samples a KNOT, and a knot cannot tell one curve from another.** 0.7519 is exactly
    /// the middle control point's own X, so `within` is 1.0 and every interpolation scheme is
    /// forced to return the stored Y. It pins the TABLE — that the overshoot value is 63.546 and
    /// that the point is where Valve put it — and nothing about the curve's shape.
    ///
    /// **Written first as the overshoot test, and it could not fail**: replacing the whole Hermite
    /// with a plain lerp left it green. That is the WRONG CONDITION case from `CLAUDE.md` — an input
    /// for which correct and broken predict the same observation. The shape is asserted by
    /// <see cref="Degrees_StrictlyBetweenControlPoints_FollowsTheSplineAndNotALerp"/>, which is the
    /// test that actually kills a lerp.
    /// </remarks>
    [Test]
    public void Degrees_AtTheMiddleControlPoint_ReturnsTheTablesOvershootValue()
    {
        ChamberRotation.Degrees(0.7519f).ShouldBe(63.546f, 1e-3f);

        ChamberRotation.Degrees(0.7519f).ShouldBeGreaterThan(
            60f, "the motion swings past its destination and settles back");
    }

    /// <remarks>
    /// **The assertion that distinguishes the spline from a lerp**, and the one the suite was
    /// missing. Sampled STRICTLY inside the first segment, where the two genuinely differ.
    ///
    /// At `fraction = 0.4` the position within the segment is `0.4 / 0.7519 = 0.5319856`, so with
    /// both tangents zero Valve's basis gives `b2 = 1 - (2t³ - 3t² + 1) = 0.5479144` and the value
    /// is `63.546 × 0.5479144 = 34.818`. A lerp would give `63.546 × 0.5319856 = 33.806` — **a
    /// difference of one degree**, which is why the tolerance here is a hundredth rather than the
    /// hundredth-of-a-degree the boundary test uses.
    ///
    /// **The old boundary test was no substitute**, and that is the second failure mode rather than
    /// the same one: at 0.9999 the spline and a lerp differ by about 0.0014°, an order of magnitude
    /// under that test's 1e-2 tolerance. Effect size below resolution.
    /// </remarks>
    [Test]
    public void Degrees_StrictlyBetweenControlPoints_FollowsTheSplineAndNotALerp()
    {
        float mid = ChamberRotation.Degrees(0.4f);

        mid.ShouldBe(34.818f, 1e-2f, "Valve's Hermite basis with both tangents at zero");

        // The control, stated as the value a lerp WOULD give, so the margin is visible rather than
        // implied by a tolerance nobody checks.
        mid.ShouldBeGreaterThan(
            34.3f, "a plain lerp across the same segment gives 33.806, a full degree lower");
    }

    /// <remarks>
    /// **The boundary is `tVal &lt; 1.0f`, strictly** (<c>tf_weapon_grenadelauncher.cpp:647</c>), so
    /// at exactly one the animation is already over: the tube advances and the partial is zero.
    /// The spline's own last control point IS 60 — asserted just below one, where the engine still
    /// evaluates it — and this test exists because the first version asserted 60 AT one and
    /// reddened against correct code. The boundary was mine to get wrong, not Valve's.
    /// </remarks>
    [Test]
    public void Degrees_AtTheBoundary_StopsBeingPartialExactlyAtOne()
    {
        ChamberRotation.Degrees(0f).ShouldBe(0f, 1e-4f);
        ChamberRotation.Degrees(0.9999f).ShouldBe(60f, 1e-2f);
        ChamberRotation.Degrees(1f).ShouldBe(0f);
    }

    /// <remarks>
    /// **Past the end the animation is over**, which the engine expresses by leaving
    /// `flPartialRotationDeg` at its initialised zero and advancing `m_iCurrentTube` instead
    /// (<c>tf_weapon_grenadelauncher.cpp:687</c>). The partial contribution is zero, not 60 — the
    /// sixty arrives through the base angle of the tube it has now reached.
    /// </remarks>
    [Test]
    public void Degrees_PastTheEnd_IsZeroBecauseTheTubeHasAdvanced()
    {
        ChamberRotation.Degrees(1.5f).ShouldBe(0f);
    }

    /// <remarks>
    /// **Six tubes, sixty degrees each** — `TF_TUBE_COUNT` is 6 (<c>:29</c>) and the base angle is
    /// `60.0f * m_iCurrentTube` (<c>:679</c>). In RADIANS, because the engine wraps the whole sum
    /// in `DEG2RAD` before it reaches the bone.
    /// </remarks>
    [Test]
    public void Angle_ForEachTube_IsSixtyDegreesApart()
    {
        ChamberRotation.Angle(tube: 0, partialDegrees: 0f).ShouldBe(0f, 1e-5f);

        ChamberRotation.Angle(tube: 1, partialDegrees: 0f)
            .ShouldBe(60f * MathF.PI / 180f, 1e-5f);

        ChamberRotation.Angle(tube: 5, partialDegrees: 0f)
            .ShouldBe(300f * MathF.PI / 180f, 1e-5f);
    }

    /// <remarks>
    /// The partial rotation adds to the base, so a chamber halfway between tubes 2 and 3 sits past
    /// 120 degrees rather than restarting.
    /// </remarks>
    [Test]
    public void Angle_MidRotation_AddsThePartialToTheTubesBase()
    {
        ChamberRotation.Angle(tube: 2, partialDegrees: 30f)
            .ShouldBe(150f * MathF.PI / 180f, 1e-5f);
    }

    /// <remarks>
    /// **0.2666 seconds, and the fraction is what the spline is indexed by** (<c>:44</c>). Named
    /// rather than inlined because a rotation that takes a different length of time is the one
    /// visible way this can be wrong while every other assertion here still passes.
    /// </remarks>
    [Test]
    public void Fraction_IsTheElapsedShareOfTheRotationTime()
    {
        ChamberRotation.Fraction(0d).ShouldBe(0f, 1e-5f);
        ChamberRotation.Fraction(0.2666d).ShouldBe(1f, 1e-4f);
        ChamberRotation.Fraction(0.1333d).ShouldBe(0.5f, 1e-3f);
    }
}
