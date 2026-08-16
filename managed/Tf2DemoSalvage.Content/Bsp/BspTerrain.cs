using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

using static Tf2DemoSalvage.Content.Bsp.BspStructLayout;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// A map's terrain lumps, read once and kept.
/// </summary>
/// <remarks>
/// **This exists because reading terrain a face at a time is quadratic in disguise.**
/// <see cref="BspDisplacements.ReadTriangles"/> takes the map's bytes and one face, so it parses the
/// header and decompresses both displacement lumps on every call. That reads correctly and costs
/// nothing noticeable for one face — and cp_process_final has 578 of them, so a full world build
/// decompressed the same two lumps 578 times.
///
/// It mattered because the world is rebuilt whenever the viewport changes size: the camera
/// projection is baked into the vertices, so a resize means a rebuild. Measured at ~830 ms per
/// rebuild, which is what made entering full screen crawl — the resize storm of hiding the sidebar,
/// dropping the border and going borderless fires several in a row.
///
/// Nothing about the decoding changes here; the arithmetic that identified the layout is documented
/// on <see cref="BspDisplacements"/> and still governs. This type only moves the lump reads out of
/// the loop.
/// </remarks>
public sealed class BspTerrain
{

    /// <summary>Smallest and largest subdivision a displacement may declare.</summary>
    /// <remarks>
    /// The engine allows 2 to 4, which is 5 to 17 vertices a side. A map is untrusted input (D32),
    /// and a power of 20 would ask for a million vertices from one face.
    /// </remarks>
    private const int MinimumPower = 2;
    private const int MaximumPower = 4;

    private readonly ReadOnlyMemory<byte> _infos;
    private readonly ReadOnlyMemory<byte> _vertices;

    private BspTerrain(ReadOnlyMemory<byte> infos, ReadOnlyMemory<byte> vertices)
    {
        _infos = infos;
        _vertices = vertices;
    }

    /// <summary>How many displacements the map declares.</summary>
    public int Count => _infos.Length / DispInfoStride;

    /// <summary>Reads a map's displacement lumps.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <returns>A reader for every displacement in it.</returns>
    /// <exception cref="InvalidDataException">The lumps are malformed.</exception>
    public static BspTerrain Create(ReadOnlyMemory<byte> file)
    {
        BspHeader header = BspHeader.Parse(file.Span);

        return new BspTerrain(
            BspLumpData.ReadStructures(
                file, header.Lump(BspLumpIndex.DispInfo), DispInfoStride, "dispinfo"),
            BspLumpData.ReadStructures(
                file, header.Lump(BspLumpIndex.DispVerts), DispVertStride, "dispverts"));
    }

    /// <summary>Reads one face's terrain, if it has any.</summary>
    /// <param name="surface">A surface whose <c>DisplacementIndex</c> is not -1.</param>
    /// <returns>The subdivided surface, or an empty list if the face is not a displacement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <exception cref="InvalidDataException">The displacement's data is malformed.</exception>
    /// <remarks>
    /// Returns triangles rather than a grid, so the caller draws them exactly like any other
    /// surface. The texture and lightmap coordinates come from interpolating the base quad's, which
    /// is how the engine parameterises a displacement: the terrain follows the surface the mapper
    /// drew it on, so the texture does not swim as the ground rises.
    /// </remarks>
    public IReadOnlyList<SurfaceVertex> ReadTriangles(BspSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!surface.IsDisplacement || surface.Vertices.Count != 4)
        {
            // A displacement is always built on a quad. Anything else is not one.
            return [];
        }

        ReadOnlySpan<byte> infos = _infos.Span;
        ReadOnlySpan<byte> vertices = _vertices.Span;

        if (surface.DisplacementIndex >= Count)
        {
            throw new InvalidDataException(
                $"Face {surface.FaceIndex} names displacement {surface.DisplacementIndex} of {Count}.");
        }

        ReadOnlySpan<byte> info = infos.Slice(
            surface.DisplacementIndex * DispInfoStride, DispInfoStride);

