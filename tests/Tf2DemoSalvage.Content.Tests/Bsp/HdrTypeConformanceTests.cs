using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>Which HDR type TF2 runs, as <c>shaderapidx9.dll</c> and <c>engine.dll</c> decide it (B62).</summary>
/// <remarks>
/// Read in disassembly (x64 TF2, Ghidra projects under <c>D:</c>); <c>HardwareCaps_t</c> sits at <c>[0x18009b008]</c>, with
/// <c>m_HDRType</c> at <c>+0x560</c> and <c>m_MaxHDRType</c> at <c>+0x5c4</c> (the caps dump <c>0x18002bb50</c> prints
/// <c>+0x560</c> through the strings "m_HDRType: HDR_TYPE_NONE/INTEGER/FLOAT" at <c>0x18007f3f8</c>/<c>f418</c>/<c>f438</c>).
/// <list type="bullet">
/// <item><c>0x1800293eb-0x180029448</c>, the caps computation: with float support, <c>m_MaxHDRType = 2</c> and
/// <c>m_HDRType = 2</c> only when <c>mat_hdr_level</c> (<c>[0x1800a6788]+0x58</c>) is 3, else 1 with integer support, else
/// 0; without float support both are 1 with integer support, else 0. <c>mat_hdr_level</c> defaults to "2"
/// (<c>0x18007e700</c>). No "-floathdr" string is in the binary.</item>
/// <item><c>0x180004810</c>, <c>GetHDRType</c>: <c>m_HDRType</c> when <c>m_bHDREnabled</c> (<c>+0x8a8</c>) and the DX level
/// is at least 90 (<c>0x5a</c>), else <c>HDR_TYPE_NONE</c>.</item>
/// <item><c>engine.dll 0x1800ffa10</c>, <c>Map_CheckForHDR</c> (named by the string at <c>0x180392508</c>): the map has
/// HDR when <c>LUMP_LIGHTING_HDR</c> (53) and <c>LUMP_WORLDLIGHTS_HDR</c> (54) are non-empty and, from BSP version 20,
/// <c>LUMP_LEAF_AMBIENT_LIGHTING_HDR</c> (55) too; HDR is enabled (vtable <c>+0x230</c>) only then, with
/// <c>mat_hdr_level</c> at least 2 and the DX level at least 90.</item>
/// </list>
/// </remarks>
public sealed class HdrTypeConformanceTests
{
    private const int LightingHdr = 53;
    private const int WorldLightsHdr = 54;
    private const int LeafAmbientLightingHdr = 55;

    [Test]
    public void CapsHdrType_FloatCapableAtTheDefaultLevel_IsInteger() =>
        BspHdr.CapsHdrType(supportsInteger: true, supportsFloat: true, matHdrLevel: 2).ShouldBe(HdrType.IntegerHdr);

    [Test]
    public void CapsHdrType_FloatCapableAtLevel3_IsFloat() =>
        BspHdr.CapsHdrType(supportsInteger: true, supportsFloat: true, matHdrLevel: 3).ShouldBe(HdrType.FloatHdr);

    [Test]
    public void CapsHdrType_IntegerOnlyAtLevel3_IsInteger() =>
        BspHdr.CapsHdrType(supportsInteger: true, supportsFloat: false, matHdrLevel: 3).ShouldBe(HdrType.IntegerHdr);

    [Test]
    public void CapsHdrType_NeitherCapability_IsNone() =>
        BspHdr.CapsHdrType(supportsInteger: false, supportsFloat: false, matHdrLevel: 2).ShouldBe(HdrType.None);

    [Test]
    public void HdrTypeInUse_HdrDisabled_IsNone() =>
        BspHdr.HdrTypeInUse(hdrEnabled: false, dxLevel: 95, HdrType.IntegerHdr).ShouldBe(HdrType.None);

    [Test]
    public void HdrTypeInUse_BelowDx90_IsNone() =>
        BspHdr.HdrTypeInUse(hdrEnabled: true, dxLevel: 81, HdrType.IntegerHdr).ShouldBe(HdrType.None);

    [Test]
    public void HdrTypeInUse_EnabledAtDx90_IsTheCapsType() =>
        BspHdr.HdrTypeInUse(hdrEnabled: true, dxLevel: 90, HdrType.IntegerHdr).ShouldBe(HdrType.IntegerHdr);

    [Test]
    public void MapHasHdr_AllThreeHdrLumps_IsTrue() =>
        BspHdr.MapHasHdr(Map(LightingHdr, WorldLightsHdr, LeafAmbientLightingHdr)).ShouldBeTrue();

    [Test]
    public void MapHasHdr_NoLeafAmbientHdrAtVersion21_IsFalse() =>
        BspHdr.MapHasHdr(Map(LightingHdr, WorldLightsHdr)).ShouldBeFalse();

    [Test]
    public void MapHasHdr_NoHdrWorldLights_IsFalse() =>
        BspHdr.MapHasHdr(Map(LightingHdr, LeafAmbientLightingHdr)).ShouldBeFalse();

    [Test]
    public void ForTf2_AnHdrMap_IsInteger() =>
        BspHdr.ForTf2(Map(LightingHdr, WorldLightsHdr, LeafAmbientLightingHdr)).ShouldBe(HdrType.IntegerHdr);

    [Test]
    public void ForTf2_AnLdrOnlyMap_IsNone() =>
        BspHdr.ForTf2(Map()).ShouldBe(HdrType.None);

    /// <summary><c>water.cpp:296-312</c> and <c>:351-355</c>, and <c>viewrender.cpp:2726-2736</c> with <c>:5351</c>.</summary>
    [Test]
    public void WaterScales_UnderInteger_AreFourFourAndAQuarter() =>
        BspHdr.WaterScales(HdrType.IntegerHdr).ShouldBe((4f, 4f, 0.25f));

    [Test]
    public void WaterScales_UnderNone_AreOne() =>
        BspHdr.WaterScales(HdrType.None).ShouldBe((1f, 1f, 1f));

    [Test]
    public void WaterScales_UnderFloat_AreOne() =>
        BspHdr.WaterScales(HdrType.FloatHdr).ShouldBe((1f, 1f, 1f));

    private static byte[] Map(params int[] lumps)
    {
        Dictionary<int, byte[]> payloads = [];

        foreach (int lump in lumps)
        {
            payloads[lump] = [1, 2, 3, 4];
        }

        return SyntheticBsp.Build(payloads);
    }
}
