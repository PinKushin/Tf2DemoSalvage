using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// Evaluating a world light the way <c>LightDesc_t::ComputeLightAtPoints</c> evaluates it.
/// </summary>
/// <remarks>
/// Expected values are computed by hand from <c>mathlib/lightdesc.cpp</c>, not captured from a run
/// of the code under test — a captured expectation records what the code does, which is the one
/// thing a parity test must never assert.
///
/// Every point light on cp_process_f12 is pure inverse-square (constant 0, linear 0, quadratic 1),
/// so that is the shape most of these use.
/// </remarks>
public sealed class LocalLightsTests
{
    /// <summary>An unlit cube, so a face's value is the light this class added and nothing else.</summary>
    private static AmbientCube Black => default;

    /// <summary>
    /// A light intensity in the units a map stores, chosen so the expected values stay whole.
    /// </summary>
    /// <remarks>
    /// **These carried a factor of 255 that the lump does not, and every expected value below was
    /// written to match it.** The old note here said a world light's intensity is 0–255 against a
    /// cube normalised to 0–1, so the two were "255 apart". vrad divides intensity by 255 on the way
    /// into the file (<c>lightmap.cpp:1647</c>), so the lump is 0–1 already — the two are in one
    /// scale and `IntensityScale` is 1. See `WorldLightScaleConformanceTests`, which asserts against
    /// vrad's arithmetic rather than against a number chosen here.
    ///
    /// **The values moved and the predictions did not**, deliberately. Scaling the inputs down by
    /// 255 puts them in the units a map really stores while leaving every expected value below
    /// exactly as it was, so this file still says the same things about falloff, cones, ranking and
    /// culling — and none of those claims ever depended on the scale.
    ///
    /// The old note also stated the reason it could not settle the question itself: "a test that
    /// invents its own intensity cannot notice, because it has no opinion about what a map uses".
    /// That remains true of everything here, which is why the scale is pinned elsewhere against a
    /// real map's authored `_light` keys.
    ///
    /// 100 rather than 1 so that the inverse-square falloff at ten units (0.01) brings it back to
    /// exactly 1.0, which keeps every expected value below readable.
    /// </remarks>
    private const float Bright = 100f;

    /// <summary>An inverse-square point light, which is what a map actually ships.</summary>
    private static BspWorldLight Point(
        (float X, float Y, float Z) origin, float intensity, float radius = 0f) =>
        new(origin, (intensity, intensity, intensity), (0f, 0f, 0f), WorldLightKind.Point,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
            Radius: radius);

