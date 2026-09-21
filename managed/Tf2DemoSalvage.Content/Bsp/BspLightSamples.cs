using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One face's lighting layout, as the engine's light read needs it.</summary>
/// <param name="Offset">`lightofs`, −1 for none.</param>
/// <param name="ExtentS">`m_LightmapExtents[0]`: luxels across, less one.</param>
/// <param name="ExtentT">`m_LightmapExtents[1]`.</param>
/// <param name="Styles">`styles[4]`, 255 for an unused slot.</param>
/// <param name="Bumped">Whether it carries four sets per style.</param>
public readonly record struct BspFaceLightLayout(int Offset, int ExtentS, int ExtentT, (byte, byte, byte, byte) Styles, bool Bumped);

/// <summary>A map's raw lightmap samples, read at one luxel as `R_LightVec` reads them (B415).</summary>
/// <remarks>
/// **Raw, not the decoded atlas**, because the atlas is halved and clamped for the renderer's overbright, and the
/// engine's reader (`engine.dll` `0x1800d37d0`) takes the linear value of the `ColorRGBExp32` sample itself:
/// `c · power2_n[e + 128]`, where `power2_n` is `2^(i − 128) / 255` (`mathlib/color_conversion.cpp:38`).
/// </remarks>
public sealed class BspLightSamples
{
    /// <summary>Samples per luxel.</summary>
    private const int SampleBytes = 4;

    /// <summary>`NUM_BUMP_VECTS + 1`.</summary>
    private const int BumpedSets = 4;

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

    /// <summary>The linear light at one luxel.</summary>
    /// <param name="face">The face index.</param>
    /// <param name="s">The luxel across, from the face's mins.</param>
    /// <param name="t">The luxel down.</param>
    /// <param name="styles">`bUseLightStyles`: every style the face has, or only the first.</param>
    /// <param name="styleValue">`d_lightstylevalue[ style ] / 264`.</param>
    /// <returns>The light; black for an unlit face or a luxel outside the lump.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="styleValue"/> is null.</exception>
    public (float R, float G, float B) Linear(int face, int s, int t, bool styles, Func<int, float> styleValue)
    {
        ArgumentNullException.ThrowIfNull(styleValue);

        if (Layout(face) is not { Offset: >= 0 } layout)
        {
            return default;
        }

        int across = layout.ExtentS + 1;
        int luxels = across * (layout.ExtentT + 1);
        int step = luxels * (layout.Bumped ? BumpedSets : 1) * SampleBytes;
        long at = layout.Offset + ((((long)across * t) + s) * SampleBytes);
        ReadOnlySpan<byte> lump = _lighting.Span;
        (byte a, byte b, byte c, byte d) = layout.Styles;
        ReadOnlySpan<byte> slots = [a, b, c, d];
        (float R, float G, float B) light = default;

        for (int slot = 0; slot < (styles ? slots.Length : 1); slot++, at += step)
        {
            if (slots[slot] == NoStyle || at < 0 || at + SampleBytes > lump.Length)
            {
                break;
            }

            ReadOnlySpan<byte> sample = lump.Slice((int)at, SampleBytes);
            float scale = MathF.ScaleB(1f, (sbyte)sample[3]) / 255f * styleValue(slots[slot]);

            light = (light.R + (sample[0] * scale), light.G + (sample[1] * scale), light.B + (sample[2] * scale));
        }

        return light;
    }
}
