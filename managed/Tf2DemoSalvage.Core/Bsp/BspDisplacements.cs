using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace Tf2DemoSalvage.Core.Bsp;

/// <summary>
/// Terrain: the heightfield a displacement face is really made of.
/// </summary>
/// <remarks>
/// **A displacement's entry in FACES is not its shape.** That face is the flat quad the terrain was
/// built on; the real surface is a grid subdividing it, with every vertex pushed along its own
/// direction. Drawing the quad gives a flat slab where a hillside should be, and paints it with the
/// first of the two textures the terrain blends — which is why <c>cp_process_final</c>'s outdoor
/// areas came out as bare dirt with no grass anywhere.
///
/// Two lumps hold it, and their layout was confirmed by arithmetic rather than recalled:
///
/// <code>
///   DISPINFO    (26), 176 bytes each: startPosition at 0, DispVertStart at 12, power at 20
///   DISP_VERTS  (33),  20 bytes each: direction at 0, distance at 12, alpha at 16
/// </code>
///
/// With those, every displacement's vertex range fits inside DISP_VERTS and the ranges together
/// account for exactly 100.0% of it — measured on cp_process_final (578 displacements, 20,306
/// vertices), cp_badlands (1,191 / 42,415) and pl_upward (558 / 14,174). A wrong stride does not
/// divide the lump at all.
///
/// **`power` is 2, 3 or 4**, giving a grid of 5, 9 or 17 vertices a side. The grid is built by
/// interpolating across the base quad and then displacing:
/// <c>position = bilinear(corners) + direction * distance</c>.
///
/// **`alpha` is what makes grass appear.** A blend material carries two textures, and this value
/// per vertex is the mix between them — dirt at zero, grass at one. Without it a blended surface
/// can only show one of its two layers.
/// </remarks>
public static class BspDisplacements
{
    private const int LumpDispInfo = 26;
    private const int LumpDispVerts = 33;

    private const int DispInfoStride = 176;
    private const int DispVertStride = 20;

    private const int StartPositionOffset = 0;
    private const int VertexStartOffset = 12;
    private const int PowerOffset = 20;

    /// <summary>Smallest and largest subdivision a displacement may declare.</summary>
    /// <remarks>
    /// The engine allows 2 to 4, which is 5 to 17 vertices a side. Anything else is not a
    /// displacement, and a map is untrusted input (D32) — a power of 20 would ask for a million
    /// vertices from one face.
    /// </remarks>
    private const int MinimumPower = 2;
    private const int MaximumPower = 4;

    /// <summary>Reads one face's terrain, if it has any.</summary>
    /// <param name="file">The map's bytes.</param>
    /// <param name="surface">A surface whose <c>DisplacementIndex</c> is not -1.</param>
    /// <returns>The subdivided surface, or an empty list if the face is not a displacement.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <exception cref="InvalidDataException">The displacement's data is malformed.</exception>
    /// <remarks>
    /// Returns triangles rather than a grid, so the caller draws them exactly like any other
    /// surface. The texture and lightmap coordinates come from interpolating the base quad's,
    /// which is how the engine parameterises a displacement: the terrain follows the surface the
    /// mapper drew it on, so the texture does not swim as the ground rises.
    /// </remarks>
    public static IReadOnlyList<SurfaceVertex> ReadTriangles(
        ReadOnlyMemory<byte> file, BspSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!surface.IsDisplacement || surface.Vertices.Count != 4)
        {
            // A displacement is always built on a quad. Anything else is not one.
            return [];
        }

        BspHeader header = BspHeader.Parse(file.Span);

        ReadOnlySpan<byte> infos = BspLumpData
            .ReadStructures(file, header.Lump(LumpDispInfo), DispInfoStride, "dispinfo").Span;
        ReadOnlySpan<byte> vertices = BspLumpData
            .ReadStructures(file, header.Lump(LumpDispVerts), DispVertStride, "dispverts").Span;

        int infoCount = infos.Length / DispInfoStride;

        if (surface.DisplacementIndex >= infoCount)
        {
            throw new InvalidDataException(
                $"Face {surface.FaceIndex} names displacement {surface.DisplacementIndex} of {infoCount}.");
        }

        ReadOnlySpan<byte> info = infos.Slice(surface.DisplacementIndex * DispInfoStride, DispInfoStride);

        (float X, float Y, float Z) start = (
            BinaryPrimitives.ReadSingleLittleEndian(info[StartPositionOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(info[(StartPositionOffset + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(info[(StartPositionOffset + 8)..]));

        int vertexStart = BinaryPrimitives.ReadInt32LittleEndian(info[VertexStartOffset..]);
        int power = BinaryPrimitives.ReadInt32LittleEndian(info[PowerOffset..]);

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

                grid[(row * side) + column] = flat with
                {
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
