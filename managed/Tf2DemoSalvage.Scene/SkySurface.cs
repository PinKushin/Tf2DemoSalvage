using System;
using System.Buffers.Binary;

namespace Tf2DemoSalvage.Scene;

/// <summary>What the `sky` shader does to a face at TF2's default HDR level, as data rather than as a shader.</summary>
/// <remarks>
/// **`Sky_HDR_DX9`, which the material system picks for `sky` when HDR is on.** It binds `$hdrcompressedTexture` raw
/// (`sky_hdr_dx9.cpp:140`), scales by `$color · 8` (:224-226), and `sky_hdr_compressed_rgbs_ps2x.fxc` decodes every tap
/// as `rgb *= a` before its own bilinear filter — so decoding once, to linear half floats, and letting the hardware filter
/// is the same arithmetic. `sky_vs20.fxc:35-38` applies `$basetexturetransform` to the face's coordinate.
/// </remarks>
public static class SkySurface
{
    /// <summary>`c0 *= 8` in `sky_hdr_dx9.cpp`: the RGBS range.</summary>
    private const float RgbsScale = 8f;

    /// <summary>Decodes an RGBS texture to linear half floats, four a texel.</summary>
    /// <param name="rgba">Four bytes a texel, red first, as <c>VtfTexture.Decode</c> expands them.</param>
    /// <returns>Eight bytes a texel: `rgb · a · 8`, and an alpha of one.</returns>
    public static byte[] DecodeRgbs(ReadOnlySpan<byte> rgba)
    {
        int texels = rgba.Length / 4;
        byte[] halves = new byte[texels * 8];

        for (int texel = 0; texel < texels; texel++)
        {
            float scale = RgbsScale * rgba[(texel * 4) + 3] / 255f;
            Span<byte> into = halves.AsSpan(texel * 8, 8);

            BinaryPrimitives.WriteHalfLittleEndian(into, (Half)(rgba[texel * 4] / 255f * scale));
            BinaryPrimitives.WriteHalfLittleEndian(into[2..], (Half)(rgba[(texel * 4) + 1] / 255f * scale));
            BinaryPrimitives.WriteHalfLittleEndian(into[4..], (Half)(rgba[(texel * 4) + 2] / 255f * scale));
            BinaryPrimitives.WriteHalfLittleEndian(into[6..], (Half)1f);
        }

        return halves;
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
