using System;

namespace Tf2DemoSalvage.Content.Bsp;

/// <summary>One collision triangle of a displacement surface, with its plane.</summary>
/// <param name="A">First vertex.</param>
/// <param name="B">Second vertex.</param>
/// <param name="C">Third vertex.</param>
/// <param name="Normal">The face normal, unit length.</param>
/// <param name="Distance">The plane distance, so the plane is <c>Normal · p = Distance</c>.</param>
/// <remarks>
/// **Valve's <c>CDispCollTri</c>** (<c>dispcoll_common.h</c>), which likewise carries the normal and
/// distance beside the vertex indices rather than recomputing them per test. Kept as values here
/// because a displacement's triangles are read from the lumps and never edited.
/// </remarks>
public readonly record struct DisplacementTriangle(
    (float X, float Y, float Z) A,
    (float X, float Y, float Z) B,
    (float X, float Y, float Z) C,
    (float X, float Y, float Z) Normal,
    float Distance)
{
    /// <summary>Builds a triangle and its plane from three vertices.</summary>
    /// <param name="a">First vertex.</param>
    /// <param name="b">Second vertex.</param>
    /// <param name="c">Third vertex.</param>
    /// <returns>The triangle.</returns>
    /// <remarks>
    /// **Winding decides which way the surface faces**, and the sweep is one-sided — a box moving
    /// along the normal passes straight through. So a triangle built the other way round is not
    /// merely mirrored, it is invisible from above.
    ///
    /// A degenerate triangle keeps a zero normal rather than producing NaNs from the normalise.
    /// `Against` then rejects it at the first plane, which is what the engine does with one too:
    /// every edge plane comes back undefined and the face plane cannot separate anything.
    /// </remarks>
    public static DisplacementTriangle From(
        (float X, float Y, float Z) a, (float X, float Y, float Z) b, (float X, float Y, float Z) c)
    {
        (float X, float Y, float Z) first = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        (float X, float Y, float Z) second = (c.X - a.X, c.Y - a.Y, c.Z - a.Z);

        (float X, float Y, float Z) normal = (
            (first.Y * second.Z) - (first.Z * second.Y),
            (first.Z * second.X) - (first.X * second.Z),
            (first.X * second.Y) - (first.Y * second.X));

        float length = MathF.Sqrt((normal.X * normal.X) + (normal.Y * normal.Y) + (normal.Z * normal.Z));

        if (length > 0f)
        {
            normal = (normal.X / length, normal.Y / length, normal.Z / length);
        }

        return new DisplacementTriangle(
            a, b, c, normal, (normal.X * a.X) + (normal.Y * a.Y) + (normal.Z * a.Z));
    }
}

/// <summary>
/// Sweeps an axis-aligned box along a ray against a displacement triangle.
/// </summary>
/// <remarks>
/// **<c>CDispCollTree::SweepAABBTriIntersect</c>, <c>public/dispcoll_common.cpp:1331</c>**, which is
/// how terrain stops anything in Source. `BspLeafTree` already walks BRUSHES; displacements are not
/// brushes and are invisible to that trace, so on a map with displacement ground — which is most
/// TF2 maps — a chase camera passes through the hillside behind a player.
///
/// **There are TWO files in the SDK and this is the newer one.** `public/dispcoll.cpp` has a
/// `SweptAABBTriIntersect`; this is `SweepAABBTriIntersect`, one letter apart, and it is the one
/// belonging to the `CDispCollTree` that carries `AABBTree_SweepAABB`. A previous handoff cited the
/// older file.
///
/// **The shape is a swept separating-axis test over fifteen planes**, accumulating a window rather
/// than a single hit: six from the triangle's axis-aligned bounds, nine from each edge crossed with
/// each axis, and the face plane. Each pushes the entry fraction forward or pulls the exit fraction
/// back; a plane the swept box is wholly in front of rejects the triangle outright.
///
/// **Every plane is expanded by the box's extents** rather than the box being tested as a volume,
/// which is what reduces a swept-box test to a ray against an extruded triangle. Valve's comment
/// says so where `EdgeCrossAxis` does it.
///
/// **The nine edge planes are precomputed in the engine** (`Cache_Create`, into `m_aTrisCache`) and
/// are computed per call here. That is the optimisation deliberately left until last: correctness
/// without it is testable, and a cache built before the maths is right is a cache of wrong planes.
/// </remarks>
public static class DisplacementSweep
{
    /// <summary><c>DISPCOLL_DIST_EPSILON</c>, <c>dispcoll_common.h:34</c>.</summary>
    /// <remarks>
    /// The same 1/32 as the brush trace's <c>DIST_EPSILON</c>, and used the same way: subtracted on
    /// entry so a sweep stops just short of the surface rather than exactly on it, which is what
    /// keeps the next frame's trace from starting solid.
    /// </remarks>
    public const float DistanceEpsilon = 0.03125f;

