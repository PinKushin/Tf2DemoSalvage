using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>How the `sky` shader draws a face at TF2's default HDR level.</summary>
/// <remarks>
/// `sky_hdr_dx9.cpp`: `$hdrcompressedTexture` is bound raw (`EnableSRGBRead( SHADER_SAMPLER0, false )`, :140) and its
/// constant is `$color` times 8 (:224-226); `sky_hdr_compressed_rgbs_ps2x.fxc` decodes each tap as `rgb *= a` before
/// filtering. `sky_vs20.fxc:35-38` dots `(u, v, 0, 1)` with `$basetexturetransform`'s two rows.
/// </remarks>
public sealed class SkySurfaceConformanceTests
{
    // **Which shader and which texture**: `Sky` falls back to `Sky_HDR_DX9` (DEFINE_FALLBACK_SHADER, sky_hdr_dx9.cpp:21),
    // which falls back to `Sky_DX9` under HDR_TYPE_NONE (:33-40); `Sky_HDR_DX9` takes $hdrcompressedtexture while
    // mat_use_compressed_hdr_textures is 1, its default (:19, :137-151), else $hdrcompressedtexture0 (:154-172), else
    // $hdrbasetexture (:173-192). `Sky_DX9` binds $basetexture (sky_dx9.cpp:82).

    [Test]
    public void Choose_IntegerHdrWithACompressedTexture_IsTheRgbsShader() =>
        SkySurface.Choose(Material(("$hdrcompressedtexture", "sky_c"), ("$hdrbasetexture", "sky_h"), ("$basetexture", "sky_l")), HdrType.IntegerHdr)
            .ShouldBe(new SkyChoice(SkyPixelShader.HdrCompressedRgbs, "sky_c"));

    [Test]
    public void Choose_IntegerHdrWithTexture0_IsTheThreeTextureShader() =>
        SkySurface.Choose(Material(("$hdrcompressedtexture0", "sky_0"), ("$hdrbasetexture", "sky_h")), HdrType.IntegerHdr)
            .ShouldBe(new SkyChoice(SkyPixelShader.HdrCompressed, "sky_0"));

    [Test]
    public void Choose_IntegerHdrWithOnlyAnHdrBase_IsThePlainShaderOnIt() =>
        SkySurface.Choose(Material(("$hdrbasetexture", "sky_h"), ("$basetexture", "sky_l")), HdrType.IntegerHdr)
            .ShouldBe(new SkyChoice(SkyPixelShader.Sky, "sky_h"));

    [Test]
    public void Choose_NoHdr_IsThePlainShaderOnTheBaseTexture() =>
        SkySurface.Choose(Material(("$hdrcompressedtexture", "sky_c"), ("$basetexture", "sky_l")), HdrType.None)
            .ShouldBe(new SkyChoice(SkyPixelShader.Sky, "sky_l"));

    // **The constants**: RGBS takes $color · 8 (sky_hdr_dx9.cpp:226-229) and is read raw (:140); the plain shader takes
    // $color, ×16 for RGBA16161616F only under HDR_TYPE_INTEGER (:268-279, sky_dx9.cpp:91-102), and reads through the
    // sRGB curve unless the texture is a 16-bit one (sky_hdr_dx9.cpp:175-180, sky_dx9.cpp:52-57).

    [Test]
    public void Shading_Rgbs_IsColourTimesEightReadRaw() =>
        SkySurface.Shading(Material(("$color", "[0.5 1 2]")), SkyPixelShader.HdrCompressedRgbs, VtfFormat.Dxt5, HdrType.IntegerHdr)
            .ShouldBe(new SkyFaceShading(SkyPixelShader.HdrCompressedRgbs, (4f, 8f, 16f), Srgb: false));

    [Test]
    public void Shading_AHalfFloatFaceUnderIntegerHdr_IsTimesSixteenReadRaw() =>
        SkySurface.Shading(Material(), SkyPixelShader.Sky, VtfFormat.Rgba16161616F, HdrType.IntegerHdr)
            .ShouldBe(new SkyFaceShading(SkyPixelShader.Sky, (16f, 16f, 16f), Srgb: false));

    [Test]
    public void Shading_AHalfFloatFaceUnderFloatHdr_IsTimesOne() =>
        SkySurface.Shading(Material(), SkyPixelShader.Sky, VtfFormat.Rgba16161616F, HdrType.FloatHdr)
            .ShouldBe(new SkyFaceShading(SkyPixelShader.Sky, (1f, 1f, 1f), Srgb: false));

    [Test]
    public void Shading_AnEightBitFace_IsTimesOneThroughTheSrgbCurve() =>
        SkySurface.Shading(Material(), SkyPixelShader.Sky, VtfFormat.Dxt1, HdrType.IntegerHdr)
            .ShouldBe(new SkyFaceShading(SkyPixelShader.Sky, (1f, 1f, 1f), Srgb: true));

    [Test]
    public void TexelInfo_ACompressedFace_IsHalfATexelLessTheFudgeAndTheSize()
    {
        // sky_hdr_dx9.cpp:220-224: FUDGE = 0.01 / max(w, h); c1 = { 0.5/w - FUDGE, 0.5/h - FUDGE, w, h }.
        (float X, float Y, float W, float H) info = SkySurface.TexelInfo(SkyPixelShader.HdrCompressedRgbs, 256, 128);

        info.X.ShouldBe((0.5f / 256f) - (0.01f / 256f), 1e-7f);
        info.Y.ShouldBe((0.5f / 128f) - (0.01f / 256f), 1e-7f);
        info.W.ShouldBe(256f);
        info.H.ShouldBe(128f);
    }

    [Test]
    public void TexelInfo_APlainFace_IsZero() =>
        SkySurface.TexelInfo(SkyPixelShader.Sky, 256, 128).ShouldBe((0f, 0f, 0f, 0f));

    private static VmtMaterial Material(params (string Key, string Value)[] values)
    {
        System.Text.StringBuilder text = new("\"sky\"\n{\n");

        foreach ((string key, string value) in values)
        {
            text.Append('"').Append(key).Append("\" \"").Append(value).Append("\"\n");
        }

        return VmtMaterial.Parse(System.Text.Encoding.ASCII.GetBytes(text.Append('}').ToString()));
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
}
