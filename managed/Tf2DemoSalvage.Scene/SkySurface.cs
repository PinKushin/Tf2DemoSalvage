using System;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>The pixel shader a sky face draws with — one of the three <c>Sky_HDR_DX9</c> and <c>Sky_DX9</c> bind.</summary>
public enum SkyPixelShader
{
    /// <summary><c>sky_ps2x.fxc</c>: one tap, times <c>InputScale</c>.</summary>
    Sky = 0,

    /// <summary><c>sky_hdr_compressed_rgbs_ps2x.fxc</c>: four RGBS taps, each <c>rgb *= a</c>, filtered by hand.</summary>
    HdrCompressedRgbs = 1,

    /// <summary><c>sky_hdr_compressed_ps2x.fxc</c>, compression method B — whose published body returns red.</summary>
    HdrCompressed = 2,
}

/// <summary>Which shader a sky face draws with and which material parameter names its texture.</summary>
/// <param name="Shader">The pixel shader.</param>
/// <param name="Texture">The texture path the parameter names.</param>
public readonly record struct SkyChoice(SkyPixelShader Shader, string Texture);

/// <summary>A sky face's draw state: its shader, <c>c0</c>, and whether sampler zero reads through the sRGB curve.</summary>
/// <param name="Shader">The pixel shader.</param>
/// <param name="InputScale"><c>c0</c>, <c>$color</c> with the shader's own factor.</param>
/// <param name="Srgb">Whether the texture is read through the sRGB curve.</param>
public readonly record struct SkyFaceShading(SkyPixelShader Shader, (float Red, float Green, float Blue) InputScale, bool Srgb);

/// <summary>The <c>sky</c> shader's choices for one face, as <c>sky_hdr_dx9.cpp</c> and <c>sky_dx9.cpp</c> make them.</summary>
/// <remarks>
/// **`Sky` is `Sky_HDR_DX9`**, by <c>DEFINE_FALLBACK_SHADER( Sky, Sky_HDR_DX9 )</c> (<c>sky_hdr_dx9.cpp:21</c>), and that
/// falls back to <c>Sky_DX9</c> under <c>HDR_TYPE_NONE</c> (<c>:33-40</c>). <c>sky_vs20.fxc:35-38</c> applies
/// <c>$basetexturetransform</c> to the face's coordinate (<see cref="Coordinate"/>).
/// </remarks>
public static class SkySurface
{
    /// <summary><c>c0 *= 8</c> for the RGBS path (<c>sky_hdr_dx9.cpp:227-229</c>).</summary>
    private const float RgbsScale = 8f;

    /// <summary><c>c0 *= 16</c> for a 16-bit face (<c>sky_hdr_dx9.cpp:276-278</c>, <c>sky_dx9.cpp:99-101</c>).</summary>
    private const float SixteenBitScale = 16f;

    /// <summary>The shader and texture a face draws with.</summary>
    /// <param name="material">The face's material.</param>
    /// <param name="hdr">The HDR type the map runs under.</param>
    /// <returns>The choice, or null when the material names no texture the chosen shader binds.</returns>
    /// <remarks>
    /// <c>mat_use_compressed_hdr_textures</c> is 1 (<c>sky_hdr_dx9.cpp:19</c>), so <c>$hdrcompressedtexture</c> wins
    /// (<c>:137-151</c>), then <c>$hdrcompressedtexture0</c> (<c>:154-172</c>), then <c>$hdrbasetexture</c>
    /// (<c>:173-192</c>). **A material under HDR naming none of the three falls to <c>$basetexture</c>** — interpolated:
    /// <c>Sky_HDR_DX9</c> would bind an undefined parameter, and no shipped sky does it (all 324 name one).
    /// </remarks>
    public static SkyChoice? Choose(VmtMaterial material, HdrType hdr)
    {
        ArgumentNullException.ThrowIfNull(material);

        if (hdr != HdrType.None)
        {
            if (material.Value("$hdrcompressedtexture") is { } compressed)
            {
                return new SkyChoice(SkyPixelShader.HdrCompressedRgbs, compressed);
            }

            if (material.Value("$hdrcompressedtexture0") is { } first)
            {
                return new SkyChoice(SkyPixelShader.HdrCompressed, first);
            }

            if (material.Value("$hdrbasetexture") is { } hdrBase)
            {
                return new SkyChoice(SkyPixelShader.Sky, hdrBase);
            }
        }

        return material.Value("$basetexture") is { } texture ? new SkyChoice(SkyPixelShader.Sky, texture) : null;
    }

