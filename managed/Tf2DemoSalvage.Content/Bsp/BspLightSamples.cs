using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One face's lighting layout, as the engine's light read needs it.</summary>
/// <param name="Offset">`lightofs`, −1 for none.</param>
/// <param name="Styles">`styles[4]`, 255 for an unused slot.</param>
public readonly record struct BspFaceLightLayout(int Offset, (byte, byte, byte, byte) Styles);

/// <summary>A map's raw lightmap data, read as `R_LightVec` reads it (B415).</summary>
/// <remarks>
/// **Raw, not the decoded atlas**, because the atlas is halved and clamped for the renderer's overbright, and the
/// engine takes the linear value of a `ColorRGBExp32` itself: `c · power2_n[e + 128]`, where `power2_n` is
/// `2^(i − 128) / 255` (`mathlib/color_conversion.cpp:38`).
/// </remarks>
public sealed class BspLightSamples
{
    /// <summary>Samples per luxel.</summary>
    private const int SampleBytes = 4;

    /// <summary>A style slot not in use.</summary>
    private const byte NoStyle = 255;

    private readonly ReadOnlyMemory<byte> _lighting;
    private readonly IReadOnlyList<BspFaceLightLayout> _faces;

    /// <summary>Samples over a lighting lump.</summary>
    /// <param name="lighting">The lump.</param>
    /// <param name="faces">Each face's layout, by face index.</param>
    /// <exception cref="ArgumentNullException"><paramref name="faces"/> is null.</exception>
    public BspLightSamples(ReadOnlyMemory<byte> lighting, IReadOnlyList<BspFaceLightLayout> faces)
    {
        ArgumentNullException.ThrowIfNull(faces);

        _lighting = lighting;
        _faces = faces;
    }

    /// <summary>A face's layout, or null past the lump.</summary>
    /// <param name="face">The face index.</param>
    /// <returns>The layout.</returns>
    public BspFaceLightLayout? Layout(int face) => face >= 0 && face < _faces.Count ? _faces[face] : null;

    /// <summary>A face's average light over its styles — the branch `r_avglight 1`, the default, takes.</summary>
    /// <param name="face">The face index.</param>
    /// <param name="styles">`bUseLightStyles`: every style the face has, or only the first.</param>
    /// <param name="styleValue">`d_lightstylevalue[ style ] / 264`.</param>
    /// <returns>The light; black for an unlit face or an average outside the lump.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="styleValue"/> is null.</exception>
    /// <remarks>
    /// **The averages sit just before the samples**, one `ColorRGBExp32` per style in reverse: style slot k's is at
    /// `lightofs − 4 · (k + 1)`. `engine.dll` `0x1800d3990` reads them backwards from the sample pointer while
    /// `r_avglight` (`"1"`, `FCVAR_CHEAT`) is set; the luxel under the point only matters when it is 0.
    /// </remarks>
    public (float R, float G, float B) Average(int face, bool styles, Func<int, float> styleValue)
    {
        ArgumentNullException.ThrowIfNull(styleValue);

        if (Layout(face) is not { Offset: >= 0 } layout)
        {
            return default;
        }

        ReadOnlySpan<byte> lump = _lighting.Span;
        (byte a, byte b, byte c, byte d) = layout.Styles;
        ReadOnlySpan<byte> slots = [a, b, c, d];
        (float R, float G, float B) light = default;

        for (int slot = 0; slot < (styles ? slots.Length : 1); slot++)
        {
            long at = layout.Offset - ((slot + 1L) * SampleBytes);

            if (slots[slot] == NoStyle || at < 0 || at + SampleBytes > lump.Length)
            {
                break;
            }

            light = Add(light, lump.Slice((int)at, SampleBytes), styleValue(slots[slot]));
        }

        return light;
    }

    /// <summary>A `ColorRGBExp32`'s linear value times a style's, added: `c · 2^e / 255 · value`.</summary>
    private static (float R, float G, float B) Add((float R, float G, float B) light, ReadOnlySpan<byte> sample, float value)
    {
        float scale = MathF.ScaleB(1f, (sbyte)sample[3]) / 255f * value;

        return (light.R + (sample[0] * scale), light.G + (sample[1] * scale), light.B + (sample[2] * scale));
    }

    /// <summary>The light of one luxel over a face's styles — `engine.dll` `0x1800d37d0`, what a displacement hit adds.</summary>
    /// <param name="face">The face index.</param>
    /// <param name="s">The luxel across, `ds`.</param>
    /// <param name="t">Down, `dt`.</param>
    /// <param name="width">`smax`, luxels across.</param>
    /// <param name="height">`tmax`, luxels down.</param>
    /// <param name="bumped">Whether the face has bumped lightmaps, so each style holds four maps.</param>
    /// <param name="styles">`bUseLightStyles`: every style the face has, or only the first.</param>
    /// <param name="styleValue">`d_lightstylevalue[ style ] / 264`.</param>
    /// <returns>The light; black for an unlit face or a luxel outside the lump.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="styleValue"/> is null.</exception>
    /// <remarks>Each style's map follows the last, `smax · tmax` samples on, times four when bumped.</remarks>
    public (float R, float G, float B) Luxel(
        int face, int s, int t, int width, int height, bool bumped, bool styles, Func<int, float> styleValue)
    {
        ArgumentNullException.ThrowIfNull(styleValue);

        if (Layout(face) is not { Offset: >= 0 } layout)
        {
            return default;
        }

        ReadOnlySpan<byte> lump = _lighting.Span;
        (byte a, byte b, byte c, byte d) = layout.Styles;
        ReadOnlySpan<byte> slots = [a, b, c, d];
        long stride = (long)width * height * (bumped ? 4 : 1) * SampleBytes;
        long at = layout.Offset + ((((long)t * width) + s) * SampleBytes);
        (float R, float G, float B) light = default;

        for (int slot = 0; slot < (styles ? slots.Length : 1); slot++, at += stride)
        {
            if (slots[slot] == NoStyle || at < 0 || at + SampleBytes > lump.Length)
            {
                break;
            }

            light = Add(light, lump.Slice((int)at, SampleBytes), styleValue(slots[slot]));
        }

        return light;
    }
}
