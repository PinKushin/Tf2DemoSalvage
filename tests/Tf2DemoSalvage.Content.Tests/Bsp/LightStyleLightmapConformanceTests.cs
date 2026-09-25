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