    /// <summary>The face's constant and sampler state.</summary>
    /// <param name="material">The face's material, for <c>$color</c>.</param>
    /// <param name="shader">The chosen shader.</param>
    /// <param name="format">The texture's format as loaded.</param>
    /// <param name="hdr">The HDR type.</param>
    /// <returns>The shading.</returns>
    /// <remarks>
    /// <c>$color</c> is copied raw (<c>memcpy</c>, <c>sky_hdr_dx9.cpp:209-212</c>). The RGBS and method-B textures are read
    /// raw (<c>:140</c>, <c>:159-161</c>); the plain shader reads raw only a 16-bit texture (<c>:175-180</c>,
    /// <c>sky_dx9.cpp:52-57</c>) and multiplies a half-float one by 16 only under <c>HDR_TYPE_INTEGER</c>
    /// (<c>:270-279</c>). <c>IMAGE_FORMAT_RGBA16161616</c>, ×16 always, has no reader here.
    /// </remarks>
    public static SkyFaceShading Shading(VmtMaterial material, SkyPixelShader shader, VtfFormat format, HdrType hdr)
    {
        ArgumentNullException.ThrowIfNull(material);

        (float red, float green, float blue) = material.ColourFactor;

        float scale = shader switch
        {
            SkyPixelShader.HdrCompressedRgbs => RgbsScale,
            SkyPixelShader.Sky when format == VtfFormat.Rgba16161616F && hdr == HdrType.IntegerHdr => SixteenBitScale,
            _ => 1f,
        };

        bool srgb = shader == SkyPixelShader.Sky && format != VtfFormat.Rgba16161616F;

        return new SkyFaceShading(shader, (red * scale, green * scale, blue * scale), srgb);
    }

    /// <summary><c>VERTEX_SHADER_SHADER_SPECIFIC_CONST_0</c>: the RGBS taps' half-texel offsets and the texture's size.</summary>
    /// <param name="shader">The shader.</param>
    /// <param name="width">The texture's actual width.</param>
    /// <param name="height">The texture's actual height.</param>
    /// <returns><c>{ 0.5/w − FUDGE, 0.5/h − FUDGE, w, h }</c> for RGBS (<c>sky_hdr_dx9.cpp:220-224</c>), else zero (<c>:244</c>).</returns>
    public static (float X, float Y, float W, float H) TexelInfo(SkyPixelShader shader, int width, int height)
    {
        if (shader != SkyPixelShader.HdrCompressedRgbs)
        {
            return (0f, 0f, 0f, 0f);
        }

        float w = width;
        float h = height;

        // "per ATI"
        float fudge = 0.01f / MathF.Max(w, h);

        return ((0.5f / w) - fudge, (0.5f / h) - fudge, w, h);
    }

    /// <summary>A face coordinate through `$basetexturetransform`, as `sky_vs20.fxc` dots it.</summary>
    /// <param name="uv">The face's own coordinate.</param>
    /// <param name="transform">The material's transform, or null for none.</param>
    /// <returns>The coordinate the texture is sampled at.</returns>
    public static (float U, float V) Coordinate((float U, float V) uv, TextureTransform? transform) =>
        transform is not { } rows
            ? uv
            : ((uv.U * rows.Row0.X) + (uv.V * rows.Row0.Y) + rows.Row0.W,
               (uv.U * rows.Row1.X) + (uv.V * rows.Row1.Y) + rows.Row1.W);
}