    /// <summary><c>DISPCOLL_INVALID_FRAC</c>, <c>dispcoll_common.h:37</c>.</summary>
    /// <remarks>
    /// **A sentinel rather than zero, and the difference is load-bearing.** The entry fraction is
    /// raised by `t > start`, so it has to begin below any legitimate `t` — including negative ones,
    /// which a box already overlapping the triangle produces. Starting it at zero would silently
    /// treat "never entered" as "entered at the very beginning".
    /// </remarks>
    public const float InvalidFraction = -99999.9f;

    /// <summary>Sweeps a box along a ray against one triangle.</summary>
    /// <param name="start">Where the box's centre begins.</param>
    /// <param name="delta">How far and in which direction it travels.</param>
    /// <param name="extents">Half the box's size on each axis.</param>
    /// <param name="triangle">The triangle to stop against.</param>
    /// <param name="best">The shortest fraction found so far; only a shorter one is returned.</param>
    /// <returns>The fraction of the way along <paramref name="delta"/>, or <paramref name="best"/>.</returns>
    public static float Against(
        (float X, float Y, float Z) start,
        (float X, float Y, float Z) delta,
        (float X, float Y, float Z) extents,
        in DisplacementTriangle triangle,
        float best)
    {
        // **"Make sure objects are traveling toward one another."** A displacement surface is
        // one-sided for a sweep, so a box already under the ground and moving up goes through it —
        // without this a player climbing a slope would catch on its underside.
        float along = (triangle.Normal.X * delta.X)
            + (triangle.Normal.Y * delta.Y)
            + (triangle.Normal.Z * delta.Z);

        if (along > DistanceEpsilon)
        {
            return best;
        }

        float entry = InvalidFraction;
        float exit = 1f;

        // **Valve tests `m_flStartFrac != DISPCOLL_INVALID_FRAC` at the end; this carries a flag
        // instead, and the two are the same claim.** The sentinel is kept because the ORDERING
        // depends on it — every plane raises the entry with `t > entry`, so it has to start below
        // any legitimate `t`, including the negative ones an already-overlapping box produces. What
        // the flag replaces is only the final float-equality comparison, which S1244 rightly objects
        // to: a value compared for exact equality is a value one arithmetic change away from never
        // matching.
        bool entered = false;

        if (!AxisPlanes(start, delta, extents, triangle, ref entry, ref exit, ref entered))
        {
            return best;
        }

        if (!EdgePlanes(start, delta, extents, triangle, ref entry, ref exit, ref entered))
        {
            return best;
        }

        if (!FacePlane(start, delta, extents, triangle, ref entry, ref exit, ref entered))
        {
            return best;
        }

        // **The `0.001` tolerance is Valve's and is not slack for its own sake.** A box that grazes
        // a surface produces an entry and an exit that cross by a hair through floating point, and
        // rejecting those would drop single triangles out of a continuous surface — a hole to fall
        // through on otherwise solid ground.
        if (entry >= exit && MathF.Abs(entry - exit) >= 0.001f)
        {
            return best;
        }

        if (!entered || entry >= best)
        {
            return best;
        }

        // "Clamp -- shouldn't really ever be here!???" — Valve's comment, kept because the case is
        // real: a box that already overlaps the triangle enters at a negative fraction.
        return entry < 0f ? 0f : entry;
    }

