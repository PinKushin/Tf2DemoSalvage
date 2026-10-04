using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The 2D sky on real HDR maps, drawn by the viewer's sky pass and read back (B461).</summary>
/// <remarks>
/// **The expected pixel is computed here from the shipped VTF by the SDK's own arithmetic**, not by the renderer: the
/// RGBS taps and lerps of <c>sky_hdr_compressed_rgbs_ps2x.fxc</c> with <c>sky_hdr_dx9.cpp</c>'s constants, or the plain
/// <c>sky_ps2x.fxc</c> tap. The camera looks along +X, so the centre pixel of an odd-sized target samples the <c>rt</c>
/// face at <c>(0.5, 0.5)</c> through its <c>$basetexturetransform</c>.
/// </remarks>
public sealed class SkyRenderTests
{
    private const int Size = 65;
    /// <summary>About one texel a pixel across the 65-pixel view, so the sampler reads the top level this arithmetic reads.</summary>
    private const int MaximumTextureSize = 64;

    private static readonly FreeCamera AlongX = new() { Origin = (0f, 0f, 0f), Angles = (0f, 0f, 0f), Aspect = 1f };

    [Test]
    public void DrawSky_Ctf2fortUnderIntegerHdr_IsTheRgbsFaceTimesEight()
    {
        if (Load("ctf_2fort") is not { } assets)
        {
            return;
        }

        // The condition: 2fort runs integer HDR and its rt face takes the RGBS shader (sky_tf2_04rt.vmt).
        assets.Hdr.ShouldBe(HdrType.IntegerHdr);
        assets.SkyFaces[0]!.Value.Sky!.Value.Shader.ShouldBe(SkyPixelShader.HdrCompressedRgbs);

        Texels face = Texels.Of(assets.SkyFaces[0]!.Value);
        (float u, float v) = SkySurface.Coordinate((0.5f, 0.5f), assets.SkyFaces[0]!.Value.BaseTransform);
        (float r, float g, float b) expected = Rgbs(face, u, v);

        (int R, int G, int B) drawn = Draw(assets, 1f);
        (int R, int G, int B) quarter = Draw(assets, 0.25f);

        TestContext.Out.WriteLine($"2FORT SKY EXPECTED {Srgb(expected.r)},{Srgb(expected.g)},{Srgb(expected.b)} DRAWN {drawn} AT A QUARTER {quarter}");

        Near(drawn.R, Srgb(expected.r));
        Near(drawn.G, Srgb(expected.g));
        Near(drawn.B, Srgb(expected.b));

        // LINEAR_LIGHT_SCALE, as a reflection view under integer HDR draws it (common_ps_fxc.h:345-350).
        Near(quarter.B, Srgb(expected.b * 0.25f));
    }

    [Test]
    public void DrawSky_KothHarvestEventUnderIntegerHdr_IsThePlainShaderOnTheHdrBase()
    {
        // sky_halloween names only $hdrbasetexture, so Sky_HDR_DX9 binds it with sky_ps2x.fxc, read through the sRGB
        // curve because it is 8-bit, at $color of one (sky_hdr_dx9.cpp:173-192, :267-279). The map packs its own copy of
        // the faces — not the VPK's RGB888_BLUESCREEN ones — which is why it loaded before the blue-screen reader too.
        if (Load("koth_harvest_event") is not { } assets)
        {
            return;
        }

        assets.Hdr.ShouldBe(HdrType.IntegerHdr);
        assets.SkyFaces.Count.ShouldBe(SkyboxGeometry.Faces);
        assets.SkyFaces[0]!.Value.Sky!.Value.ShouldBe(new SkyFaceShading(SkyPixelShader.Sky, (1f, 1f, 1f), Srgb: true));

        Texels face = Texels.Of(assets.SkyFaces[0]!.Value);
        (float u, float v) = SkySurface.Coordinate((0.5f, 0.5f), assets.SkyFaces[0]!.Value.BaseTransform);
        (float r, float g, float b) expected = Bilinear(face, u, v, srgb: true);

        (int R, int G, int B) drawn = Draw(assets, 1f);

        TestContext.Out.WriteLine($"HALLOWEEN SKY EXPECTED {Srgb(expected.r)},{Srgb(expected.g)},{Srgb(expected.b)} DRAWN {drawn}");

        Near(drawn.R, Srgb(expected.r));
        Near(drawn.G, Srgb(expected.g));
        Near(drawn.B, Srgb(expected.b));
    }

    /// <summary>Within three levels: the GPU's filter and sRGB encode round where this arithmetic does not.</summary>
    private static void Near(int drawn, int expected) =>
        Math.Abs(drawn - expected).ShouldBeLessThanOrEqualTo(3, $"drew {drawn}, expected {expected}");

