using System;
using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>One convex piece of static world, ready to be collided against.</summary>
/// <param name="Center">The centre of its bounding sphere, in Source units.</param>
/// <param name="Radius">That sphere's radius.</param>
/// <param name="Planes">Its faces as <c>(normal, distance)</c>, outward-facing.</param>
/// <remarks>
/// **Planes rather than triangles, because a ledge is CONVEX and that is the whole point of it.**
/// IVP's own narrow phase walks the half-edge structure to find a closest feature; the geometry it
/// is walking is a convex polyhedron, and a convex polyhedron is the intersection of its face
/// half-spaces. Deriving the planes once at load turns every later test into dot products.
/// </remarks>
public readonly record struct IvpWorldLedge(
    Vector3 Center, float Radius, IReadOnlyList<(Vector3 Normal, float Distance)> Planes);

/// <summary>
/// The static map, as the thing a corpse lands on (B58).
/// </summary>
/// <remarks>
/// **The world is an ordinary body with one bit set, and that is read from the engine rather than
/// assumed** — `docs/findings/51`, *The static world is an ordinary body with one bit set*. The
/// contact builder zeroes a body's mass and inertia contribution on `core+0x0 &amp; 2`, and the island
/// driver skips integrating a core on the same bit; `CreatePolyObjectStatic` takes the identical
/// `CPhysCollide *` as the moving variant, so there is no separate world path at the API boundary
/// either. Here that is expressed by the world having no body at all: it contributes nothing to any
/// effective mass and never moves, which is the same arithmetic.
///
/// **The broadphase is Valve's own.** Each ledge carries the minimal bounding sphere its ledge-tree
/// node stores — measured, with a wrong-offset control, in `docs/findings/51`. Nothing here invents
/// a bound.
///
/// **What is implemented is the vertex-face case, and that is a STATED departure.** IVP dispatches
/// four narrow-phase routines out of `ivp_mindist_event.cxx` — inferred to be the classic
/// vertex-vertex, vertex-edge, edge-edge and vertex-face pairs. A corpse resting on a floor,
/// sliding down a slope, or piling on another is vertex-face in nearly every contact; edge-edge is
/// what a limb crossing a railing needs, and it is absent rather than approximated. **The symptom
/// if it matters: a thin edge can pass through a thin edge.** It is written down here so it does
/// not read as deliberate completeness.
///
/// **Units cross here and nowhere else.** Hull points are in IVP metres and this simulation runs in
/// Source units, deliberately — `RagdollSimulation` states why. The conversion is one multiply at
/// load, at the one seam that knows both conventions ([[ivp-is-a-third-convention]]).
/// </remarks>
public sealed class IvpWorldCollision
{
    /// <summary>Source units per IVP metre — <c>1 / METERS_PER_INCH</c>.</summary>
    /// <remarks>
    /// **Valve's own constant, inverted.** `CPhysicsEnvironment` converts the other way at its
    /// boundary; this project's simulation stays in Source units, so the hull comes to it.
    /// </remarks>
    public const float SourceUnitsPerMetre = 1f / 0.0254f;

    private readonly List<IvpWorldLedge> _ledges = [];

    /// <summary>Ledge indices by grid cell — the broadphase.</summary>
    private readonly Dictionary<(int X, int Y, int Z), List<int>> _grid = [];

    /// <summary>Ledges too large to file, tested against everything.</summary>
    private readonly List<int> _oversized = [];

    /// <summary>Every convex piece of the world.</summary>
    public IReadOnlyList<IvpWorldLedge> Ledges => _ledges;

    /// <summary>Adds one ledge, converting it from IVP metres into Source units.</summary>
    /// <param name="points">The ledge's points, in metres.</param>
    /// <param name="triangles">Its triangles, indexing those points.</param>
    /// <param name="center">Its node's bounding-sphere centre, in metres.</param>
    /// <param name="radius">That sphere's radius, in metres.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **Duplicate planes are collapsed**, because a ledge's faces are triangulated: a floor quad
    /// arrives as two triangles with the same plane, and keeping both would count one contact twice
    /// and push a corpse off the ground at double strength.
    /// </remarks>
    public void Add(
        IReadOnlyList<Vector3> points,
        IReadOnlyList<(int A, int B, int C)> triangles,
        Vector3 center,
        float radius)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(triangles);

        List<(Vector3 Normal, float Distance)> planes = [];

        foreach ((int a, int b, int c) in triangles)
        {
            if (a < 0 || b < 0 || c < 0 ||
                a >= points.Count || b >= points.Count || c >= points.Count)
            {
                continue;
            }

            Vector3 first = points[a] * SourceUnitsPerMetre;
            Vector3 second = points[b] * SourceUnitsPerMetre;
            Vector3 third = points[c] * SourceUnitsPerMetre;

            Vector3 normal = Vector3.Cross(second - first, third - first);

            if (normal.LengthSquared() <= DegenerateArea)
            {
                // A sliver. IVP's own builder drops these too rather than normalising a zero.
                continue;
            }

            normal = Vector3.Normalize(normal);

            float distance = Vector3.Dot(normal, first);

            if (!Duplicate(planes, normal, distance))
            {
                planes.Add((normal, distance));
            }
        }

        if (planes.Count == 0)
        {
            return;
        }

        IvpWorldLedge ledge = new(
            center * SourceUnitsPerMetre, radius * SourceUnitsPerMetre, planes);

        _ledges.Add(ledge);

