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
    private readonly ReadOnlyMemory<byte> _samples;

    /// <summary>`CPowerInfo::m_pTriInfos` by power, built once.</summary>
    private static readonly int[]?[] Triangles = new int[MaximumPower + 1][];

    /// <summary>`0x1800c0600`'s byte weight scale, `0.003921569`, as the binary writes it rather than `1/255`.</summary>
    private const float SampleWeight = 0.003921569f;

    private BspTerrain(ReadOnlyMemory<byte> infos, ReadOnlyMemory<byte> vertices, ReadOnlyMemory<byte> samples)
    {
        _infos = infos;
        _vertices = vertices;
        _samples = samples;
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
                file, header.Lump(BspLumpIndex.DispVerts), DispVertStride, "dispverts"),
            BspLumpData.ReadStructures(
                file, header.Lump(BspLumpIndex.DispLightmapSamplePositions), 1, "disp lightmap sample positions"));
    }

    /// <summary>`CPowerInfo::m_pTriInfos` for a power: three grid vertex indices per triangle, as `InitPowerInfoTriInfos_R` winds them.</summary>
    /// <param name="power">The displacement's power, 2 to 4.</param>
    /// <returns>The indices, <c>y · side + x</c>, two triangles per grid quad.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The power is outside 2 to 4.</exception>
    /// <remarks>
    /// `disp_powerinfo.cpp:319-370`: from the centre, into the four children (upper-right, upper-left, lower-left,
    /// lower-right, `g_ChildNodeIndexMul`) until a node one step from the grid, which winds the eight `g_TesselateVerts`
    /// round itself, each consecutive pair closing a triangle on the node. The engine reads these at `+0x28` of the power
    /// info (`0x1800c0600`), and vbsp's sample positions name them by index (`disp_vbsp.cpp:58-90`).
    /// </remarks>
    public static IReadOnlyList<int> SampleTriangles(int power)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(power, MinimumPower);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(power, MaximumPower);

        if (Triangles[power] is { } built)
        {
            return built;
        }

        ReadOnlySpan<(int X, int Y)> children = [(1, 1), (-1, 1), (-1, -1), (1, -1)];
        ReadOnlySpan<(int X, int Y)> winding = [(1, -1), (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1)];
        int side = (1 << power) + 1;
        List<int> triangles = new((1 << power) * (1 << power) * 6);

        Node(side / 2, side / 2, 0, children, winding);

        return Triangles[power] = [.. triangles];

        void Node(int x, int y, int level, ReadOnlySpan<(int X, int Y)> children, ReadOnlySpan<(int X, int Y)> winding)
        {
            if (level + 1 < power)
            {
                int step = 1 << (power - level - 2);

                foreach ((int cx, int cy) in children)
                {
                    Node(x + (cx * step), y + (cy * step), level + 1, children, winding);
                }

                return;
            }

            int first = -1;

            foreach ((int wx, int wy) in winding)
            {
                int vertex = ((y + wy) * side) + x + wx;

                if (first >= 0)
                {
                    triangles.Add(first);
                    triangles.Add(vertex);
                    triangles.Add((y * side) + x);
                }

                first = vertex;
            }
        }
    }

    /// <summary>`0x1800c0600`: each luxel's position from its sample record and the displacement's grid.</summary>
    /// <param name="samples">The displacement's records in lump 34, from its `m_iLightmapSamplePositionStart`.</param>
    /// <param name="count">How many luxels: the face's lightmap samples across times down.</param>
    /// <param name="power">The displacement's power.</param>
    /// <param name="grid">Its vertices, <c>y · side + x</c>.</param>
    /// <returns>A position per luxel, row by row; the origin for any the records run out before.</returns>
    /// <remarks>
    /// A record is a triangle byte — 255 escapes to <c>255 + the next byte</c> — and three byte weights, each times
    /// `0.003921569`, on the triangle's first, second and third vertex. A sample vbsp could place in no triangle is written
    /// as four zeros (`disp_vbsp.cpp:127-133`), so it reads as weight zero everywhere: the origin, as the engine reads it.
    /// </remarks>
    public static (float X, float Y, float Z)[] LuxelPositions(
        ReadOnlySpan<byte> samples, int count, int power, IReadOnlyList<(float X, float Y, float Z)> grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        IReadOnlyList<int> triangles = SampleTriangles(power);
        (float X, float Y, float Z)[] positions = new (float, float, float)[Math.Max(0, count)];
        int at = 0;

        for (int luxel = 0; luxel < positions.Length && at + 4 <= samples.Length; luxel++)
        {
            int triangle = samples[at];

            if (triangle == byte.MaxValue && at + 5 <= samples.Length)
            {
                at++;
                triangle = samples[at] + byte.MaxValue;
            }

            float w0 = samples[at + 1] * SampleWeight;
            float w1 = samples[at + 2] * SampleWeight;
            float w2 = samples[at + 3] * SampleWeight;

            at += 4;

            if ((triangle * 3) + 2 >= triangles.Count)
            {
                continue;
            }

            (float X, float Y, float Z) a = Vertex(triangles[triangle * 3]);
            (float X, float Y, float Z) b = Vertex(triangles[(triangle * 3) + 1]);
            (float X, float Y, float Z) c = Vertex(triangles[(triangle * 3) + 2]);

            positions[luxel] = (
                (w1 * b.X) + (w0 * a.X) + (w2 * c.X),
                (w1 * b.Y) + (w0 * a.Y) + (w2 * c.Y),
                (w1 * b.Z) + (w0 * a.Z) + (w2 * c.Z));
        }

        return positions;

        (float X, float Y, float Z) Vertex(int index) => index < grid.Count ? grid[index] : default;
    }

    /// <summary>Where each luxel of a displacement's lightmap sits in the world, as a dlight reads it (B425).</summary>
    /// <param name="surface">A displacement's parent face.</param>
    /// <returns>A position per luxel, row by row; empty for a face that is not a displacement or a map without the lump.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <exception cref="InvalidDataException">The displacement's data is malformed.</exception>
    public (float X, float Y, float Z)[] ReadLuxelPositions(BspSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (Grid(surface) is not { } built)
        {
            return [];
        }

        ReadOnlySpan<byte> info = _infos.Span.Slice(surface.DisplacementIndex * DispInfoStride, DispInfoStride);
        int start = BinaryPrimitives.ReadInt32LittleEndian(info[DispLightmapSamplePositionStartOffset..]);

        if (start < 0 || start >= _samples.Length)
        {
            return [];
        }

        (float X, float Y, float Z)[] grid = new (float, float, float)[built.Grid.Length];

        for (int index = 0; index < grid.Length; index++)
        {
            grid[index] = (built.Grid[index].X, built.Grid[index].Y, built.Grid[index].Z);
        }

        return LuxelPositions(
            _samples.Span[start..], Math.Max(1, surface.LuxelWidth) * Math.Max(1, surface.LuxelHeight), built.Power, grid);
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

        if (Grid(surface) is not { } built)
        {
            return [];
        }

        ReadOnlySpan<byte> info = _infos.Span.Slice(surface.DisplacementIndex * DispInfoStride, DispInfoStride);

        // **Valve's own tesselation, not a uniform grid, and the difference is where the ground
        // ENDS.** A displacement next to a coarser neighbour has edge vertices turned off in
        // `m_AllowedVerts` so its edge collapses onto the neighbour's and the two meet exactly;
        // spanning every quad regardless keeps them, and the fine edge then bulges away from the
        // straight line the coarse one draws. Measured before this: 26 columns on
        // `koth_harvest_final` where a ray fell through ground the map's own brush tree stops it
        // on, and a corpse seeded on one of them reached z -1015.
        //
        // **The same walk serves the renderer, which is Valve's arrangement rather than ours** —
        // *"This interface is shared betwixt VBSP and the engine. VBSP uses it to build the physics
        // mesh and the engine uses it to render"* (`disp_tesselate.h:174-175`). A renderer and a
        // collision mesh that tesselate one displacement differently disagree about where the
        // ground is, which is a defect wearing the clothes of an optimisation.
        List<int> indices = new((built.Side - 1) * (built.Side - 1) * 6);

        DisplacementTesselation.Build(built.Power, info[DispAllowedVertsOffset..], indices);

        List<SurfaceVertex> triangles = new(indices.Count);

        foreach (int index in indices)
        {
            triangles.Add(built.Grid[index]);
        }

        return triangles;
    }

    /// <summary>A displacement's full vertex grid, <c>y · side + x</c>, and its power; null for a face that is not one.</summary>
    private (SurfaceVertex[] Grid, int Power, int Side)? Grid(BspSurface surface)
    {
        if (!surface.IsDisplacement || surface.Vertices.Count != 4)
        {
            // A displacement is always built on a quad. Anything else is not one.
            return null;
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

        return (grid, power, side);
    }

    /// <summary>Reads one face's displacement as the engine's collision tree, and whether it is excluded from physics.</summary>
    /// <param name="surface">A surface whose <c>DisplacementIndex</c> is not -1.</param>
    /// <returns>
    /// The tree, and whether <c>SURF_NOPHYSICS_COLL</c> and <c>SURF_NORAY_COLL</c> (<c>0x8</c>) are set; null for a face that is not a
    /// displacement.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <exception cref="InvalidDataException">The displacement's data is malformed.</exception>
    /// <remarks>
    /// **The same corners, start corner first, and the same field as <see cref="ReadTriangles"/>**, handed to
    /// <see cref="DisplacementCollisionTree.Build"/>. The flags are <c>CCoreDispInfo::InitDispInfo</c>'s (`public/builddisp.cpp:762-768`):
    /// <c>minTess</c> with its high bit set carries <c>SURF_NOPHYSICS_COLL</c> (<c>0x2</c>, `builddisp.h:737`), which the engine's
    /// loader (`engine.dll` `FUN_18016f6d0`) tests before it makes a virtual mesh.
    /// </remarks>
    public (DisplacementCollisionTree Tree, bool NoPhysics, bool NoRay)? ReadCollisionTree(BspSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!surface.IsDisplacement || surface.Vertices.Count != 4)
        {
            return null;
        }

        if (surface.DisplacementIndex >= Count)
        {
            throw new InvalidDataException($"Face {surface.FaceIndex} names displacement {surface.DisplacementIndex} of {Count}.");
        }

        ReadOnlySpan<byte> info = _infos.Span.Slice(surface.DisplacementIndex * DispInfoStride, DispInfoStride);
        ReadOnlySpan<byte> vertices = _vertices.Span;

        (float X, float Y, float Z) start = (
            BinaryPrimitives.ReadSingleLittleEndian(info[DispStartPositionOffset..]),
            BinaryPrimitives.ReadSingleLittleEndian(info[(DispStartPositionOffset + 4)..]),
            BinaryPrimitives.ReadSingleLittleEndian(info[(DispStartPositionOffset + 8)..]));

        int vertexStart = BinaryPrimitives.ReadInt32LittleEndian(info[DispVertexStartOffset..]);
        int power = BinaryPrimitives.ReadInt32LittleEndian(info[DispPowerOffset..]);
        int minTess = BinaryPrimitives.ReadInt32LittleEndian(info[DispMinTessOffset..]);

        if (power is < MinimumPower or > MaximumPower)
        {
            throw new InvalidDataException($"Displacement {surface.DisplacementIndex} declares power {power}.");
        }

        int side = (1 << power) + 1;
        int needed = side * side;

        if (vertexStart < 0 || vertexStart + needed > vertices.Length / DispVertStride)
        {
            throw new InvalidDataException($"Displacement {surface.DisplacementIndex} needs vertices {vertexStart} to {vertexStart + needed}.");
        }

        SurfaceVertex[] corners = Rotate(surface.Vertices, start);
        (System.Numerics.Vector3 Direction, float Distance)[] field = new (System.Numerics.Vector3, float)[needed];

        for (int index = 0; index < needed; index++)
        {
            ReadOnlySpan<byte> vertex = vertices.Slice((vertexStart + index) * DispVertStride, DispVertStride);

            field[index] = (
                new System.Numerics.Vector3(
                    BinaryPrimitives.ReadSingleLittleEndian(vertex),
                    BinaryPrimitives.ReadSingleLittleEndian(vertex[4..]),
                    BinaryPrimitives.ReadSingleLittleEndian(vertex[8..])),
                BinaryPrimitives.ReadSingleLittleEndian(vertex[12..]));
        }

        DisplacementCollisionTree tree = DisplacementCollisionTree.Build(
            [.. Array.ConvertAll(corners, corner => new System.Numerics.Vector3(corner.X, corner.Y, corner.Z))], power, field);

        bool flagged = (minTess & int.MinValue) != 0;

        return (tree, flagged && (minTess & 0x2) != 0, flagged && (minTess & 0x8) != 0);
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
