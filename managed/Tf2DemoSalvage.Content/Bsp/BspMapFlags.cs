using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>`LUMP_MAP_FLAGS` (59): what vrad baked, as `dflagslump_t.m_LevelFlags`.</summary>
/// <remarks>
/// `bspfile.h`: <c>LVLFLAGS_BAKED_STATIC_PROP_LIGHTING_NONHDR 0x00000001</c> and
/// <c>LVLFLAGS_BAKED_STATIC_PROP_LIGHTING_HDR 0x00000002</c>. `engine.dll` reads it at map load (0x1800ffa10) and
/// decides from it alone whether a static prop's `.vhv` is read at all, and which one.
/// </remarks>
public static class BspMapFlags
{
    /// <summary>`LUMP_MAP_FLAGS`.</summary>
    private const int Lump = 59;

    /// <summary>`LVLFLAGS_BAKED_STATIC_PROP_LIGHTING_NONHDR`.</summary>
    public const uint BakedStaticPropLightingNonHdr = 0x1;

    /// <summary>`LVLFLAGS_BAKED_STATIC_PROP_LIGHTING_HDR`.</summary>
    public const uint BakedStaticPropLightingHdr = 0x2;

    /// <summary>The map's level flags, zero when the lump is absent or empty.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>`m_LevelFlags`.</returns>
    public static uint Read(ReadOnlyMemory<byte> file)
    {
        ReadOnlyMemory<byte> lump = BspLumpData.Read(file, BspHeader.Parse(file.Span).Lump(Lump));

        return lump.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(lump.Span) : 0u;
    }
}
