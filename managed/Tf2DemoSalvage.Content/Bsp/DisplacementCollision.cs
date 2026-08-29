using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>
/// Every displacement surface on a map, as something a swept box can be stopped by.
/// </summary>
/// <remarks>
/// **The narrowing is by BOUNDS, and that is a measured correction to the plan** (B227). The
/// previous handoff's step 2 was *"leaf → LUMP_LEAFFACES → faces → dispinfo"*, and on `cp_badlands`
/// that route reaches **none** of the 1191 displacement faces — with 12,654 flat faces reached by
/// the same walk as the control, so the walk works and the absence is real.
/// `LeafDisplacementReachTests` pins the measurement.
///
/// It makes sense once seen: a displacement's base quad is not the terrain. The real surface is a
/// heightfield bulging out of that quad, often well outside it, so the compiler has no single leaf
/// to file the face under. `vrad` builds its own displacement list rather than using leaves for the
/// same reason, and the engine's own `cmodel_disp.cpp` — which would say so outright — is not
/// published.
///
/// **So each displacement carries a box, and a sweep tests the ones its own travel could touch.**
/// That is cheap enough to need nothing cleverer here: `cp_badlands` has 1191 of them, and a
/// chase camera's sweep is 96 units, so all but a handful are rejected by a box comparison.
///
/// **Built at load rather than lazily**, which is what `CDispCollTree` does: the engine builds every
/// displacement's collision tree when the map loads. Deferring it would make a read into a write
/// (`docs/memory/a-lazy-cache-makes-reading-a-write.md`) on a path the render loop calls.
///
/// **The per-displacement AABB tree Valve walks INSIDE one displacement is still not here**
/// (`CDispCollTree::AABBTree_*`). That one is an optimisation over a few hundred triangles, and this
/// rejects whole displacements before reaching them; it is the right thing to add when a profile
/// says so, and no earlier.
/// </remarks>
public sealed class DisplacementCollision
{
    private readonly Displacement[] _displacements;

    private DisplacementCollision(Displacement[] displacements) => _displacements = displacements;

    /// <summary>An empty set, for a map with no terrain or none read.</summary>
    public static DisplacementCollision Empty { get; } = new([]);

    /// <summary>How many displacement surfaces are held.</summary>
    public int Count => _displacements.Length;

    /// <summary>How many collision triangles they come to in total.</summary>
    public int TriangleCount
    {
        get
        {
            int total = 0;

            foreach (Displacement displacement in _displacements)
            {
                total += displacement.Triangles.Length;
            }

            return total;
        }
    }

    /// <summary>Builds the collision set from a map's surfaces and its terrain.</summary>
    /// <param name="surfaces">Every face, in face order.</param>
    /// <param name="terrain">The displacement lumps.</param>
    /// <returns>The set; empty when the map has no displacements.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Takes what the map loader already read rather than re-reading the file.** Building the
    /// triangles is the expensive part and the renderer needs the same ones, so a second pass here
    /// would double it — the shape `docs/memory/per-item-apis-hide-quadratic-reads.md` is about.
    ///
    /// A displacement whose data will not read is skipped rather than throwing: one malformed
    /// surface must not leave a whole map with no terrain collision at all, which would be a far
    /// larger and much less visible failure.
    /// </remarks>
    public static DisplacementCollision From(
        IReadOnlyList<BspSurface> surfaces, BspTerrain terrain)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(terrain);

        List<Displacement> built = [];