    private static MapAssets? Load(string name)
    {
        string tf = GameInstall.Root ?? string.Empty;
        string map = System.IO.Path.Combine(tf, "maps", $"{name}.bsp");

        if (!Direct3DApi.IsAvailable || !System.IO.File.Exists(map))
        {
            Assert.Ignore($"needs Direct3D and TF2's {name}");
            return null;
        }

        return MapAssets.Load(System.IO.File.ReadAllBytes(map), GameArchives.Open(tf), MaximumTextureSize);
    }

    /// <summary>The face's top level as the device receives it, widened to RGBA for reading.</summary>
    private sealed record Texels(byte[] Pixels, int Width, int Height)
    {
        public static Texels Of(MapTexture face) => new(face.Image.ToRgba(face.Width, face.Height), face.Width, face.Height);
    }

    private static (int R, int G, int B) Draw(MapAssets assets, float linearLightScale)
    {
        using OffscreenTarget target = OffscreenTarget.TryCreate(Size, Size)!;

        target.Clear(0f, 0f, 0f);
        target.DrawSky(assets, AlongX, linearLightScale).ShouldBeTrue();

        return target.PixelAt(Size / 2, Size / 2);
    }

    /// <summary>sky_hdr_compressed_rgbs_ps2x.fxc's PC branch, with sky_vs20.fxc's taps and c1, c0 = 8.</summary>
    private static (float, float, float) Rgbs(Texels face, float u, float v)
    {
        float w = face.Width;
        float h = face.Height;
        float fudge = 0.01f / MathF.Max(w, h);
        float dx = (0.5f / w) - fudge;
        float dy = (0.5f / h) - fudge;

        (float r, float g, float b, float a) Tap(float x, float y) => Sample(face, x, y, srgb: false);

        (float r, float g, float b) Premultiplied((float r, float g, float b, float a) s) => (s.r * s.a, s.g * s.a, s.b * s.a);

        (float r, float g, float b) s00 = Premultiplied(Tap(u - dx, v - dy));
        (float r, float g, float b) s10 = Premultiplied(Tap(u + dx, v - dy));
        (float r, float g, float b) s01 = Premultiplied(Tap(u - dx, v + dy));
        (float r, float g, float b) s11 = Premultiplied(Tap(u + dx, v + dy));

        float fx = Frac((u - dx) * w);
        float fy = Frac((v - dy) * h);

        (float r, float g, float b) top = Lerp(s00, s10, fx);
        (float r, float g, float b) bottom = Lerp(s01, s11, fx);
        (float r, float g, float b) result = Lerp(top, bottom, fy);

        return (result.r * 8f, result.g * 8f, result.b * 8f);
    }

    private static (float, float, float) Bilinear(Texels face, float u, float v, bool srgb)
    {
        (float r, float g, float b, _) = Sample(face, u, v, srgb);

        return (r, g, b);
    }

    /// <summary>A clamped bilinear tap in linear values, decoding sRGB first when the sampler would.</summary>
    private static (float, float, float, float) Sample(Texels face, float u, float v, bool srgb)
    {
        float x = (u * face.Width) - 0.5f;
        float y = (v * face.Height) - 0.5f;
        int x0 = (int)MathF.Floor(x);
        int y0 = (int)MathF.Floor(y);
        float fx = x - x0;
        float fy = y - y0;

        float[] Texel(int tx, int ty)
        {
            int cx = Math.Clamp(tx, 0, face.Width - 1);
            int cy = Math.Clamp(ty, 0, face.Height - 1);
            int at = ((cy * face.Width) + cx) * 4;
            float[] value = new float[4];

            for (int channel = 0; channel < 4; channel++)
            {
                float stored = face.Pixels[at + channel] / 255f;

                value[channel] = srgb && channel < 3 ? Linear(stored) : stored;
            }

            return value;
        }

        float[] a = Texel(x0, y0);
        float[] b = Texel(x0 + 1, y0);
        float[] c = Texel(x0, y0 + 1);
        float[] d = Texel(x0 + 1, y0 + 1);
        float[] mixed = new float[4];

        for (int channel = 0; channel < 4; channel++)
        {
            float top = a[channel] + ((b[channel] - a[channel]) * fx);
            float bottom = c[channel] + ((d[channel] - c[channel]) * fx);

            mixed[channel] = top + ((bottom - top) * fy);
        }

        return (mixed[0], mixed[1], mixed[2], mixed[3]);
    }

    private static (float, float, float) Lerp((float r, float g, float b) a, (float r, float g, float b) b, float t) =>
        (a.r + ((b.r - a.r) * t), a.g + ((b.g - a.g) * t), a.b + ((b.b - a.b) * t));

    private static float Frac(float value) => value - MathF.Floor(value);

    private static float Linear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    /// <summary>The byte the offscreen target stores: it is <c>B8G8R8A8_UNORM</c>, so the shader's linear output as written.</summary>
    private static int Srgb(float linear) => (int)MathF.Round(Math.Clamp(linear, 0f, 1f) * 255f);
}