    [Test]
    public void APointLightAbove_LightsTheUpwardFace()
    {
        // Light 10 units straight up from the origin.
        //   dist2 = 100, falloff = 1 / (1 * 100) = 0.01
        //   delta normalised = (0,0,1); +Z face normal is (0,0,1) so strength = 1
        //   contribution = 1 * 0.01 * 100 = 1.0
        AmbientCube lit = LocalLights.AddTo(
            Black, [Point((0f, 0f, 10f), Bright)], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(1f, 0.001f);
    }

    [Test]
    public void APointLightAbove_LeavesTheDownwardFaceDark()
    {
        // The control for the test above, and the one that catches a missing dot product: with
        // strength = max(0, delta . normal), the -Z face faces away and receives nothing. An
        // implementation that added falloff * colour to every face would pass the first test and
        // fail this one.
        AmbientCube lit = LocalLights.AddTo(
            Black, [Point((0f, 0f, 10f), Bright)], 0f, 0f, 0f);

        lit.NegativeZ.Red.ShouldBe(0f, 0.001f);
    }

    [Test]
    public void ExistingAmbientIsKept_NotReplaced()
    {
        // The cube is the bounce term and the lights are the direct one; they add. Replacing would
        // make a lit room darker than an unlit one wherever no light is in range.
        AmbientCube ambient = new(
            (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f),
            (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f), (0.2f, 0.2f, 0.2f));

        AmbientCube lit = LocalLights.AddTo(
            ambient, [Point((0f, 0f, 10f), Bright)], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(1.2f, 0.001f);
        lit.NegativeZ.Red.ShouldBe(0.2f, 0.001f);
    }

    [Test]
    public void DistanceIsClampedToOne_NotOffsetByOne()
    {
        // **The detail most easily conflated with the ambient blend**, which uses 1 / (dist + 1).
        // Valve clamps instead: MaxSIMD( Four_Ones, dist2 ). A light exactly one unit away has
        // dist2 = 1 either way, so the distinguishing case is a light CLOSER than one unit.
        //
        //   at 0.5 units: true dist2 = 0.25, clamped to 1, falloff = 1/1 = 1
        //   contribution = 1 * 1 * 3 = 3.0
        //
        // The offset form would give 1 / (0.25 + 1) = 0.8 and a contribution of 2.4.
        AmbientCube lit = LocalLights.AddTo(
            Black, [Point((0f, 0f, 0.5f), 3f)], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(3f, 0.001f);
    }

    [Test]
    public void LocalLights_ARadius_CullsBeyondIt()
    {
        // dist2 = 100 against range^2 = 25, so the light is culled entirely.
        AmbientCube lit = LocalLights.AddTo(
            Black, [Point((0f, 0f, 10f), Bright, radius: 5f)], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(0f, 0.001f);

        // The control: the same light with a radius that reaches still lights the face. Without
        // this, an implementation that culled everything would pass.
        AmbientCube reached = LocalLights.AddTo(
            Black, [Point((0f, 0f, 10f), Bright, radius: 20f)], 0f, 0f, 0f);

        reached.PositiveZ.Red.ShouldBe(1f, 0.001f);
    }

    [Test]
    public void AZeroRadius_MeansNoCull()
    {
        // Zero is "no cutoff", not "cut off at zero distance" - `if (m_Range != 0.f)`. Reading it
        // as a real radius extinguishes every light on the map, since cp_process stores zero for
        // all of them.
        AmbientCube lit = LocalLights.AddTo(
            Black, [Point((0f, 0f, 10f), Bright, radius: 0f)], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBeGreaterThan(0f);
    }

    [Test]
    public void ASpotlightInsideItsInnerCone_IsAtFullStrength()
    {
        // Pointing straight down from 10 units up, inner cone cos 0.9, outer cos 0.5. The point
        // below is on the axis, so dot2 = 1: the cone scale is (1 - 0.5) / (0.9 - 0.5) = 1.25,
        // clamped to 1. Contribution is then the same 1.0 as a bare point light.
        BspWorldLight spot = new(
            (0f, 0f, 10f), (Bright, Bright, Bright), (0f, 0f, -1f), WorldLightKind.Spotlight,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
            StopDot: 0.9f, StopDot2: 0.5f, Exponent: 1f);

        AmbientCube lit = LocalLights.AddTo(Black, [spot], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(1f, 0.001f);
    }

    [Test]
    public void ASpotlightPointingAway_ContributesNothing()
    {
        // Same light turned to point straight up, away from the surface below it. dot2 = -1, which
        // is below phiDot, so the cone masks it out entirely. Without that mask the cone scale goes
        // negative and a negative strength SUBTRACTS light.
        BspWorldLight spot = new(
            (0f, 0f, 10f), (Bright, Bright, Bright), (0f, 0f, 1f), WorldLightKind.Spotlight,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
            StopDot: 0.9f, StopDot2: 0.5f, Exponent: 1f);

        AmbientCube lit = LocalLights.AddTo(Black, [spot], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(0f, 0.001f);
    }

    [Test]
    public void LocalLights_OnlyTheFourStrongest_Apply()
    {
        // The engine carries four: LightDesc_t m_LocalLightDescs[4]. Five identical lights at the
        // same distance must contribute as four, not five.
        List<BspWorldLight> five =
        [
            Point((0f, 0f, 10f), Bright), Point((0f, 0f, 10f), Bright), Point((0f, 0f, 10f), Bright),
            Point((0f, 0f, 10f), Bright), Point((0f, 0f, 10f), Bright),
        ];

        AmbientCube lit = LocalLights.AddTo(Black, five, 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(4f, 0.001f);
    }

    [Test]
    public void TheStrongestAreChosen_NotTheFirstFour()
    {
        // Four dim lights listed before one bright one. Taking the first four in file order would
        // drop the only light that matters, and a map lists its lights in no useful order.
        List<BspWorldLight> lights =
        [
            Point((0f, 0f, 10f), 1f), Point((0f, 0f, 10f), 1f),
            Point((0f, 0f, 10f), 1f), Point((0f, 0f, 10f), 1f),
            Point((0f, 0f, 10f), Bright),
        ];

        AmbientCube lit = LocalLights.AddTo(Black, lights, 0f, 0f, 0f);

        // The bright one (100 * 0.01 = 1.0) plus three dim ones (1 * 0.01 = 0.01).
        lit.PositiveZ.Red.ShouldBe(1.03f, 0.001f);
    }

    [Test]
    public void ALightWithNoAttenuation_IsConstantOne_NotInfinite()
    {
        // **Found in the viewer's own log, not by any of the tests above**, which is the point of
        // it existing. Four capture points reported a luminance of ∞ while all eleven unit tests
        // passed, because every one of them used a light with quadratic attenuation and cp_process
        // ships lights with none.
        //
        // The first transcription took `else falloff = Four_Epsilons` from ComputeLightAtPoints
        // literally, as float.Epsilon — the smallest denormal — so 1/falloff overflowed. That
        // branch guards a reciprocal; it does not describe the light. vrad states what such a
        // light IS: constant_attn = 1 when all three terms are below EQUAL_EPSILON.
        //
        //   falloff = 1 / 1 = 1, strength = 1, contribution = 1 * 1 * 2 = 2.0
        BspWorldLight bare = new(
            (0f, 0f, 10f), (2f, 2f, 2f), (0f, 0f, 0f), WorldLightKind.Point);

        AmbientCube lit = LocalLights.AddTo(Black, [bare], 0f, 0f, 0f);

        float.IsFinite(lit.PositiveZ.Red).ShouldBeTrue();
        lit.PositiveZ.Red.ShouldBe(2f, 0.001f);
    }

    [Test]
    public void TheSun_IsNotALocalLight()
    {
        // emit_skylight is directional and reaches only what can see the sky, which needs a trace
        // this project does not do. It is applied elsewhere; treating it as a point light here
        // would place the sun 10 units above whatever is being lit.
        BspWorldLight sun = new(
            (0f, 0f, 10f), (Bright, Bright, Bright), (0f, 0f, -1f), WorldLightKind.SkyLight,
            QuadraticAttenuation: 1f);

        AmbientCube lit = LocalLights.AddTo(Black, [sun], 0f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(0f, 0.001f);
    }
}
