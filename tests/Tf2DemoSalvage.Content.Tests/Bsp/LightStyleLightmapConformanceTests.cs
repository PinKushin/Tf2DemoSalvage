using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// A face lit by more than its base style: vrad stores one lightmap per style slot (`dface_t.styles[4]`), style-major
/// (`radial.cpp`: `lightofs + ( k * bumpSampleCount + bumpSample ) * numluxels * 4`), and the engine builds what it draws
/// as the sum of each style's samples times that style's value over 264 (`R_BuildLightMap`; the values from
/// `R_AnimateLight`, `engine.dll` `0x1800d3ec0`).
/// </summary>
public sealed class LightStyleLightmapConformanceTests
{
    private const int FacesLump = 7;
    private const int TexinfoLump = 6;
    private const int LightingLump = 8;

    [Test]
    public void ReadAll_AFaceWithASwitchableStyle_CarriesEachStylesSamples()
    {
        BspFaceLighting face = BspLightmaps.ReadAll(Map((0, 32, 255, 255)))[0];

        face.Styles.Count.ShouldBe(2);
        face.Styles[0].Style.ShouldBe((byte)0);
        face.Styles[1].Style.ShouldBe((byte)32);
        face.Styles[1].Sets[0][0].ShouldBe(40f);
    }

    [Test]
    public void ReadAll_AFaceLitByStyleZeroAlone_CarriesNoStyles()
    {
        BspLightmaps.ReadAll(Map((0, 255, 255, 255)))[0].Styles.ShouldBeEmpty();
    }

    /// <remarks>
    /// Style 0's red is 64 and style 32's is 40. With the switchable light on (`'m'`, 264) the sum is 104; off (`'a'`, 0)
    /// it is 64 — each encoded exactly as a single-style face would be.
    /// </remarks>
    [Test]
    public void Compose_ASwitchableLightOnAndOff_AddsItsSamplesByItsValue()
    {
        BspFaceLighting face = BspLightmaps.ReadAll(Map((0, 32, 255, 255)))[0];
        byte[] on = new byte[4];
        byte[] off = new byte[4];

        BspLightmaps.Compose(face.Styles, 0, static _ => 1f, on);
        BspLightmaps.Compose(face.Styles, 0, static style => style == 32 ? 0f : 1f, off);

        on[0].ShouldBe(BspLightmaps.ReadAll(Map((0, 255, 255, 255), red: 104))[0].Flat.Pixels.Span[0]);
        off[0].ShouldBe(BspLightmaps.ReadAll(Map((0, 255, 255, 255), red: 64))[0].Flat.Pixels.Span[0]);
    }

    /// <remarks>
    /// At TF2's default HDR level the engine lights faces from `LUMP_LIGHTING_HDR` (53) through `LUMP_FACES_HDR` (58), each
    /// taken whenever it is non-empty, and `LUMP_LIGHTING`/`LUMP_FACES` only otherwise. The HDR pair here says red 100 at
    /// offset 4; the LDR pair says 64 at offset 0. Stored halved for the shader's overbright: 50 against 32.
    /// </remarks>
    [Test]
    public void ReadAll_AMapCarryingBothLightings_IsLitByTheHdrPair()
    {
        byte[] map = HdrMap();

        BspLightmaps.ReadAll(map)[0].Flat.Pixels.Span[0].ShouldBe((byte)50);
        BspLightmaps.Read(map)[0].Pixels.Span[0].ShouldBe((byte)50);
    }

    [Test]
    public void ReadAll_AMapWithOnlyLdrLighting_IsLitByIt()
    {
        BspLightmaps.ReadAll(Map((0, 255, 255, 255)))[0].Flat.Pixels.Span[0].ShouldBe((byte)32);
    }

    /// <summary>One face, one luxel, lit red 64 by the LDR pair and red 100 by the HDR pair at a different offset.</summary>
    private static byte[] HdrMap()
    {
        byte[] Face(int offset)
        {
            byte[] face = new byte[56];

            (face[16], face[17], face[18], face[19]) = ((byte)0, (byte)255, (byte)255, (byte)255);
            BinaryPrimitives.WriteInt32LittleEndian(face.AsSpan(20), offset);

            return face;
        }

        return SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [TexinfoLump] = new byte[72],
            [FacesLump] = Face(0),
            [LightingLump] = [64, 0, 0, 0],
            [FacesHdrLump] = Face(4),
            [LightingHdrLump] = [0, 0, 0, 0, 100, 0, 0, 0],
        });
    }

    private const int FacesHdrLump = 58;
    private const int LightingHdrLump = 53;

    /// <summary>One unbumped face, one luxel, lit by the given style slots: red 64 for the first, 40 for the second.</summary>
    private static byte[] Map((byte, byte, byte, byte) styles, byte red = 64)
    {
        byte[] faces = new byte[56];

        (faces[16], faces[17], faces[18], faces[19]) = styles;
        BinaryPrimitives.WriteInt32LittleEndian(faces.AsSpan(20), 0);

        // m_LightmapTextureSizeInLuxels is one less than the samples: zero for one luxel.
        BinaryPrimitives.WriteInt32LittleEndian(faces.AsSpan(36), 0);
        BinaryPrimitives.WriteInt32LittleEndian(faces.AsSpan(40), 0);

        byte[] lighting = [red, 0, 0, 0, 40, 0, 0, 0];

        return SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [TexinfoLump] = new byte[72],
            [FacesLump] = faces,
            [LightingLump] = lighting,
        });
    }
}
