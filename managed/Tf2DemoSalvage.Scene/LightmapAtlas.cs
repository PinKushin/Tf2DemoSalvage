using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene;

/// <summary>Where one face's lightmap sits inside the atlas, in 0..1.</summary>
/// <param name="U">Left edge.</param>
/// <param name="V">Top edge.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct AtlasRect(float U, float V, float Width, float Height);

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
public sealed class LightmapAtlas
{
    /// <summary>
    /// The texel unlit faces sample, kept white.
    /// </summary>
    /// <remarks>
    /// **A face with no baked light must not come out black.** Its rectangle is empty, so it
    /// samples the atlas at (0,0) — which is padding, and padding is zeroes. Multiplying a texture
    /// by black gives black, so every unlit surface in the map drew as a dark blob over otherwise
    /// correct geometry.
    ///
    /// Reserving the first texel and pointing empty rectangles at it means an unlit face is drawn
    /// at full texture brightness, which is what "no lightmap" should look like, and it needs no
    /// branch in the shader.
    /// </remarks>
    private const int WhiteTexel = 0;

    /// <summary>Gap between packed lightmaps, in texels.</summary>
    /// <remarks>
    /// One texel of padding on each side, so a bilinear sample at the edge of a face cannot reach
    /// its neighbour even before the half-texel inset is applied.
    /// </remarks>
    private const int Padding = 1;

    private LightmapAtlas(
        int width,
        int height,
        byte[] pixels,
        IReadOnlyList<AtlasRect> rectangles,
        IReadOnlyList<float> directionalSteps)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
        Rectangles = rectangles;
        DirectionalSteps = directionalSteps;
    }

    /// <summary>How far along to step for each directional lightmap, in texture coordinates.</summary>
    /// <remarks>
    /// **Valve's own layout.** lightmappedgeneric_vs20.fxc builds the three directional coordinates
    /// by adding a per-vertex offset to the base one, once, twice and three times, so the four sets
    /// sit adjacent in a page and the shader walks them. Packing a strip mirrors that, and any
    /// bilinear bleed at a seam is behaviour the engine already ships.
    ///
    /// Zero for a face that is not bump lit, which is most of them.
    /// </remarks>
    public IReadOnlyList<float> DirectionalSteps { get; }

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

        return PackAll(
            [.. lightmaps.Select(map => new BspFaceLighting(map, []))], maximumWidth);
    }

    /// <summary>Packs every face's lighting, directional sets included, into one image.</summary>
    /// <param name="lighting">One entry per face; empty entries are skipped.</param>
    /// <param name="maximumWidth">Width to pack into.</param>
    /// <returns>The atlas.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lighting"/> is null.</exception>
    /// <remarks>
    /// A bump-lit face occupies a strip four lightmaps wide rather than one, and its entry in
    /// <see cref="DirectionalSteps"/> says how far along each set sits. The rectangle still covers
    /// set 0 alone, so every existing caller keeps the coordinates it always had.
    /// </remarks>
    public static LightmapAtlas PackAll(
        IReadOnlyList<BspFaceLighting> lighting, int maximumWidth = 2048)
    {
        ArgumentNullException.ThrowIfNull(lighting);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumWidth);

        List<BspLightmap> lightmaps = [.. lighting.Select(face => face.Flat)];
        List<AtlasRect> rectangles = new(lightmaps.Count);
        List<float> steps = new(lightmaps.Count);
        List<(int Face, int X, int Y, int Width, int Height)> placements = [];

        // The reserved white texel sits at (0,0), so packing starts past it.
        int shelfX = Padding + 1;
        int shelfY = Padding;
        int shelfHeight = 0;
        int usedWidth = 0;

        for (int face = 0; face < lightmaps.Count; face++)
        {
            BspLightmap lightmap = lightmaps[face];

            if (lightmap.IsEmpty)
            {
                // Points at the reserved white texel rather than at nothing: see WhiteTexel.
                rectangles.Add(new AtlasRect(0f, 0f, 0f, 0f));
                steps.Add(0f);
                continue;
            }

            // **A bump-lit face is four lightmaps wide.** Reserving one face's width and then
            // writing four is how the next face on the shelf gets overwritten - and the damage
            // reads as a lighting artefact on a neighbour rather than as a packing bug.
            int sets = 1 + lighting[face].Directional.Count;
            int width = lightmap.Width * sets;
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
            steps.Add(sets > 1 ? lightmap.Width : 0f);

            shelfHeight = Math.Max(shelfHeight, height);
            usedWidth = Math.Max(usedWidth, shelfX + width + Padding);
            shelfX += width + Padding;
        }

        int atlasWidth = Math.Max(2, usedWidth);
        int atlasHeight = Math.Max(2, shelfY + shelfHeight + Padding);
        byte[] pixels = new byte[atlasWidth * atlasHeight * 4];

        // The reserved texel, white and opaque, for faces with no baked light.
        pixels[(WhiteTexel * 4) + 0] = 255;
        pixels[(WhiteTexel * 4) + 1] = 255;
        pixels[(WhiteTexel * 4) + 2] = 255;
        pixels[(WhiteTexel * 4) + 3] = 255;

        foreach ((int face, int x, int y, int _, int height) in placements)
        {
            int setWidth = lightmaps[face].Width;
            int set = 0;

            // Set 0 first, then each directional set one lightmap further along, which is the
            // order the shader's stepped coordinates expect to find them in.
            foreach (BspLightmap source in
                     new[] { lightmaps[face] }.Concat(lighting[face].Directional))
            {
                ReadOnlySpan<byte> bytes = source.Pixels.Span;

                for (int row = 0; row < height; row++)
                {
                    bytes.Slice(row * setWidth * 4, setWidth * 4)
                        .CopyTo(pixels.AsSpan(
                            (((y + row) * atlasWidth) + x + (set * setWidth)) * 4));
                }

                set++;
            }

            // **Half a texel in from each edge.** A sample sits at its texel's centre, so a
            // coordinate of exactly 0 or 1 lands on the boundary and bilinear filtering reaches the
            // neighbouring face - a bright seam along every edge in the map.
            rectangles[face] = new AtlasRect(
                (x + 0.5f) / atlasWidth,
                (y + 0.5f) / atlasHeight,
                Math.Max(0f, setWidth - 1f) / atlasWidth,
                Math.Max(0f, height - 1f) / atlasHeight);

            // Held in texels until now so the packing arithmetic stays whole numbers.
            steps[face] = steps[face] / atlasWidth;
        }

        return new LightmapAtlas(atlasWidth, atlasHeight, pixels, rectangles, steps);
    }
}
