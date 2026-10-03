using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The two float4s the pixel shader's fog reads, packed as Valve's shader API packs them.
/// </summary>
/// <remarks>
/// Layout cited in FogConformanceTests: colour is <c>g_LinearFogColor</c> (common_ps_fxc.h:45) with w
/// saying whether fog is on; parameters are <c>fogParams</c> as <c>CalcRangeFog</c> unpacks them —
/// x start/(end-start), y the water height (unused, zero), z max density, w 1/(end-start).
/// </remarks>
public sealed class FogConstantsTests
{
    /// <summary>cp_process's controller, as both movement-test recordings carry it.</summary>
    private static readonly SceneFog Process = new(100f, 11000f, 120f / 255f, 133f / 255f, 158f / 255f, 1f);

    [Test]
    public void For_TheProcessController_PacksValvesConstants()
    {
        float[] packed = FogConstants.For(Process);

        packed.Length.ShouldBe(8);

        // Colour: gamma 0..1 from GetFogColor, made linear with Source's 2.2 curve.
        packed[0].ShouldBe(MathF.Pow(120f / 255f, 2.2f), 1e-6f);
        packed[1].ShouldBe(MathF.Pow(133f / 255f, 2.2f), 1e-6f);
        packed[2].ShouldBe(MathF.Pow(158f / 255f, 2.2f), 1e-6f);
        packed[3].ShouldBe(1f);

        packed[4].ShouldBe(100f / 10900f, 1e-9f);
        packed[5].ShouldBe(0f);
        packed[6].ShouldBe(1f);
        packed[7].ShouldBe(1f / 10900f, 1e-12f);
    }

    [Test]
    public void For_NoFog_IsOffAndAllZero()
    {
        FogConstants.For(null).ShouldBe(new float[8]);
    }

    [Test]
    public void For_ASkyboxScaleOfSixteen_DividesTheDistancesBySixteen()
    {
        // Enable3dSkyboxFog: FogStart( start * (1 / scale) ), FogEnd( end * (1 / scale) ). Start
        // over range is unchanged by a common scale; one over the range grows by the scale.
        float[] packed = FogConstants.For(Process, 1f / 16f);

        packed[4].ShouldBe(100f / 10900f, 1e-7f);
        packed[7].ShouldBe(16f / 10900f, 1e-9f);
    }

    [Test]
    public void For_RadialFog_MarksTheColoursWAsTwo()
    {
        // w carries the PIXEL_FOG_TYPE the shader selects: 0 off (none), 1 range, 2 radial — the
        // shader's own numbering is RANGE 0 and RANGE_RADIAL 2 (common_ps_fxc.h:69-71), shifted by
        // one so that zero can mean no fog.
        FogConstants.For(Process)[3].ShouldBe(1f);
        FogConstants.For(Process with { Radial = true })[3].ShouldBe(2f);
    }

    [Test]
    public void For_AMaxDensityBelowOne_IsCarriedUnchanged()
    {
        FogConstants.For(Process with { MaxDensity = 0.6f })[6].ShouldBe(0.6f);
    }
}