        (float X, float Y, float Z) start = (
            BinaryPrimitives.ReadSingleLittleEndian(info[DispStartPositionOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(info[(DispStartPositionOffset + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(info[(DispStartPositionOffset + 8)..]));

        int vertexStart = BinaryPrimitives.ReadInt32LittleEndian(info[DispVertexStartOffset..]);
        int power = BinaryPrimitives.ReadInt32LittleEndian(info[DispPowerOffset..]);

        if (power is < MinimumPower or > MaximumPower)
        {
            throw new InvalidDataException(
                $"Displacement {surface.DisplacementIndex} declares power {power}.");
        }

        int side = (1 << power) + 1;
        int needed = side * side;
        int available = vertices.Length / DispVertStride;

        if (vertexStart < 0 || vertexStart + needed > available)
        {
            throw new InvalidDataException(
                $"Displacement {surface.DisplacementIndex} needs vertices {vertexStart} to " +
                $"{vertexStart + needed} of {available}.");
        }

        // **The grid starts at the corner nearest startPosition, not at the face's first vertex.**
        // The compiler records which corner the mapper's grid began at, and ignoring it rotates the
        // terrain a quarter turn against the quad it belongs to - a hillside that runs the wrong
        // way while staying inside its own outline.
        SurfaceVertex[] corners = Rotate(surface.Vertices, start);
        SurfaceVertex[] grid = new SurfaceVertex[needed];

        // The face's own lightmap, in samples. The span is one less, because the coordinates run
        // between texel centres rather than across the whole image.
        float luxelWidth = Math.Max(1, surface.LuxelWidth);
        float luxelHeight = Math.Max(1, surface.LuxelHeight);
        float luxelSpanU = Math.Max(0f, luxelWidth - 1f);
        float luxelSpanV = Math.Max(0f, luxelHeight - 1f);

        for (int row = 0; row < side; row++)
        {
            float rowFraction = row / (float)(side - 1);

            for (int column = 0; column < side; column++)
            {
                float columnFraction = column / (float)(side - 1);

                // Bilinear across the quad: down one edge, down the opposite edge, then across.
                SurfaceVertex left = Mix(corners[0], corners[1], rowFraction);
                SurfaceVertex right = Mix(corners[3], corners[2], rowFraction);
                SurfaceVertex flat = Mix(left, right, columnFraction);

                ReadOnlySpan<byte> displacement = vertices.Slice(
                    (vertexStart + (row * side) + column) * DispVertStride, DispVertStride);

                float directionX = BinaryPrimitives.ReadSingleLittleEndian(displacement);
                float directionY = BinaryPrimitives.ReadSingleLittleEndian(displacement[4..]);
                float directionZ = BinaryPrimitives.ReadSingleLittleEndian(displacement[8..]);
                float distance = BinaryPrimitives.ReadSingleLittleEndian(displacement[12..]);
                float alpha = BinaryPrimitives.ReadSingleLittleEndian(displacement[16..]);

                // **A displacement's lightmap coordinates are NOT projected through lightmapVecs.**
                // The compiler assigns them straight from the corner ordering, spanning texel
                // centres across the face's own lightmap:
                //
                //     corner 0 -> (0.5, 0.5)          corner 1 -> (0.5, V + 0.5)
                //     corner 3 -> (U + 0.5, 0.5)      corner 2 -> (U + 0.5, V + 0.5)
                //
                // with the same start corner this grid is already rotated to. Interpolating the
                // base quad's projected coordinates instead - which is what this did, and which
                // looks obviously right - put 219 of cp_process_final's 578 displacements outside
                // their own lightmap, worst case by a factor of 389. Those were then clamped, so
                // each drew in one flat shade taken from an edge texel: the diffuse dark patches
                // scattered over the map's terrain.
                float luxelU = (0.5f + (columnFraction * luxelSpanU)) / luxelWidth;
                float luxelV = (0.5f + (rowFraction * luxelSpanV)) / luxelHeight;

                grid[(row * side) + column] = flat with
                {
                    LightU = luxelU,
                    LightV = luxelV,
                    X = flat.X + (directionX * distance),
                    Y = flat.Y + (directionY * distance),
                    Z = flat.Z + (directionZ * distance),

                    // Alpha rides in as the blend between the material's two textures. It is
                    // stored 0..255 in the file's own terms; normalised here so the renderer never
                    // has to know that.
                    Alpha = Math.Clamp(alpha / 255f, 0f, 1f),
                };
            }
        }

        List<SurfaceVertex> triangles = new((side - 1) * (side - 1) * 6);

        for (int row = 0; row + 1 < side; row++)
        {
            for (int column = 0; column + 1 < side; column++)
            {
                SurfaceVertex topLeft = grid[(row * side) + column];
                SurfaceVertex topRight = grid[(row * side) + column + 1];
                SurfaceVertex bottomLeft = grid[((row + 1) * side) + column];
                SurfaceVertex bottomRight = grid[((row + 1) * side) + column + 1];

                triangles.Add(topLeft);
                triangles.Add(topRight);
                triangles.Add(bottomRight);

                triangles.Add(topLeft);
                triangles.Add(bottomRight);
                triangles.Add(bottomLeft);
            }
        }

        return triangles;
    }

    /// <summary>Rotates a quad so the corner nearest a point comes first.</summary>
    private static SurfaceVertex[] Rotate(
        IReadOnlyList<SurfaceVertex> corners, (float X, float Y, float Z) start)
    {
        int nearest = 0;
        float best = float.MaxValue;

        for (int index = 0; index < 4; index++)
        {
            float dx = corners[index].X - start.X;
            float dy = corners[index].Y - start.Y;
            float dz = corners[index].Z - start.Z;
            float distance = (dx * dx) + (dy * dy) + (dz * dz);

            if (distance < best)
            {
                best = distance;
                nearest = index;
            }
        }

        return
        [
            corners[nearest],
            corners[(nearest + 1) % 4],
            corners[(nearest + 2) % 4],
            corners[(nearest + 3) % 4],
        ];
    }

    /// <summary>Interpolates every channel of a corner, position and coordinates alike.</summary>
    private static SurfaceVertex Mix(SurfaceVertex from, SurfaceVertex to, float fraction) => new(
        from.X + ((to.X - from.X) * fraction),
        from.Y + ((to.Y - from.Y) * fraction),
        from.Z + ((to.Z - from.Z) * fraction),
        from.U + ((to.U - from.U) * fraction),
        from.V + ((to.V - from.V) * fraction),
        from.LightU + ((to.LightU - from.LightU) * fraction),
        from.LightV + ((to.LightV - from.LightV) * fraction),
        from.Alpha + ((to.Alpha - from.Alpha) * fraction));
}
