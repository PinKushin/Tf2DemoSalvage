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
/// **Inside one displacement, a one-level box cull in the spirit of `CDispCollTree::AABBTree_SweepAABB`**
/// (`dispcoll_common.cpp:891`): a box per eight triangles, culled by the line rather than the
/// travel box (<see cref="Misses"/>, which names how it differs from Valve's tree). The profile that
/// asked for it was B667's: a light's visibility trace sweeping 37 triangles a ray on koth_toxic.
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

        List<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> read = [];

        foreach (BspSurface surface in surfaces)
        {
            if (!surface.IsDisplacement)
            {
                continue;
            }

            try
            {
                read.Add((surface.MaterialIndex, terrain.ReadTriangles(surface)));
            }
            catch (System.IO.InvalidDataException)
            {
                // Skipped rather than fatal, and deliberately not silent to a reader of this code:
                // the count is observable through `Count`, so a map that lost terrain says so.
                continue;
            }
        }

        return FromTriangles(read);
    }

    /// <summary>`DISP_ALPHA_PROP_DELTA` (`builddisp.h:25`), 382.5 over three alphas of 0–255, in alpha scaled to one.</summary>
    private const float SecondPropAlpha = 382.5f / 255f;

    /// <summary>Builds the set from each displacement's texdata and triangle-list corners.</summary>
    /// <param name="displacements">Each displacement: its material's texdata index and its corners, three per triangle.</param>
    /// <returns>The set; empty when none has a triangle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="displacements"/> is null.</exception>
    public static DisplacementCollision FromTriangles(
        IEnumerable<(int Texdata, IReadOnlyList<SurfaceVertex> Corners)> displacements)
    {
        ArgumentNullException.ThrowIfNull(displacements);

        List<Displacement> built = [];

        foreach ((int texdata, IReadOnlyList<SurfaceVertex>? corners) in displacements)
        {
            if (corners is null || corners.Count < 3)
            {
                continue;
            }

            DisplacementTriangle[] triangles = new DisplacementTriangle[corners.Count / 3];
            bool[] second = new bool[triangles.Length];

            for (int triangle = 0; triangle < triangles.Length; triangle++)
            {
                SurfaceVertex a = corners[triangle * 3];
                SurfaceVertex b = corners[(triangle * 3) + 1];
                SurfaceVertex c = corners[(triangle * 3) + 2];

                triangles[triangle] = DisplacementTriangle.From(
                    (a.X, a.Y, a.Z), (b.X, b.Y, b.Z), (c.X, c.Y, c.Z));

                // `CDispCollTree::Create`: `flTotalAlpha > DISP_ALPHA_PROP_DELTA` flags `DISPSURF_FLAG_SURFPROP2`.
                second[triangle] = a.Alpha + b.Alpha + c.Alpha > SecondPropAlpha;
            }

            built.Add(new Displacement(triangles, second, texdata));
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
    /// the sweep then reports clear. The group cull inside a displacement (<see cref="Misses"/>) keeps
    /// the rule: it only chooses which triangles are tested, and every one tested is handed the
    /// whole ray.
    /// </remarks>
    public float Sweep(
        float fromX, float fromY, float fromZ,
        float toX, float toY, float toZ,
        float halfExtent) =>
        SweepSurface(fromX, fromY, fromZ, toX, toY, toZ, halfExtent).Fraction;

    /// <summary><see cref="Sweep"/>, with the surface it stopped on — engine.dll `FUN_18016f290`'s half of `trace_t`.</summary>
    /// <param name="fromX">Where the box's centre starts.</param>
    /// <param name="fromY">Where the box's centre starts.</param>
    /// <param name="fromZ">Where the box's centre starts.</param>
    /// <param name="toX">Where it would end unobstructed.</param>
    /// <param name="toY">Where it would end unobstructed.</param>
    /// <param name="toZ">Where it would end unobstructed.</param>
    /// <param name="halfExtent">Half the box's width, on every axis.</param>
    /// <returns>
    /// The fraction; the struck displacement's texdata, −1 for none; and whether the struck triangle takes the material's
    /// second surfaceprop (`DISPSURF_FLAG_SURFPROP2`); and the struck triangle's plane, `trace.plane.normal` and `.dist`.
    /// </returns>
    public (float Fraction, int Texdata, bool SurfaceProp2, (float X, float Y, float Z) Normal, float Distance) SweepSurface(
        float fromX, float fromY, float fromZ,
        float toX, float toY, float toZ,
        float halfExtent) =>
        SweepSurface(fromX, fromY, fromZ, toX, toY, toZ, (halfExtent, halfExtent, halfExtent), BspLeafTree.MaskSolid);

    /// <summary><see cref="SweepSurface(float, float, float, float, float, float, float)"/> for a box with three extents and a mask.</summary>
    /// <param name="fromX">Where the box's centre starts.</param>
    /// <param name="fromY">Where the box's centre starts.</param>
    /// <param name="fromZ">Where the box's centre starts.</param>
    /// <param name="toX">Where it would end unobstructed.</param>
    /// <param name="toY">Where it would end unobstructed.</param>
    /// <param name="toZ">Where it would end unobstructed.</param>
    /// <param name="extents">Half the box's size on each axis — `Ray_t::m_Extents` (`cmodel.h:66`).</param>
    /// <param name="mask">The contents that stop it. Terrain is `CONTENTS_SOLID`, so a mask without that bit passes through.</param>
    /// <param name="limit">
    /// A fraction something else already stops the trace at — the brush trace's: only a shorter terrain hit is looked
    /// for, and the bound comes back, with no surface, when there is none. 1 for the whole ray.
    /// </param>
    /// <returns>As the cube's.</returns>
    /// <remarks>
    /// **The bound shrinks the travel box, which is the whole saving.** A 57,016-unit sky trace's box overlapped most of a
    /// map's displacements; the engine's world trace is read to reach only the displacements in the leaves the ray crosses
    /// before it stops (`CM_TraceToLeaf`) — **an inference from the shipped engine's shape, not read here: the SDK tree has no
    /// `cmodel.cpp`** (only the displacement side, `dispcoll_common.cpp`). A hit shorter than the bound lies inside the shortened box, so nothing it could return
    /// is culled (`DisplacementSweepLimitTests`).
    /// </remarks>
    public (float Fraction, int Texdata, bool SurfaceProp2, (float X, float Y, float Z) Normal, float Distance) SweepSurface(
        float fromX, float fromY, float fromZ,
        float toX, float toY, float toZ,
        (float X, float Y, float Z) extents,
        int mask,
        float limit = 1f)
    {
        SweepCounts counts = default;

        return SweepSurface(fromX, fromY, fromZ, toX, toY, toZ, extents, mask, limit, ref counts);
    }

    /// <summary>The sweep, counting what the group cull did — the seam its tests and the `trace-cost` probe read.</summary>
    internal (float Fraction, int Texdata, bool SurfaceProp2, (float X, float Y, float Z) Normal, float Distance) SweepSurface(
        float fromX, float fromY, float fromZ,
        float toX, float toY, float toZ,
        (float X, float Y, float Z) extents,
        int mask,
        float limit,
        ref SweepCounts counts)
    {
        if (_displacements.Length == 0 || (mask & BspLeafTree.ContentsSolid) == 0)
        {
            return (limit, -1, false, default, 0f);
        }

        (float X, float Y, float Z) start = (fromX, fromY, fromZ);
        (float X, float Y, float Z) delta = (toX - fromX, toY - fromY, toZ - fromZ);

        // The travel's own box up to the bound, grown by the sweeping box and a unit for the clip's epsilons, so a
        // displacement can be rejected without touching a triangle.
        float endX = limit < 1f ? fromX + (delta.X * limit) : toX;
        float endY = limit < 1f ? fromY + (delta.Y * limit) : toY;
        float endZ = limit < 1f ? fromZ + (delta.Z * limit) : toZ;
        float pad = limit < 1f ? BoundPad : 0f;
        float lowX = MathF.Min(fromX, endX) - extents.X - pad;
        float lowY = MathF.Min(fromY, endY) - extents.Y - pad;
        float lowZ = MathF.Min(fromZ, endZ) - extents.Z - pad;
        float highX = MathF.Max(fromX, endX) + extents.X + pad;
        float highY = MathF.Max(fromY, endY) + extents.Y + pad;
        float highZ = MathF.Max(fromZ, endZ) + extents.Z + pad;

        float hit = limit;
        int texdata = -1;
        bool second = false;
        (float X, float Y, float Z) normal = default;
        float distance = 0f;
        Line line = new(start, delta, extents);

        foreach (Displacement displacement in _displacements)
        {
            if (displacement.Bounds.MinX > highX || displacement.Bounds.MaxX < lowX ||
                displacement.Bounds.MinY > highY || displacement.Bounds.MaxY < lowY ||
                displacement.Bounds.MinZ > highZ || displacement.Bounds.MaxZ < lowZ)
            {
                continue;
            }

            for (int group = 0; group < displacement.Groups.Length; group++)
            {
                // Against the shortest hit so far, not the bound: only a shorter one can replace it.
                if (Misses(line, hit, displacement.Groups[group]))
                {
                    counts.GroupsCulled++;
                    continue;
                }

                int end = Math.Min((group + 1) * GroupSize, displacement.Triangles.Length);
                counts.GroupsSwept++;
                counts.TrianglesSwept += end - (group * GroupSize);

                for (int index = group * GroupSize; index < end; index++)
                {
                    float against = DisplacementSweep.Against(start, delta, extents, displacement.Triangles[index], hit);

                    if (against < hit)
                    {
                        hit = against;
                        texdata = displacement.Texdata;
                        second = displacement.SecondProp[index];
                        normal = displacement.Triangles[index].Normal;
                        distance = displacement.Triangles[index].Distance;
                    }
                }
            }
        }

        return (hit, texdata, second, normal, distance);
    }

    /// <summary>How far a bounded travel box is grown beyond the sweeping box: only ever adds candidates, which the bound then filters.</summary>
    private const float BoundPad = 1f;

    /// <summary>How far <see cref="Misses"/> grows a displacement's box: 32 times `DISPCOLL_DIST_EPSILON`, which is all a plane is moved by.</summary>
    private const float SegmentPad = 1f;

    /// <summary>`SweepAABBTriIntersect`'s window tolerance, the `0.001` in <see cref="DisplacementSweep.Against"/>.</summary>
    private const float WindowTolerance = 0.001f;

    /// <summary>How many consecutive triangles share one <see cref="Displacement.Groups"/> box.</summary>
    /// <remarks>
    /// **Assumes the triangle list is two to a quad in row order** (`BspTerrain.ReadTriangles`), so eight are four
    /// neighbouring quads in a strip — a box tight to that piece of the surface, where the displacement's own box spans
    /// every hill on it. Were the order different the boxes would be looser and cull less; never wrong, since a group's box
    /// is its own triangles' wherever they lie. **Eight is not evidence-backed**: four measured within this machine's noise
    /// of it (measurements/2026-11-02-b667-trace-cost.md).
    /// </remarks>
    internal const int GroupSize = 8;

    /// <summary>Whether the swept segment, short of <paramref name="best"/>, passes clear of a box — so no triangle inside it can stop it shorter.</summary>
    /// <remarks>
    /// **A one-level box cull in the spirit of `CDispCollTree::AABBTree_SweepAABB` (`dispcoll_common.cpp:891`), not a
    /// transcription of it** (B667). Without it a light's visibility trace swept every triangle of each displacement whose
    /// box it crossed, 37 a ray on koth_toxic, 58% of the trace's cost (measurements/2026-11-02-b667-trace-cost.md). It
    /// differs from Valve's in three ways, and the output is the same either way:
    /// - Valve's leaves hold two triangles in a quadtree of 2 × 2 quad blocks; ours are one level of eight triangles in row
    ///   order, a 4 × 1 strip.
    /// - Valve pads a node's box by the extents plus `DISPCOLL_DIST_EPSILON` (1/32); ours by the extents plus
    ///   <see cref="SegmentPad"/>, one unit.
    /// - Valve's `BuildRayLeafList` tests the whole ray; ours also skips a group entered at or past the shortest hit so far.
    ///
    /// **Why it can never cull a hit — arithmetic, not measured.** Each triangle lies inside its group's box, so the
    /// grown box's slab on every axis contains the triangle's axis-plane slab with <see cref="SegmentPad"/> to spare, more
    /// than the `DISPCOLL_DIST_EPSILON` <see cref="DisplacementSweep.Against"/> moves a plane by. Its entry on an axis is
    /// therefore no later, and its exit no earlier, than the axis planes'. `Against` returns a hit only when its window,
    /// entry the largest of the planes' and exit the smallest beside 1, is open within 0.001, and its entry is below
    /// <paramref name="best"/>. A box window shut by 0.001, or entered at or past <paramref name="best"/>, shuts that one
    /// too. An axis the travel does not move along culls only when the start is outside the grown slab, where `Against`'s
    /// two plane distances on that axis are both positive and it rejects.
    ///
    /// **The case that needs care: a start already inside a slab.** There the box's entry on that axis is negative, and it
    /// can be LATER than a strongly negative entry an edge plane gave `Against`, so "the box enters no later" fails for
    /// that axis. The cull is still safe: the box's entry is then below 0, which is below any <paramref name="best"/>, and
    /// the box's exit on every axis is above 0 whenever the triangle is not rejected, so neither test fires on it.
    ///
    /// **That leans on <paramref name="best"/> being above 0, so the case where it is not is stated here.** With a bound of
    /// 0, or once a hit at fraction 0 has been found, <paramref name="best"/> is 0, and a group entered at or after 0 is
    /// culled. Nothing is lost: `Against` returns a value below its `best` only for an entry below it, and an entry below 0
    /// comes back clamped to 0, which is not below 0 — so with a best of 0 no triangle, culled or swept, can replace it.
    /// The answer is the bound, or the fraction-0 hit already held. `DisplacementGroupCullTests`'
    /// `SweepSurface_ALimitOfZeroAndStartsOnABoxFace_EqualsTheBruteForceExactly` pins it.
    ///
    /// The ray's reciprocal and grown extents are worked out once a sweep (<see cref="Line"/>), so this multiplies where it
    /// would divide, as Valve's `vecInvDelta` does.
    /// </remarks>
    private static bool Misses(in Line line, float best, in Box box)
    {
        float enter = float.NegativeInfinity;
        float exit = float.PositiveInfinity;

        return Slab(line.Start.X, line.Inverse.X, line.Flat.X, box.MinX - line.Grown.X, box.MaxX + line.Grown.X, ref enter, ref exit) ||
            Slab(line.Start.Y, line.Inverse.Y, line.Flat.Y, box.MinY - line.Grown.Y, box.MaxY + line.Grown.Y, ref enter, ref exit) ||
            Slab(line.Start.Z, line.Inverse.Z, line.Flat.Z, box.MinZ - line.Grown.Z, box.MaxZ + line.Grown.Z, ref enter, ref exit) ||
            enter >= best ||
            enter >= MathF.Min(exit, 1f) + WindowTolerance;
    }

    /// <summary>Narrows the line's window to one axis's slab; true when the line never enters it.</summary>
    /// <remarks>
    /// A reciprocal that overflows to infinity can meet a zero distance and give NaN; NaN makes every later comparison
    /// false, so the group is swept rather than culled — the safe side.
    /// </remarks>
    private static bool Slab(float start, float inverse, bool flat, float low, float high, ref float enter, ref float exit)
    {
        if (flat)
        {
            return start < low || start > high;
        }

        float near = (low - start) * inverse;
        float far = (high - start) * inverse;

        enter = MathF.Max(enter, MathF.Min(near, far));
        exit = MathF.Min(exit, MathF.Max(near, far));

        return false;
    }

    /// <summary>One sweep's ray as <see cref="Misses"/> reads it, worked out once.</summary>
    private readonly struct Line((float X, float Y, float Z) start, (float X, float Y, float Z) delta, (float X, float Y, float Z) extents)
    {
        public (float X, float Y, float Z) Start { get; } = start;

        /// <summary>1 / delta per axis; unused where <see cref="Flat"/>.</summary>
        public (float X, float Y, float Z) Inverse { get; } = (1f / delta.X, 1f / delta.Y, 1f / delta.Z);

        /// <summary>Whether the ray does not move along each axis.</summary>
        public (bool X, bool Y, bool Z) Flat { get; } = (delta.X == 0f, delta.Y == 0f, delta.Z == 0f);

        /// <summary>How far a box is grown on each axis: the extents and <see cref="SegmentPad"/>.</summary>
        public (float X, float Y, float Z) Grown { get; } = (extents.X + SegmentPad, extents.Y + SegmentPad, extents.Z + SegmentPad);
    }

    /// <summary>An axis-aligned box.</summary>
    private readonly record struct Box(float MinX, float MinY, float MinZ, float MaxX, float MaxY, float MaxZ)
    {
        /// <summary>The box that holds nothing, which any point grows.</summary>
        public static Box Nothing { get; } = new(
            float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity,
            float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);

        /// <summary>This box grown to hold a point.</summary>
        public Box Holding((float X, float Y, float Z) point) => new(
            MathF.Min(MinX, point.X), MathF.Min(MinY, point.Y), MathF.Min(MinZ, point.Z),
            MathF.Max(MaxX, point.X), MathF.Max(MaxY, point.Y), MathF.Max(MaxZ, point.Z));

        /// <summary>This box grown to hold another.</summary>
        public Box Holding(in Box other) => Holding((other.MinX, other.MinY, other.MinZ)).Holding((other.MaxX, other.MaxY, other.MaxZ));
    }

    /// <summary>One displacement surface: its triangles and the box they live in.</summary>
    private sealed class Displacement
    {
        public Displacement(DisplacementTriangle[] triangles, bool[] secondProp, int texdata)
        {
            Triangles = triangles;
            SecondProp = secondProp;
            Texdata = texdata;
            Groups = new Box[(triangles.Length + GroupSize - 1) / GroupSize];

            Box bounds = Box.Nothing;

            for (int group = 0; group < Groups.Length; group++)
            {
                Box box = Box.Nothing;

                for (int index = group * GroupSize; index < Math.Min((group + 1) * GroupSize, triangles.Length); index++)
                {
                    box = box.Holding(triangles[index].A).Holding(triangles[index].B).Holding(triangles[index].C);
                }

                Groups[group] = box;
                bounds = bounds.Holding(box);
            }

            Bounds = bounds;
        }

        public DisplacementTriangle[] Triangles { get; }

        /// <summary>The box of each run of <see cref="GroupSize"/> triangles, in order.</summary>
        public Box[] Groups { get; }

        /// <summary>Every triangle's box: the union of <see cref="Groups"/>.</summary>
        public Box Bounds { get; }

        /// <summary>Per triangle, `DISPSURF_FLAG_SURFPROP2`.</summary>
        public bool[] SecondProp { get; }

        /// <summary>The face's texdata, which carries its material's two surfaceprops.</summary>
        public int Texdata { get; }
    }
}

/// <summary>What one sweep's group cull did (<see cref="DisplacementCollision"/>): the seam its tests and the probe read.</summary>
/// <param name="GroupsCulled">Groups skipped by their box.</param>
/// <param name="GroupsSwept">Groups whose triangles were swept.</param>
/// <param name="TrianglesSwept">Triangles handed to <see cref="DisplacementSweep.Against"/>.</param>
internal record struct SweepCounts(int GroupsCulled, int GroupsSwept, int TrianglesSwept);
