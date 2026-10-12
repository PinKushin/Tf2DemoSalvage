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

    /// <remarks>B424: `FUN_1801b6bf0` reads `dworldlight_t.cluster` at `+0x24` and the style at `+0x2c`.</remarks>
    [Test]
    public void WorldLightsRead_ALightsClusterAndStyle_AreItsOwnFields()
    {
        byte[] light = new byte[BspStructLayout.WorldLightStride];
        light[0x24] = 7;
        light[0x2c] = 5;

        BspWorldLight read = BspWorldLights.Read(SyntheticBsp.Build(new Dictionary<int, byte[]> { [WorldLights] = light }))[0];

        read.Cluster.ShouldBe(7);
        read.Style.ShouldBe(5);
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

    /// <remarks>
    /// **`mat_hdr_level` below 2 is `HDR_TYPE_NONE`, and every one of these readers asks that first** (B537).
    /// `Mod_LoadLeafs` (`engine.dll` 0x180102300), read again for the other branch: the hardware config's HDR type
    /// (`*DAT_1807a8c70 + 0x1b0`) is tested BEFORE the lump's size, and zero goes straight to lumps 0x38 and 0x34. A map
    /// carrying both sets is every shipped map, so with HDR off the HDR set is there and is not read.
    /// </remarks>
    [Test]
    public void AmbientRead_AMapCarryingBothSetsWithHdrOff_ReadsTheLdrSamples()
    {
        byte[] map = SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [LeafAmbientLighting] = Sample(x: 10),
            [LeafAmbientIndex] = [1, 0, 0, 0],
            [LeafAmbientLightingHdr] = Sample(x: 200),
            [LeafAmbientIndexHdr] = [1, 0, 0, 0],
        });

        BspAmbientLight.Read(map, hdrEnabled: false)[0].Samples[0].X.ShouldBe(10f / 255f);
    }

    [Test]
    public void WorldLightsRead_AMapCarryingBothSetsWithHdrOff_ReadsTheLdrLights()
    {
        byte[] map = SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [WorldLights] = new byte[BspStructLayout.WorldLightStride],
            [WorldLightsHdr] = new byte[BspStructLayout.WorldLightStride * 2],
        });

        BspWorldLights.Read(map, hdrEnabled: false).Count.ShouldBe(1);
    }

    /// <remarks>The faces and the lighting they index are a pair: `LUMP_FACES` (7) with `LUMP_LIGHTING` (8) when HDR is off.</remarks>
    [Test]
    public void LightSamples_AMapCarryingBothFaceLumps_AreTheLdrFacesWithHdrOffAndTheHdrOnesWithItOn()
    {
        const int faces = 7, lighting = 8, lightingHdr = 53, facesHdr = 58, faceBytes = 56;

        byte[] map = SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [faces] = new byte[faceBytes],
            [lighting] = new byte[4],
            [facesHdr] = new byte[faceBytes * 2],
            [lightingHdr] = new byte[8],
        });

        BspLightmaps.ReadSamples(map, hdr: false).Layout(1).ShouldBeNull("one LDR face");
        BspLightmaps.ReadSamples(map, hdr: true).Layout(1).ShouldNotBeNull("two HDR faces");
    }

    /// <remarks>`engine.dll` 0x1800f4760: `sp_hdr_%d.vhv` only when HDR is ON and the map's HDR bake bit is set.</remarks>
    [Test]
    public void StaticPropLightingPath_AnHdrBakedMapWithHdrOff_IsTheLdrFile()
    {
        const uint both = BspMapFlags.BakedStaticPropLightingNonHdr | BspMapFlags.BakedStaticPropLightingHdr;

        Tf2DemoSalvage.Content.Assets.StudioVertexLighting.PathsFor(7, both, hdr: false).ShouldBe(["sp_7.vhv"]);
        Tf2DemoSalvage.Content.Assets.StudioVertexLighting.PathsFor(7, both, hdr: true).ShouldBe(["sp_hdr_7.vhv"]);
    }

    private static byte[] Sample(byte x)
    {
        byte[] sample = new byte[AmbientSampleBytes];

        sample[24] = x;

        return sample;
    }
}
