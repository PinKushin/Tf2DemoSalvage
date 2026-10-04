using System;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary><c>HDRType_t</c>: <c>HDR_TYPE_NONE</c>, <c>HDR_TYPE_INTEGER</c>, <c>HDR_TYPE_FLOAT</c>.</summary>
public enum HdrType
{
    /// <summary>No HDR: overbright light clips, nothing is tone mapped.</summary>
    None = 0,

    /// <summary>HDR into 8-bit targets, scaled by the tone-map scale on the way out — TF2's on any DX9 PC.</summary>
    IntegerHdr = 1,

    /// <summary>Floating-point targets; only at <c>mat_hdr_level 3</c>.</summary>
    FloatHdr = 2,
}

/// <summary>Which HDR type TF2 runs, as <c>shaderapidx9.dll</c> and <c>engine.dll</c> decide it (B62).</summary>
/// <remarks>Every address is x64 TF2's, read in disassembly; the citations are in <c>HdrTypeConformanceTests</c>.</remarks>
public static class BspHdr
{
    /// <summary><c>mat_hdr_level</c>'s default, "2" (<c>shaderapidx9.dll 0x18007e700</c>).</summary>
    public const int DefaultMatHdrLevel = 2;

    /// <summary>A DX9-class device's level: anything a current PC or DXVK reports is at least 90.</summary>
    public const int Dx9Level = 95;

    /// <summary><c>HardwareCaps_t::m_HDRType</c> as the caps computation sets it (<c>0x1800293eb-0x180029448</c>).</summary>
    /// <param name="supportsInteger">The device can do integer HDR.</param>
    /// <param name="supportsFloat">The device can do float HDR.</param>
    /// <param name="matHdrLevel"><c>mat_hdr_level</c>.</param>
    /// <returns>Float only at level 3, so never at TF2's default.</returns>
    public static HdrType CapsHdrType(bool supportsInteger, bool supportsFloat, int matHdrLevel)
    {
        if (supportsFloat && matHdrLevel == 3)
        {
            return HdrType.FloatHdr;
        }

        return supportsInteger ? HdrType.IntegerHdr : HdrType.None;
    }

    /// <summary><c>CHardwareConfig::GetHDRType</c> (<c>0x180004810</c>).</summary>
    /// <param name="hdrEnabled"><c>m_bHDREnabled</c>, which <see cref="MapHasHdr"/> decides per map.</param>
    /// <param name="dxLevel">The DX support level.</param>
    /// <param name="caps">The caps' type.</param>
    /// <returns>The caps' type when enabled at DX 90 or above, else none.</returns>
    public static HdrType HdrTypeInUse(bool hdrEnabled, int dxLevel, HdrType caps) =>
        hdrEnabled && dxLevel >= 90 ? caps : HdrType.None;

    /// <summary><c>Map_CheckForHDR</c>'s lump test (<c>engine.dll 0x1800ffa10</c>).</summary>
    /// <param name="map">The map's bytes.</param>
    /// <returns>True when the HDR lighting and world lights are present, and from version 20 the HDR leaf ambient.</returns>
    public static bool MapHasHdr(ReadOnlySpan<byte> map)
    {
        BspHeader header = BspHeader.Parse(map);

        return header.Lump(BspLumpIndex.LightingHdr).Length > 0 &&
            header.Lump(BspLumpIndex.WorldLightsHdr).Length > 0 &&
            (header.Version < 20 || header.Lump(BspLumpIndex.LeafAmbientLightingHdr).Length > 0);
    }

    /// <summary>The type TF2 runs on this map at its defaults, on a DX9-class device.</summary>
    /// <param name="map">The map's bytes.</param>
    /// <returns><see cref="HdrType.IntegerHdr"/> on an HDR map, else <see cref="HdrType.None"/>.</returns>
    /// <remarks>Map_CheckForHDR also asks for <c>mat_hdr_level</c> at least 2, which the default meets.</remarks>
    public static HdrType ForTf2(ReadOnlySpan<byte> map) => HdrTypeInUse(
        MapHasHdr(map), Dx9Level, CapsHdrType(supportsInteger: true, supportsFloat: true, DefaultMatHdrLevel));

    /// <summary>The <c>Water</c> shader's three HDR-dependent scales.</summary>
    /// <param name="type">The HDR type.</param>
    /// <returns>
    /// The reflect tint's (<c>water.cpp:296-312</c>), c7.z's overbright (<c>:351-355</c>), and the reflection view's tone-map
    /// scale (<c>SetLightmapScaleForWater</c>, <c>viewrender.cpp:2726-2736</c>, from <c>PushView</c> <c>:5351</c>).
    /// </returns>
    public static (float ReflectTint, float Overbright, float ReflectionViewScale) WaterScales(HdrType type) =>
        type == HdrType.IntegerHdr ? (4f, 4f, 0.25f) : (1f, 1f, 1f);
}
