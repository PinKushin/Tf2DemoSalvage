using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Bsp;

namespace Tf2DemoSalvage.Viewer3D;

/// <summary>Where one face's lightmap sits inside the atlas, in 0..1.</summary>
/// <param name="U">Left edge.</param>
/// <param name="V">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
internal readonly record struct AtlasRect(float U, float V, float Width, float Height);

/// <summary>
/// Every face's baked lighting packed into one texture.
/// </summary>
/// <remarks>
/// **One texture, because the alternative is thirteen thousand draw calls.** A map's lighting
/// arrives as one small image per face — typically 16x16 luxels, often far less — and binding each
/// one separately would mean a draw call per face. Packed into a single atlas, the whole map's
/// lighting is one bind, and faces can be batched by material instead.
///
/// The packing is a shelf: rows filled left to right, a new row started when the current one runs
/// out. That is not the tightest possible packing and does not need to be — lightmaps are tiny and
/// similar in size, which is the case shelf packing handles well.
///
/// **Each face's rectangle is inset by half a texel.** A lightmap sample sits at the centre of its
/// texel, so sampling at exactly 0 or 1 lands on the boundary and bilinear filtering pulls in the
/// neighbouring face's lighting — a bright seam along every edge in the map. The inset is the
/// standard fix and the reason the atlas stores rectangles rather than just offsets.
/// </remarks>
internal sealed class LightmapAtlas
{
    /// <summary>Gap between packed lightmaps, in texels.</summary>
    /// <remarks>
    /// One texel of padding on each side, so a bilinear sample at the edge of a face cannot reach
    /// its neighbour even before the half-texel inset is applied.
    /// </remarks>
    private const int Padding = 1;

    private LightmapAtlas(int width, int height, byte[] pixels, IReadOnlyList<AtlasRect> rectangles)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Rectangles = rectangles;
    }

    /// <summary>Atlas width in texels.</summary>
    public int Width { get; }

    /// <summary>Atlas height in texels.</summary>
    public int Height { get; }

    /// <summary>The atlas image, four bytes per texel, red first.</summary>
    public byte[] Pixels { get; }

    /// <summary>Where each face's lightmap sits, indexed by face.</summary>
    public IReadOnlyList<AtlasRect> Rectangles { get; }

    /// <summary>Packs every face's lightmap into one image.</summary>
    /// <param name="lightmaps">One entry per face; empty entries are skipped.</param>
    /// <param name="maximumWidth">Width to pack into.</param>
    /// <returns>The atlas.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lightmaps"/> is null.</exception>
    /// <remarks>
    /// A face with no lighting gets a zero rectangle, and the renderer draws it unlit. That is
    /// correct rather than a fallback: a face with <c>lightofs</c> of -1 genuinely has no baked
    /// light.
    /// </remarks>
    public static LightmapAtlas Pack(IReadOnlyList<BspLightmap> lightmaps, int maximumWidth = 2048)
    {
        ArgumentNullException.ThrowIfNull(lightmaps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);

        List<AtlasRect> rectangles = new(lightmaps.Count);
        List<(int Face, int X, int Y, int Width, int Height)> placements = [];

        int shelfX = Padding;
        int shelfY = Padding;
        int shelfHeight = 0;
        int usedWidth = 0;

        for (int face = 0; face < lightmaps.Count; face++)
        {
            BspLightmap lightmap = lightmaps[face];

            if (lightmap.IsEmpty)
            {
                rectangles.Add(default);
                continue;
            }

            int width = lightmap.Width;
            int height = lightmap.Height;

            if (shelfX + width + Padding > maximumWidth)
            {
                // Start a new shelf. The row's height is whatever the tallest entry in it needed.
                shelfX = Padding;
                shelfY += shelfHeight + Padding;
                shelfHeight = 0;
            }

            placements.Add((face, shelfX, shelfY, width, height));
            rectangles.Add(default);

            shelfHeight = Math.Max(shelfHeight, height);
            usedWidth = Math.Max(usedWidth, shelfX + width + Padding);
            shelfX += width + Padding;
        }

        int atlasWidth = Math.Max(1, usedWidth);
        int atlasHeight = Math.Max(1, shelfY + shelfHeight + Padding);
        byte[] pixels = new byte[atlasWidth * atlasHeight * 4];

        foreach ((int face, int x, int y, int width, int height) in placements)
        {
            ReadOnlySpan<byte> source = lightmaps[face].Pixels.Span;

            for (int row = 0; row < height; row++)
            {
                source.Slice(row * width * 4, width * 4)
                    .CopyTo(pixels.AsSpan((((y + row) * atlasWidth) + x) * 4));
            }

            // **Half a texel in from each edge.** A sample sits at its texel's centre, so a
            // coordinate of exactly 0 or 1 lands on the boundary and bilinear filtering reaches the
            // neighbouring face - a bright seam along every edge in the map.
            rectangles[face] = new AtlasRect(
                (x + 0.5f) / atlasWidth,
                (y + 0.5f) / atlasHeight,
                Math.Max(0f, width - 1f) / atlasWidth,
                Math.Max(0f, height - 1f) / atlasHeight);
        }

        return new LightmapAtlas(atlasWidth, atlasHeight, pixels, rectangles);
    }
}
