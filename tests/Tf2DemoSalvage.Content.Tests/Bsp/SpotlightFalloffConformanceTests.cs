using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A spotlight's angular falloff, which vrad applies in two separate places.
/// </summary>
/// <remarks>
/// **The same cosine is used twice, for two different purposes, and that is what makes the second
/// one easy to miss.** `lightmap.cpp:1929`-1942, in `GatherSampleLightSSE`:
///
/// <code>
/// out.m_flFalloff = ReciprocalSIMD( out.m_flFalloff );
/// out.m_flFalloff = MulSIMD( out.m_flFalloff, dot2 );          // a plain cosine on the falloff
/// // outside the inner cone
/// inFringe = CmpLeSIMD( dot2, ReplicateX4( dl->light.stopdot ) );
/// mult = ( dot2 - stopdot2 ) / ( stopdot - stopdot2 ), clamped to 0..1
/// </code>
///
/// The first is a cosine term on the falloff itself: a spotlight dims away from its axis everywhere,
/// not only in the penumbra. The second is the penumbra fringe between the inner and outer cones.
/// This project computed `dot2`, used it for the mask and the fringe, and never applied it as the
/// cosine — so a light was at full strength anywhere inside its inner cone (B122).
///
/// **An on-axis test cannot see this**, which is why the existing suite did not: straight down the
/// axis `dot2` is 1 and multiplying by it changes nothing. The condition has to be off-axis and
/// inside the cone, which is the ordinary case for a lamp lighting a room.
///
/// Point lights are deliberately unaffected — `emit_point` carries no such term
/// (<c>lightmap.cpp:1885</c>-1895) — and that difference is asserted here so the fix cannot be
/// applied to both.
/// </remarks>
public sealed class SpotlightFalloffConformanceTests
{
    /// <summary>An unlit cube, so a face's value is only what was added.</summary>
    private static AmbientCube Black => default;

    /// <summary>
    /// Chosen so the arithmetic below stays exact: at ten units the inverse-square falloff is 0.01.
    /// </summary>
    private const float Bright = 100f;

    [Test]
    public void AddTo_ASpotlightOffItsAxis_FallsOffByTheCosineAsWellAsTheFringe()
    {
        // Light eight units up, pointing straight down, over a point six units to the side:
        //
        //   distance    = 10, so the inverse-square falloff is 1/100
        //   dot2        = 0.8   (the cosine between the light's axis and the direction to the point)
        //   fringe      = (0.8 - 0.5) / (0.9 - 0.5) = 0.75
        //   strength    = 0.8   (the +Z face's own cosine, which is a different term again)
        //
        // vrad:  0.8 * 0.01 * 0.8 * 0.75 * 100 = 0.48
        // ours without the cosine: 0.8 * 0.01 * 0.75 * 100 = 0.60
        BspWorldLight spot = new(
            (0f, 0f, 8f), (Bright, Bright, Bright), (0f, 0f, -1f), WorldLightKind.Spotlight,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
            StopDot: 0.9f, StopDot2: 0.5f, Exponent: 1f);

        AmbientCube lit = LocalLights.AddTo(Black, [spot], 6f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(0.48f, 0.001f);
    }

    [Test]
    public void AddTo_ASpotlightOnItsAxis_IsUnchangedByTheCosine()
    {
        // **The control that keeps the case above honest.** On the axis the cosine is one, so this
        // value is the same before and after the fix — which is exactly why an on-axis suite could
        // hold the wrong behaviour for as long as it did.
        BspWorldLight spot = new(
            (0f, 0f, 10f), (Bright, Bright, Bright), (0f, 0f, -1f), WorldLightKind.Spotlight,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
            StopDot: 0.9f, StopDot2: 0.5f, Exponent: 1f);

        LocalLights.AddTo(Black, [spot], 0f, 0f, 0f).PositiveZ.Red.ShouldBe(1f, 0.001f);
    }

    [Test]
    public void AddTo_APointLightOffAxis_KeepsItsFullFalloff()
    {
        // **The second control, and it guards the other direction.** `emit_point` has no cosine term
        // in vrad, so applying one to every light would be as wrong as applying it to none — and a
        // fix written only against the spotlight case above would not notice.
        //
        //   distance 10, falloff 1/100, strength 0.8, no cone: 0.8 * 0.01 * 100 = 0.80
        BspWorldLight lamp = new(
            (0f, 0f, 8f), (Bright, Bright, Bright), (0f, 0f, 0f), WorldLightKind.Point,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f);

        LocalLights.AddTo(Black, [lamp], 6f, 0f, 0f).PositiveZ.Red.ShouldBe(0.8f, 0.001f);
    }
}
