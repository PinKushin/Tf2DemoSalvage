using System;
using System.Buffers.Binary;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Content.Tests.Bsp;

/// <summary>
/// `LUMP_OVERLAY_FADES` (60): one `doverlayfade_t` per overlay, parallel to lump 45.
/// </summary>
/// <remarks>
/// `bspfile.h:1056` — `float flFadeDistMinSq; float flFadeDistMaxSq;`. vbsp squares the mapper's
/// `fademindist`/`fademaxdist` only when positive (`utils/vbsp/overlay.cpp:43-52`), so 0 means "no
/// fade" and is what an overlay without the keys carries.
/// </remarks>
public sealed class OverlayFadeLumpTests
{
    private const int OverlayStride = 352;

    [Test]
    public void Read_WithAFadeLump_CarriesBothSquaredDistances()
    {
        byte[] fades = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(fades, 250_000f);
        BinaryPrimitives.WriteSingleLittleEndian(fades.AsSpan(4), 1_000_000f);

        BspOverlay overlay = BspOverlays.Read(SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [BspLumpIndex.Overlays] = new byte[OverlayStride],
            [BspLumpIndex.OverlayFades] = fades,
        }))[0];

        overlay.FadeMinSquared.ShouldBe(250_000f);
        overlay.FadeMaxSquared.ShouldBe(1_000_000f);
    }

    [Test]
    public void Read_WithoutAFadeLump_DoesNotFade()
    {
        BspOverlay overlay = BspOverlays.Read(SyntheticBsp.Build(new Dictionary<int, byte[]>
        {
            [BspLumpIndex.Overlays] = new byte[OverlayStride],
        }))[0];

        overlay.FadeMaxSquared.ShouldBe(0f);
    }
}