        Index(_ledges.Count - 1, ledge);
    }

    /// <summary>Files one ledge under every grid cell its bounding sphere reaches.</summary>
    /// <remarks>
    /// **A broadphase is not an optimisation here, it is the difference between running and not.**
    /// Testing every hull point against all 3,030 of `koth_harvest_final`'s ledges cost 751 ms per
    /// frame, measured — eight corpses catching up sixty-six ticks each is billions of sphere
    /// tests. **IVP has one too** (`ivp_range_manager`), so this is the engine's shape rather than
    /// a departure; the structure differs because IVP's is a coordinate-sorted range list and this
    /// is a uniform grid, and the answer either gives is the same candidate set.
    ///
    /// **A ledge too big for the grid goes in a list checked every time.** The world brush model
    /// has a few of those — a whole skybox shell is one convex piece — and filing them into
    /// thousands of cells would cost more than testing them always.
    /// </remarks>
    private void Index(int at, IvpWorldLedge ledge)
    {
        int minimumX = Cell(ledge.Center.X - ledge.Radius);
        int maximumX = Cell(ledge.Center.X + ledge.Radius);
        int minimumY = Cell(ledge.Center.Y - ledge.Radius);
        int maximumY = Cell(ledge.Center.Y + ledge.Radius);
        int minimumZ = Cell(ledge.Center.Z - ledge.Radius);
        int maximumZ = Cell(ledge.Center.Z + ledge.Radius);

        long cells =
            (long)(maximumX - minimumX + 1) *
            (maximumY - minimumY + 1) *
            (maximumZ - minimumZ + 1);

        if (cells > MaximumCells)
        {
            _oversized.Add(at);
            return;
        }

        for (int x = minimumX; x <= maximumX; x++)
        {
            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    (int, int, int) key = (x, y, z);

                    if (!_grid.TryGetValue(key, out List<int>? bucket))
                    {
                        bucket = [];
                        _grid[key] = bucket;
                    }

                    bucket.Add(at);
                }
            }
        }
    }

    /// <summary>Which grid cell a coordinate falls in.</summary>
    private static int Cell(float along) => (int)MathF.Floor(along / CellSize);

    /// <summary>How deep a point is inside the world, and along which normal.</summary>
    /// <param name="point">Where to test, in Source units.</param>
    /// <returns>The outward normal and the depth, or null when the point is outside everything.</returns>
    /// <remarks>
    /// **The SHALLOWEST face wins, which is the opposite of the obvious choice.** A point inside a
    /// convex ledge is behind every one of its planes; the face it is least far behind is the one it
    /// entered through, so that is the direction that takes it back out by the shortest route.
    /// Picking the deepest instead pushes a corpse resting on a floor out through a wall.
    ///
    /// **A ledge whose sphere does not reach the point is skipped before its planes are read**,
    /// which is what the ledge tree's bounding sphere is for.
    /// </remarks>
    public (Vector3 Normal, float Depth)? Penetration(Vector3 point)
    {
        (Vector3 Normal, float Depth)? best = null;

        _grid.TryGetValue((Cell(point.X), Cell(point.Y), Cell(point.Z)), out List<int>? nearby);

        int candidates = (nearby?.Count ?? 0) + _oversized.Count;

        for (int candidate = 0; candidate < candidates; candidate++)
        {
            int index = nearby is not null && candidate < nearby.Count
                ? nearby[candidate]
                : _oversized[candidate - (nearby?.Count ?? 0)];

            IvpWorldLedge ledge = _ledges[index];

            if ((point - ledge.Center).LengthSquared() > ledge.Radius * ledge.Radius)
            {
                continue;
            }

            (Vector3 Normal, float Depth)? shallowest = null;

            for (int plane = 0; plane < ledge.Planes.Count; plane++)
            {
                (Vector3 normal, float distance) = ledge.Planes[plane];

                float outside = Vector3.Dot(normal, point) - distance;

                if (outside > 0f)
                {
                    // Outside one face of a convex piece is outside the piece. No further test can
                    // put the point back in, so this ledge is finished.
                    shallowest = null;
                    break;
                }

                if (shallowest is null || -outside < shallowest.Value.Depth)
                {
                    shallowest = (normal, -outside);
                }
            }

            if (shallowest is { } found && (best is null || found.Depth > best.Value.Depth))
            {
                best = found;
            }
        }

        return best;
    }

    /// <summary>Whether a plane is already held, up to the angle and offset IVP treats as the same.</summary>
    private static bool Duplicate(
        List<(Vector3 Normal, float Distance)> planes, Vector3 normal, float distance)
    {
        foreach ((Vector3 held, float at) in planes)
        {
            if (Vector3.Dot(held, normal) > PlaneCosine && MathF.Abs(at - distance) < PlaneOffset)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How wide one broadphase cell is, in Source units.</summary>
    /// <remarks>
    /// **128 units is two player heights**, which is the scale a corpse's limbs move at. Smaller
    /// cells file a floor brush into more of them for no gain; larger ones put a whole room in one
    /// bucket and undo the index.
    /// </remarks>
    private const float CellSize = 128f;

    /// <summary>How many cells one ledge may be filed into before it is called oversized.</summary>
    private const int MaximumCells = 512;

    /// <summary>Below this squared cross-product length a triangle has no usable normal.</summary>
    private const float DegenerateArea = 1e-12f;

    /// <summary>How parallel two faces must be to count as one — about a quarter of a degree.</summary>
    private const float PlaneCosine = 0.99999f;

    /// <summary>And how close their offsets must be, in Source units.</summary>
    private const float PlaneOffset = 0.01f;
}
