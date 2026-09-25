using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>Which of a map's two lightings the engine reads at TF2's default HDR level.</summary>
/// <remarks>
/// `engine.dll`, renamed in `tf2enginex64.gpr`: `CModelLoader_Map_LoadModel` (0x1800ffc30) loads world lights from
/// `LUMP_WORLDLIGHTS_HDR` (0x36) when the HDR type is not `HDR_TYPE_NONE` and that lump is non-empty, and `Mod_LoadLeafs`
/// (0x180102300) reads the leaf ambient lighting from 0x37 with its index 0x33 under the same test, else 0x38 and 0x34.
/// vrad bakes the HDR set from the lights' `_lightHDR`, which a map may set apart from `_light`.
/// </remarks>
public sealed class HdrLumpChoiceConformanceTests
{
    private const int WorldLights = 15;
    private const int WorldLightsHdr = 54;
    private const int LeafAmbientIndexHdr = 51;
    private const int LeafAmbientIndex = 52;
    private const int LeafAmbientLightingHdr = 55;
    private const int LeafAmbientLighting = 56;

    /// <summary>`dleafambientlighting_t`: a compressed cube, then the sample's position as three fraction bytes.</summary>
    private const int AmbientSampleBytes = 28;

    [Test]
    public void AmbientRead_AMapCarryingBothSets_ReadsTheHdrSamples()
    {
        byte[] map = SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [LeafAmbientLighting] = Sample(x: 10),
            [LeafAmbientIndex] = [1, 0, 0, 0],
            [LeafAmbientLightingHdr] = Sample(x: 200),
            [LeafAmbientIndexHdr] = [1, 0, 0, 0],
        });

        BspAmbientLight.Read(map)[0].Samples[0].X.ShouldBe(200f / 255f);
    }

    [Test]
    public void AmbientRead_AMapWithOnlyLdrSamples_ReadsThem()
    {
        byte[] map = SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [LeafAmbientLighting] = Sample(x: 10),
            [LeafAmbientIndex] = [1, 0, 0, 0],
        });

        BspAmbientLight.Read(map)[0].Samples[0].X.ShouldBe(10f / 255f);
    }

    [Test]
    public void WorldLightsRead_AMapCarryingBothSets_ReadsTheHdrLights()
    {
        byte[] map = SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [WorldLights] = new byte[BspStructLayout.WorldLightStride],
            [WorldLightsHdr] = new byte[BspStructLayout.WorldLightStride * 2],
        });

        BspWorldLights.Read(map).Count.ShouldBe(2);
    }

    [Test]
    public void WorldLightsRead_AMapWithOnlyLdrLights_ReadsThem()
    {
        byte[] map = SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [WorldLights] = new byte[BspStructLayout.WorldLightStride],
        });

        BspWorldLights.Read(map).Count.ShouldBe(1);
    }

    private static byte[] Sample(byte x)
    {
        byte[] sample = new byte[AmbientSampleBytes];

        sample[24] = x;

        return sample;
    }
}
