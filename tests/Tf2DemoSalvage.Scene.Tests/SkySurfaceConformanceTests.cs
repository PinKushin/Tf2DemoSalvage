using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>How the `sky` shader draws a face at TF2's default HDR level.</summary>
/// <remarks>
/// `sky_hdr_dx9.cpp`: `$hdrcompressedTexture` is bound raw (`EnableSRGBRead( SHADER_SAMPLER0, false )`, :140) and its
/// constant is `$color` times 8 (:224-226); `sky_hdr_compressed_rgbs_ps2x.fxc` decodes each tap as `rgb *= a` before
/// filtering. `sky_vs20.fxc:35-38` dots `(u, v, 0, 1)` with `$basetexturetransform`'s two rows.
/// </remarks>
public sealed class SkySurfaceConformanceTests
{
    [Test]
    public void DecodeRgbs_ATexel_IsRgbTimesAlphaTimesEight()
    {
        // (255, 128, 0) at alpha 64: 8 · 64/255 = 2.0078 scales each channel.
        byte[] halves = SkySurface.DecodeRgbs([255, 128, 0, 64]);

        float scale = 8f * 64f / 255f;

        Channel(halves, 0).ShouldBe(scale, 0.01f);
        Channel(halves, 1).ShouldBe(128f / 255f * scale, 0.01f);
        Channel(halves, 2).ShouldBe(0f);
        Channel(halves, 3).ShouldBe(1f);
    }

    [Test]
    public void Coordinate_HarvestsSideTransform_StretchesVByTwo()
    {
        // `sky_harvest_01bk`: "center 0 0 scale 1 2 rotate 0 translate 0 0" — v 0..1 becomes 0..2, so the clamped
        // texture covers the top half of the face and its bottom row the rest.
        TextureTransform scale = new((1f, 0f, 0f, 0f), (0f, 2f, 0f, 0f));

        SkySurface.Coordinate((0.25f, 0.75f), scale).ShouldBe((0.25f, 1.5f));
        SkySurface.Coordinate((0.25f, 0.75f), null).ShouldBe((0.25f, 0.75f));
    }

    private static float Channel(byte[] halves, int index) =>
        (float)BinaryPrimitives.ReadHalfLittleEndian(halves.AsSpan(index * 2));
}
