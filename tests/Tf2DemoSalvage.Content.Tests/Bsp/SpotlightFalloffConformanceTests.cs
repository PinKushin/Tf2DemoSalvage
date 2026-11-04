using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A spotlight's angular falloff in the runtime light cache: the fringe, and no cosine of its own (B669, reversing B122 for
/// the cube).
/// </summary>
/// <remarks>
/// **`engine.dll` `FUN_1800efbf0` case 2, `Engine_WorldLightAngle`'s spot case, is all the angle a folded spot takes**: with the
/// face's dot apart (`FUN_1801b5db0`) and the direction passed for the surface normal, `dot` is 1, and the result is 1 inside
/// `stopdot`, 0 outside `stopdot2`, and the fringe between, raised to the exponent unless that is 0 or 1. `FUN_1801b9570`'s
/// return is the distance falloff with no angle in it. **Measured**, logged from the running engine at `FUN_1801b5db0` entry
/// on `cp_process_f12` (`measurements/2026-11-03-b669-pipe-traces-live.md`).
///
/// **B122 took its cosine from vrad.** `lightmap.cpp:1929` does multiply the falloff by `dot2`, and that is the bake; the
/// runtime cube does not. So every spotlight folded into a cube read short by that cosine, and the pipes behind the pane
/// (B669) were short by about a third for it.
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
    public void AddTo_ASpotlightOffItsAxis_TakesTheFringeAndNoCosineOfItsOwn()
    {
        // Light eight units up, pointing straight down, over a point six units to the side:
        //
        //   distance    = 10, so the inverse-square falloff is 1/100
        //   dot2        = 0.8   (read only for the fringe)
        //   fringe      = (0.8 - 0.5) / (0.9 - 0.5) = 0.75
        //   strength    = 0.8   (the +Z face's own cosine, FUN_1801b5db0)
        //
        // engine: 0.8 * 0.01 * 0.75 * 100 = 0.60
        // vrad's bake, which B122 copied: 0.48
        BspWorldLight spot = new(
            (0f, 0f, 8f), (Bright, Bright, Bright), (0f, 0f, -1f), WorldLightKind.Spotlight,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
            StopDot: 0.9f, StopDot2: 0.5f, Exponent: 1f);

        AmbientCube lit = LocalLights.AddTo(Black, [spot], 6f, 0f, 0f);

        lit.PositiveZ.Red.ShouldBe(0.60f, 0.001f);
    }

    [Test]
    public void Fold_ASpotInsideItsInnerConeOffItsAxis_IsAtFullStrength()
    {
        // `fVar1 <= fVar6` returns the dot alone. Six over eight up again, with the inner cone wide enough to hold it:
        // 0.8 * 0.01 * 100 = 0.80, the value an unconed lamp gives (AddTo_APointLightOffAxis_KeepsItsFullFalloff).
        BspWorldLight spot = new(
            (0f, 0f, 8f), (Bright, Bright, Bright), (0f, 0f, -1f), WorldLightKind.Spotlight,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
            StopDot: 0.7f, StopDot2: 0.5f, Exponent: 1f);

        LocalLights.Fold(Black, spot, 6f, 0f, 0f).PositiveZ.Red.ShouldBe(0.80f, 0.001f);
    }

    [Test]
    public void Fold_TheLoggedSpotAtPlacement215_ReproducesTheEnginesAmount()
    {
        // `cp_process_f12`'s spot at (-3400 -1128 486.028), lit at (-3459 -683 506). The engine, at `FUN_1801b5db0`: XMM3
        // 1.142209585e-04, direction (0.13129, -0.99035, -0.04445), green 128.62177. So its -y face is
        // 1.142209585e-04 * 0.99035 * 128.62177 = 0.014549. With B122's cosine it would be 0.000647.
        BspWorldLight spot = new(
            (-3400f, -1128f, 486.028f), (108.87539f, 128.62177f, 119.10582f), (0f, 0f, 1f), WorldLightKind.Spotlight,
            ConstantAttenuation: 0f, LinearAttenuation: 1f, QuadraticAttenuation: 0f,
            StopDot: 0.8660254f, StopDot2: 0f, Exponent: 1f);

        LocalLights.Fold(Black, spot, -3459f, -683f, 506f).NegativeY.Green.ShouldBe(0.014549f, 0.00002f);
    }

    [Test]
    public void AddTo_ASpotlightOnItsAxis_IsUnchangedByTheCosine()
    {
        // The on-axis control: dot2 is one, so this value holds under either reading.
        BspWorldLight spot = new(
            (0f, 0f, 10f), (Bright, Bright, Bright), (0f, 0f, -1f), WorldLightKind.Spotlight,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f,
            StopDot: 0.9f, StopDot2: 0.5f, Exponent: 1f);

        LocalLights.AddTo(Black, [spot], 0f, 0f, 0f).PositiveZ.Red.ShouldBe(1f, 0.001f);
    }

    [Test]
    public void AddTo_APointLightOffAxis_KeepsItsFullFalloff()
    {
        //   distance 10, falloff 1/100, strength 0.8, no cone: 0.8 * 0.01 * 100 = 0.80
        BspWorldLight lamp = new(
            (0f, 0f, 8f), (Bright, Bright, Bright), (0f, 0f, 0f), WorldLightKind.Point,
            ConstantAttenuation: 0f, LinearAttenuation: 0f, QuadraticAttenuation: 1f);

        LocalLights.AddTo(Black, [lamp], 6f, 0f, 0f).PositiveZ.Red.ShouldBe(0.8f, 0.001f);
    }
}