    /// <summary>Six planes from the triangle's own axis-aligned bounds.</summary>
    /// <remarks><c>CDispCollTree::AxisPlanesXYZ</c>, <c>dispcoll_common.cpp:995</c>.</remarks>
    private static bool AxisPlanes(
        (float X, float Y, float Z) start,
        (float X, float Y, float Z) delta,
        (float X, float Y, float Z) extents,
        in DisplacementTriangle triangle,
        ref float entry,
        ref float exit,
        ref bool entered)
    {
        for (int axis = 2; axis >= 0; axis--)
        {
            float rayStart = Component(start, axis);
            float rayExtent = Component(extents, axis);
            float rayDelta = Component(delta, axis);

            float low = MathF.Min(
                Component(triangle.A, axis),
                MathF.Min(Component(triangle.B, axis), Component(triangle.C, axis)));

            float high = MathF.Max(
                Component(triangle.A, axis),
                MathF.Max(Component(triangle.B, axis), Component(triangle.C, axis)));

            // Minimum side. The engine keeps per-triangle min/max vertex indices for this; taking
            // the extreme of the three coordinates is the same number without the bookkeeping.
            float startDistance = (low - rayExtent) - rayStart;

            if (!Resolve(startDistance, startDistance - rayDelta, ref entry, ref exit, ref entered))
            {
                return false;
            }

            // Maximum side.
            startDistance = rayStart - (high + rayExtent);

            if (!Resolve(startDistance, startDistance + rayDelta, ref entry, ref exit, ref entered))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Nine planes: each edge crossed with each axis.</summary>
    /// <remarks>
    /// **<c>Cache_EdgeCrossAxis*</c> and <c>EdgeCrossAxis&lt;AXIS&gt;</c>**, computed here rather
    /// than cached. Each is a two-dimensional test in the other two axes, because crossing an edge
    /// with an axis leaves a normal with nothing in that axis.
    ///
    /// **The vertex arguments are Valve's exactly**, and getting them wrong is silent: edge 1 is
    /// `verts[1] - verts[0]` with `verts[0]` on the edge and `verts[2]` off it, edge 2 is
    /// `verts[2] - verts[1]` off `verts[0]`, edge 3 is `verts[0] - verts[2]` off `verts[1]`. The
    /// off-edge vertex only decides which way the plane faces, so a wrong one still produces a
    /// plausible plane pointing the wrong way.
    /// </remarks>
    private static bool EdgePlanes(
        (float X, float Y, float Z) start,
        (float X, float Y, float Z) delta,
        (float X, float Y, float Z) extents,
        in DisplacementTriangle triangle,
        ref float entry,
        ref float exit,
        ref bool entered)
    {
        (float X, float Y, float Z)[] on = [triangle.A, triangle.B, triangle.C];
        (float X, float Y, float Z)[] off = [triangle.C, triangle.A, triangle.B];

        (float X, float Y, float Z)[] edges =
        [
            (triangle.B.X - triangle.A.X, triangle.B.Y - triangle.A.Y, triangle.B.Z - triangle.A.Z),
            (triangle.C.X - triangle.B.X, triangle.C.Y - triangle.B.Y, triangle.C.Z - triangle.B.Z),
            (triangle.A.X - triangle.C.X, triangle.A.Y - triangle.C.Y, triangle.A.Z - triangle.C.Z),
        ];

        for (int edge = 0; edge < 3; edge++)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                if (!EdgeCrossAxis(
                    start, delta, extents, edges[edge], on[edge], off[edge], axis,
                    ref entry, ref exit, ref entered))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>One edge crossed with one axis.</summary>
    private static bool EdgeCrossAxis(
        (float X, float Y, float Z) start,
        (float X, float Y, float Z) delta,
        (float X, float Y, float Z) extents,
        (float X, float Y, float Z) edge,
        (float X, float Y, float Z) onEdge,
        (float X, float Y, float Z) offEdge,
        int axis,
        ref float entry,
        ref float exit,
        ref bool entered)
    {
        int first = (axis + 1) % 3;
        int second = (axis + 2) % 3;

        // edge x axisX = ( 0, edgeZ, -edgeY ); the same pattern rotated for Y and Z.
        float normalFirst = -Component(edge, second);
        float normalSecond = Component(edge, first);

        float length = MathF.Sqrt((normalFirst * normalFirst) + (normalSecond * normalSecond));

        // **"Check for zero length normals."** Valve tests the two live components against zero
        // individually, which rejects more than a zero-length normal does: an edge parallel to one
        // of the other axes leaves one component exactly zero, and its plane is then the axis plane
        // already tested above. An axis-aligned triangle has eight of its nine edge planes rejected
        // this way, and treating them as real would double-count.
        if (length == 0f)
        {
            return true;
        }

        normalFirst /= length;
        normalSecond /= length;

        if (normalFirst == 0f || normalSecond == 0f)
        {
            return true;
        }

        float distance = (normalFirst * Component(onEdge, first)) + (normalSecond * Component(onEdge, second));
        float offDistance = (normalFirst * Component(offEdge, first)) + (normalSecond * Component(offEdge, second));

        // "Adjust plane facing - triangle should be behind the plane."
        if (!(MathF.Abs(offDistance - distance) < DistanceEpsilon) && offDistance > distance)
        {
            distance = -distance;
            normalFirst = -normalFirst;
            normalSecond = -normalSecond;
        }

        float extentFirst = normalFirst < 0f ? Component(extents, first) : -Component(extents, first);
        float extentSecond = normalSecond < 0f ? Component(extents, second) : -Component(extents, second);

        float expanded = distance - ((normalFirst * extentFirst) + (normalSecond * extentSecond));

        float startDistance =
            (normalFirst * Component(start, first)) + (normalSecond * Component(start, second)) - expanded;

        float endDistance =
            (normalFirst * (Component(start, first) + Component(delta, first)))
            + (normalSecond * (Component(start, second) + Component(delta, second)))
            - expanded;

        return Resolve(startDistance, endDistance, ref entry, ref exit, ref entered);
    }

    /// <summary>The triangle's own plane.</summary>
    /// <remarks><c>CDispCollTree::FacePlane</c>, <c>dispcoll_common.cpp:978</c>.</remarks>
    private static bool FacePlane(
        (float X, float Y, float Z) start,
        (float X, float Y, float Z) delta,
        (float X, float Y, float Z) extents,
        in DisplacementTriangle triangle,
        ref float entry,
        ref float exit,
        ref bool entered)
    {
        // CalcClosestExtents: the corner of the box nearest the plane, which is the negative extent
        // on every axis the normal points along.
        float closest =
            (triangle.Normal.X * (triangle.Normal.X < 0f ? extents.X : -extents.X))
            + (triangle.Normal.Y * (triangle.Normal.Y < 0f ? extents.Y : -extents.Y))
            + (triangle.Normal.Z * (triangle.Normal.Z < 0f ? extents.Z : -extents.Z));

        float expanded = triangle.Distance - closest;

        float startDistance =
            (triangle.Normal.X * start.X)
            + (triangle.Normal.Y * start.Y)
            + (triangle.Normal.Z * start.Z)
            - expanded;

        float endDistance =
            (triangle.Normal.X * (start.X + delta.X))
            + (triangle.Normal.Y * (start.Y + delta.Y))
            + (triangle.Normal.Z * (start.Z + delta.Z))
            - expanded;

        return Resolve(startDistance, endDistance, ref entry, ref exit, ref entered);
    }

    /// <summary>Folds one plane into the entry/exit window.</summary>
    /// <remarks>
    /// **<c>CDispCollTree::ResolveRayPlaneIntersect</c>, <c>dispcoll_common.cpp:940</c>**, and the
    /// whole algorithm turns on its four cases:
    ///
    /// <code>
    ///   if( ( flStart > 0.0f ) &amp;&amp; ( flEnd > 0.0f ) ) return false;   // wholly in front: reject
    ///   if( ( flStart &lt; 0.0f ) &amp;&amp; ( flEnd &lt; 0.0f ) ) return true;    // wholly behind: no constraint
    /// </code>
    ///
    /// then an entry (front to back) raises the entry fraction and an exit lowers the exit one. Note
    /// the epsilon is SUBTRACTED on entry and ADDED on exit, so the window closes slightly from both
    /// ends.
    ///
    /// **A zero denominator yields t = 0 rather than an infinity**, which is Valve's
    /// `bDenomIsZero` — a plane exactly parallel to the travel, where the box neither enters nor
    /// leaves.
    /// </remarks>
    private static bool Resolve(
        float startDistance, float endDistance, ref float entry, ref float exit, ref bool entered)
    {
        if (startDistance > 0f && endDistance > 0f)
        {
            return false;
        }

        if (startDistance < 0f && endDistance < 0f)
        {
            return true;
        }

        float denominator = startDistance - endDistance;
        bool flat = denominator == 0f;

        if (startDistance >= 0f && endDistance <= 0f)
        {
            float t = flat ? 0f : (startDistance - DistanceEpsilon) / denominator;

            if (t > entry)
            {
                entry = t;
                entered = true;
            }
        }
        else
        {
            float t = flat ? 0f : (startDistance + DistanceEpsilon) / denominator;

            if (t < exit)
            {
                exit = t;
            }
        }

        return true;
    }

    /// <summary>One component of a vector by index, matching Valve's <c>Vector::operator[]</c>.</summary>
    private static float Component((float X, float Y, float Z) vector, int axis) => axis switch
    {
        0 => vector.X,
        1 => vector.Y,
        _ => vector.Z,
    };
}