        foreach (BspSurface surface in surfaces)
        {
            if (!surface.IsDisplacement)
            {
                continue;
            }

            IReadOnlyList<SurfaceVertex> corners;

            try
            {
                corners = terrain.ReadTriangles(surface);
            }
            catch (System.IO.InvalidDataException)
            {
                // Skipped rather than fatal, and deliberately not silent to a reader of this code:
                // the count is observable through `Count`, so a map that lost terrain says so.
                continue;
            }

            if (corners.Count < 3)
            {
                continue;
            }

            DisplacementTriangle[] triangles = new DisplacementTriangle[corners.Count / 3];

            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            float maxZ = float.NegativeInfinity;

            for (int triangle = 0; triangle < triangles.Length; triangle++)
            {
                SurfaceVertex a = corners[triangle * 3];
                SurfaceVertex b = corners[(triangle * 3) + 1];
                SurfaceVertex c = corners[(triangle * 3) + 2];

                triangles[triangle] = DisplacementTriangle.From(
                    (a.X, a.Y, a.Z), (b.X, b.Y, b.Z), (c.X, c.Y, c.Z));

                foreach (SurfaceVertex vertex in (SurfaceVertex[])[a, b, c])
                {
                    minX = MathF.Min(minX, vertex.X);
                    minY = MathF.Min(minY, vertex.Y);
                    minZ = MathF.Min(minZ, vertex.Z);
                    maxX = MathF.Max(maxX, vertex.X);
                    maxY = MathF.Max(maxY, vertex.Y);
                    maxZ = MathF.Max(maxZ, vertex.Z);
                }
            }

            built.Add(new Displacement(triangles, minX, minY, minZ, maxX, maxY, maxZ));
        }

        return built.Count == 0 ? Empty : new DisplacementCollision([.. built]);
    }

    /// <summary>Sweeps a box through the terrain and says how far it got.</summary>
    /// <param name="fromX">Where the box's centre starts.</param>
    /// <param name="fromY">Where the box's centre starts.</param>
    /// <param name="fromZ">Where the box's centre starts.</param>
    /// <param name="toX">Where it would end unobstructed.</param>
    /// <param name="toY">Where it would end unobstructed.</param>
    /// <param name="toZ">Where it would end unobstructed.</param>
    /// <param name="halfExtent">Half the box's width, on every axis.</param>
    /// <returns>The fraction of the way it got, 1 when nothing stopped it.</returns>
    /// <remarks>
    /// **The WHOLE ray, every time, which is the trap the brush trace already paid for.**
    /// `CM_TraceToLeaf` clips the entire trace and the tree walk only chooses candidates; handing a
    /// clip a sub-segment finds no entry, because the piece already begins inside the surface, and
    /// the sweep then reports clear. There is no tree walk here at all, so the rule is easy to keep
    /// — it is written down because the next person adding one will need it.
    /// </remarks>
    public float Sweep(
        float fromX, float fromY, float fromZ,
        float toX, float toY, float toZ,
        float halfExtent)
    {
        if (_displacements.Length == 0)
        {
            return 1f;
        }

        // The travel's own box, grown by the sweeping box, so a displacement can be rejected without
        // touching a triangle.
        float lowX = MathF.Min(fromX, toX) - halfExtent;
        float lowY = MathF.Min(fromY, toY) - halfExtent;
        float lowZ = MathF.Min(fromZ, toZ) - halfExtent;
        float highX = MathF.Max(fromX, toX) + halfExtent;
        float highY = MathF.Max(fromY, toY) + halfExtent;
        float highZ = MathF.Max(fromZ, toZ) + halfExtent;

        (float X, float Y, float Z) start = (fromX, fromY, fromZ);
        (float X, float Y, float Z) delta = (toX - fromX, toY - fromY, toZ - fromZ);
        (float X, float Y, float Z) extents = (halfExtent, halfExtent, halfExtent);

        float hit = 1f;

        foreach (Displacement displacement in _displacements)
        {
            if (displacement.MinX > highX || displacement.MaxX < lowX ||
                displacement.MinY > highY || displacement.MaxY < lowY ||
                displacement.MinZ > highZ || displacement.MaxZ < lowZ)
            {
                continue;
            }

            foreach (DisplacementTriangle triangle in displacement.Triangles)
            {
                hit = DisplacementSweep.Against(start, delta, extents, triangle, hit);
            }
        }

        return hit;
    }

    /// <summary>One displacement surface: its triangles and the box they live in.</summary>
    private sealed class Displacement(
        DisplacementTriangle[] triangles,
        float minX, float minY, float minZ,
        float maxX, float maxY, float maxZ)
    {
        public DisplacementTriangle[] Triangles { get; } = triangles;

        public float MinX { get; } = minX;

        public float MinY { get; } = minY;

        public float MinZ { get; } = minZ;

        public float MaxX { get; } = maxX;

        public float MaxY { get; } = maxY;

        public float MaxZ { get; } = maxZ;
    }
}
