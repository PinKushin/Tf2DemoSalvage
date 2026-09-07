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
/// <param name="Contents">
/// What this ledge is made of, as the <c>CONTENTS_*</c> mask the map declares for its solid.
///
/// **The engine sets exactly this on every world solid it creates** —
/// `pObject-&gt;SetContents( g_SolidSetup.GetContentsMask() )` (`game/shared/physics_shared.cpp:648`)
/// — and then refuses any pair the two masks do not share:
/// `if ( !(pObj0-&gt;GetContents() &amp; pEntity1-&gt;PhysicsSolidMaskForEntity()) || ... ) return 0;`
/// (`game/client/physics.cpp:249`). Without it a corpse collides with every brush in the map
/// including the ones written to stop players and nothing else.
/// </param>
public readonly record struct IvpWorldLedge(
    Vector3 Center,
    float Radius,
    IReadOnlyList<(Vector3 Normal, float Distance)> Planes,
    int Contents);

/// <summary>One triangle of terrain, with its own plane.</summary>
/// <param name="A">First vertex, in Source units.</param>
/// <param name="B">Second.</param>
/// <param name="C">Third.</param>
/// <param name="Normal">Its outward normal.</param>
/// <param name="Distance">Its plane's offset along that normal.</param>
public readonly record struct IvpWorldTriangle(
    Vector3 A, Vector3 B, Vector3 C, Vector3 Normal, float Distance);

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

    /// <summary>One point, out of IVP's convention and into Source's.</summary>
    /// <param name="point">A point as the <c>IVPS</c> section stores it.</param>
    /// <returns>The same point in Source units and Source axes.</returns>
    /// <remarks>
    /// **Units are only one third of the conversion, and this project already knew that** —
    /// [[ivp-is-a-third-convention]] says "axes, units and transpose, all at once", and the first
    /// version of this reader did the units alone. **IVP is Y-up where Source is Z-up**, so a
    /// height arrives in the Y slot: `koth_harvest_final`'s world hull read as `y -1184..160` and
    /// `z -10016..3040`, which is a map lying on its side.
    ///
    /// **`hl.y = −ivp.z` and not `+`**, because the swap also changes handedness. Reading it
    /// without the sign mirrors the map — every wall in the right place along one axis and the
    /// wrong side along another, which looks like a subtly wrong map rather than a broken one.
    /// </remarks>
    public static Vector3 ToSource(Vector3 point) =>
        new(
            point.X * SourceUnitsPerMetre,
            -point.Z * SourceUnitsPerMetre,
            point.Y * SourceUnitsPerMetre);

    private readonly List<IvpWorldLedge> _ledges = [];

    /// <summary>Ledge indices by grid cell — the broadphase.</summary>
    private readonly Dictionary<(int X, int Y, int Z), List<int>> _grid = [];

    /// <summary>Ledges too large for the fine grid, filed in a coarser one.</summary>
    private readonly Dictionary<(int X, int Y, int Z), List<int>> _coarse = [];

    /// <summary>Terrain triangles, and their own index.</summary>
    private readonly List<IvpWorldTriangle> _triangles = [];

    private readonly Dictionary<(int X, int Y, int Z), List<int>> _triangleGrid = [];

    /// <summary>Every convex piece of the world.</summary>
    public IReadOnlyList<IvpWorldLedge> Ledges => _ledges;

    /// <summary>How many ledges are too large to file and so are tested against everything.</summary>
    /// <remarks>
    /// **The number that decides whether the broadphase is one**, because an oversized ledge is
    /// examined by every sweep of every hull point of every body. A handful is the cost of a
    /// skybox shell; a thousand is a linear scan wearing a grid.
    /// </remarks>
    public int OversizedCount => _coarse.Count;

    /// <summary>How many candidate ledges and triangles the sweeps have examined.</summary>
    /// <remarks>Carried out of the loop that examined them, never recounted (B243).</remarks>
    public long Examined { get; private set; }

    /// <summary>How many terrain triangles this world holds.</summary>
    /// <remarks>
    /// **A control, and the reason it exists is that its absence cost a wrong conclusion.** "The
    /// corpse still falls" was read as a collision-response fault while the terrain half might
    /// simply have been empty — and an empty answer needs something that MUST be present before it
    /// can be believed (`docs/memory/an-empty-search-needs-a-control.md`).
    /// </remarks>
    public int TriangleCount => _triangles.Count;

    /// <summary><c>CONTENTS_SOLID</c>, what ordinary brushwork and every prop is made of.</summary>
    /// <remarks><c>public/bspflags.h:22</c>. The default for anything that does not say.</remarks>
    public const int ContentsSolid = 0x1;

    /// <summary><c>MASK_SOLID</c> — what a RAGDOLL collides with, and the whole rule.</summary>
    /// <remarks>
    /// **A ragdoll uses `MASK_SOLID`, and the SDK says why in a comment above the override**:
    ///
    /// <code>
    /// // Makes ragdolls ignore npcclip brushes
    /// unsigned int C_AI_BaseNPC::PhysicsSolidMaskForEntity( void ) const
    /// {
    ///     // This allows ragdolls to move through npcclip brushes
    ///     if ( !IsRagdoll() ) { return MASK_NPCSOLID; }
    ///     return MASK_SOLID;
    /// }
    /// </code>
    ///
    /// `game/client/c_ai_basenpc.cpp:53-62`, and the base is the same value —
    /// `CBaseEntity::PhysicsSolidMaskForEntity` returns `MASK_SOLID` outright
    /// (`game/shared/physics_main_shared.cpp:1107-1110`). So a corpse collides with
    /// `CONTENTS_SOLID | CONTENTS_MOVEABLE | CONTENTS_WINDOW | CONTENTS_MONSTER | CONTENTS_GRATE`
    /// (`public/bspflags.h:106`) — and **not** with `CONTENTS_PLAYERCLIP`, which
    /// `MASK_PLAYERSOLID` has and this does not.
    ///
    /// **That absence is what put three corpses under `koth_harvest_final`.** Its solid 1 declares
    /// `"contents" "65536"` — playerclip alone — and spans x ±1600, y ±2376, z −800..16: a box over
    /// the whole middle of the map. A corpse that dropped below z 16 was inside it, in contact with
    /// its interior the entire way down, and slid to rest on its floor at −755. Every escapee
    /// measured had ten or more contacts at the moment it passed −50, which is what "sinking while
    /// touching" had been describing all along.
    /// </remarks>
    public const int MaskSolid = 0x1 | 0x4000 | 0x2 | 0x2000000 | 0x8;

    /// <summary>What this world's queries collide with, defaulting to a ragdoll's mask.</summary>
    /// <remarks>
    /// **Settable because the mask is a property of the ASKER, not of the world.** The engine reads
    /// it off the entity at every pair test — `pEntity1-&gt;PhysicsSolidMaskForEntity()` — so a world
    /// that hard-coded one would be answering a different question for a player than for a corpse.
    /// Nothing but a corpse asks this world anything yet, which is why the default is theirs.
    /// </remarks>
    public int Mask { get; set; } = MaskSolid;

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
        float radius) =>
        Add(points, triangles, center, radius, Vector3.Zero, ContentsSolid);

    /// <summary>Adds one ledge, converting it and placing it at an entity's origin.</summary>
    /// <param name="points">The ledge's points, in metres.</param>
    /// <param name="triangles">Its triangles, indexing those points.</param>
    /// <param name="center">Its node's bounding-sphere centre, in metres.</param>
    /// <param name="radius">That sphere's radius, in metres.</param>
    /// <param name="origin">Where the model this ledge belongs to stands, in SOURCE units.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A brush ENTITY's hull is in its own model space and the entity places it.** The world is
    /// model 0 and stands at the map origin; a `func_door` or `func_brush` is model `*N` with its
    /// own `origin` key, and its collide is stored relative to that. Adding one without the offset
    /// puts a door at the map origin — geometry missing where the door is and phantom geometry
    /// where it is not.
    /// </remarks>
    public void Add(
        IReadOnlyList<Vector3> points,
        IReadOnlyList<(int A, int B, int C)> triangles,
        Vector3 center,
        float radius,
        Vector3 origin) =>
        Add(points, triangles, center, radius, origin, ContentsSolid);

    /// <summary>Adds one ledge at an entity's origin, with the contents its solid declares.</summary>
    /// <param name="points">The ledge's points, in metres.</param>
    /// <param name="triangles">Its triangles, indexing those points.</param>
    /// <param name="center">Its node's bounding-sphere centre, in metres.</param>
    /// <param name="radius">That sphere's radius, in metres.</param>
    /// <param name="origin">Where the model this ledge belongs to stands, in SOURCE units.</param>
    /// <param name="contents">The <c>CONTENTS_*</c> mask — see <see cref="IvpWorldLedge"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Add(
        IReadOnlyList<Vector3> points,
        IReadOnlyList<(int A, int B, int C)> triangles,
        Vector3 center,
        float radius,
        Vector3 origin,
        int contents) =>
        Add(
            points, triangles, center, radius, Matrix4x4.CreateTranslation(origin), contents);

    /// <summary>Adds one ledge, converting it and placing it by a full transform.</summary>
    /// <param name="points">The ledge's points, in metres.</param>
    /// <param name="triangles">Its triangles, indexing those points.</param>
    /// <param name="center">Its node's bounding-sphere centre, in metres.</param>
    /// <param name="radius">That sphere's radius, in metres.</param>
    /// <param name="placement">Where the model stands and how it is turned, in SOURCE units.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **A static prop is ROTATED where a brush entity is only moved**, which is why this exists
    /// beside the origin overload. The engine places one through the same call it places any other
    /// static object — an origin and a `QAngle` — so a prop lying on its side or a fence turned to
    /// face a path is a different hull in the world, not the same hull shifted.
    ///
    /// **Uniform scale belongs in here too**, since the placement carries it and a scaled prop's
    /// collision scales with it. The radius is taken from the transform's own scale rather than
    /// assumed to be one, so a bounding sphere still contains what it claims to.
    /// </remarks>
    public void Add(
        IReadOnlyList<Vector3> points,
        IReadOnlyList<(int A, int B, int C)> triangles,
        Vector3 center,
        float radius,
        Matrix4x4 placement) =>
        Add(points, triangles, center, radius, placement, ContentsSolid);

    /// <summary>Adds one ledge by a full transform, with the contents its solid declares.</summary>
    /// <param name="points">The ledge's points, in metres.</param>
    /// <param name="triangles">Its triangles, indexing those points.</param>
    /// <param name="center">Its node's bounding-sphere centre, in metres.</param>
    /// <param name="radius">That sphere's radius, in metres.</param>
    /// <param name="placement">Where the model stands and how it is turned, in SOURCE units.</param>
    /// <param name="contents">The <c>CONTENTS_*</c> mask — see <see cref="IvpWorldLedge"/>.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public void Add(
        IReadOnlyList<Vector3> points,
        IReadOnlyList<(int A, int B, int C)> triangles,
        Vector3 center,
        float radius,
        Matrix4x4 placement,
        int contents)
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

            Vector3 first = Vector3.Transform(ToSource(points[a]), placement);
            Vector3 second = Vector3.Transform(ToSource(points[b]), placement);
            Vector3 third = Vector3.Transform(ToSource(points[c]), placement);

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

        // **The radius has to grow with the placement's scale or the sphere stops containing the
        // hull**, and a sphere that under-reports is a ledge the broadphase skips for a point that
        // is actually inside it. Taken from the transform rather than from a separate argument, so
        // the two cannot disagree.
        float scale = MathF.Sqrt(MathF.Max(
            MathF.Max(
                new Vector3(placement.M11, placement.M12, placement.M13).LengthSquared(),
                new Vector3(placement.M21, placement.M22, placement.M23).LengthSquared()),
            new Vector3(placement.M31, placement.M32, placement.M33).LengthSquared()));

        IvpWorldLedge ledge = new(
            Vector3.Transform(ToSource(center), placement),
            radius * SourceUnitsPerMetre * scale,
            planes,
            contents);

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

        // **A ledge too big for the fine grid gets a COARSE one, not a list tested every time.**
        // Ninety of `koth_harvest_final`'s 3,030 ledges are that big — a skybox shell, a whole
        // floor slab — and testing all ninety on every sweep of every hull point was 69,000 of the
        // 75,000 candidates a single tick examined. A second tier at sixteen times the cell size
        // files them in a handful of cells each and a sweep touches only the ones it passes.
        if (cells > MaximumCells)
        {
            File(_coarse, at, CoarseCellSize, ledge);
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

    /// <summary>Files one ledge into a grid of the given cell size, by its bounding sphere.</summary>
    private static void File(
        Dictionary<(int X, int Y, int Z), List<int>> grid,
        int at,
        float size,
        IvpWorldLedge ledge)
    {
        int minimumX = (int)MathF.Floor((ledge.Center.X - ledge.Radius) / size);
        int maximumX = (int)MathF.Floor((ledge.Center.X + ledge.Radius) / size);
        int minimumY = (int)MathF.Floor((ledge.Center.Y - ledge.Radius) / size);
        int maximumY = (int)MathF.Floor((ledge.Center.Y + ledge.Radius) / size);
        int minimumZ = (int)MathF.Floor((ledge.Center.Z - ledge.Radius) / size);
        int maximumZ = (int)MathF.Floor((ledge.Center.Z + ledge.Radius) / size);

        for (int x = minimumX; x <= maximumX; x++)
        {
            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    (int, int, int) key = (x, y, z);

                    if (!grid.TryGetValue(key, out List<int>? bucket))
                    {
                        bucket = [];
                        grid[key] = bucket;
                    }

                    bucket.Add(at);
                }
            }
        }
    }

    /// <summary>Which grid cell a coordinate falls in.</summary>
    private static int Cell(float along) => (int)MathF.Floor(along / CellSize);

    /// <summary>Adds one triangle of terrain, already in Source units.</summary>
    /// <param name="a">First vertex.</param>
    /// <param name="b">Second.</param>
    /// <param name="c">Third.</param>
    /// <remarks>
    /// **Terrain is a TRIANGLE SOUP and not a convex piece, which is the engine's own shape.**
    /// `CDispCollTree::GetVirtualMeshList` fills a `virtualmeshlist_t` of verts and indices
    /// (`dispcoll_common.cpp:1472`), and vphysics is handed that rather than a `CPhysCollide` built
    /// from brushes — which is why displacements are absent from `LUMP_PHYSCOLLIDE` entirely and
    /// why a map with perfect brush hulls still has no ground under a corpse.
    ///
    /// **Already in Source units**, unlike a hull: these come from the BSP's displacement lump,
    /// which the compiler writes in world coordinates, not from IVP.
    /// </remarks>
    public void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a);

        if (normal.LengthSquared() <= DegenerateArea)
        {
            return;
        }

        normal = Vector3.Normalize(normal);

        IvpWorldTriangle triangle = new(a, b, c, normal, Vector3.Dot(normal, a));

        _triangles.Add(triangle);

        int at = _triangles.Count - 1;

        int minimumX = Cell(MathF.Min(a.X, MathF.Min(b.X, c.X)));
        int maximumX = Cell(MathF.Max(a.X, MathF.Max(b.X, c.X)));
        int minimumY = Cell(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)));
        int maximumY = Cell(MathF.Max(a.Y, MathF.Max(b.Y, c.Y)));

        // **Filed by X and Y only, over the whole Z column its slab reaches.** A point resting on
        // terrain is directly above a triangle and a little below it; cells stacked in Z would put
        // the point and the triangle it stands on in different buckets on every slope.
        int minimumZ = Cell(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) - TerrainDepth);
        int maximumZ = Cell(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)));

        for (int x = minimumX; x <= maximumX; x++)
        {
            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    (int, int, int) key = (x, y, z);

                    if (!_triangleGrid.TryGetValue(key, out List<int>? bucket))
                    {
                        bucket = [];
                        _triangleGrid[key] = bucket;
                    }

                    bucket.Add(at);
                }
            }
        }
    }

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
    public (Vector3 Normal, float Depth)? Penetration(Vector3 point) =>
        Penetration(point, Vector3.Zero);

    /// <summary>The same, told which way the point was travelling when it got there.</summary>
    /// <param name="point">Where to test, in Source units.</param>
    /// <param name="motion">How the point has been moving; zero when that is not known.</param>
    /// <returns>The outward normal and the depth, or null when the point is outside everything.</returns>
    /// <remarks>
    /// **The face a point is LEAST far behind stops being the way out once it is past halfway**,
    /// and that is what put corpses under the map. A floor brush is often sixteen units thick; a
    /// body nine to fourteen units into one — which is what the escapees measured — is nearer the
    /// underside, so the shallowest face is the bottom and the push that should lift it drives it
    /// through instead. Ticks 13651, 13701 and 13823, each within a second of its own death and
    /// each with contacts the whole way.
    ///
    /// **The engine does not have to choose, because it never forgets.** IVP's mindist keeps the
    /// closest-feature pair from when the two were still apart, so the face a body entered through
    /// is simply the one it is still being measured against — the retained feature, not a fresh
    /// guess from inside.
    ///
    /// **Travel direction was tried as a stand-in for that memory and MEASURED WORSE**, so the
    /// parameter is accepted and ignored rather than quietly kept. Refusing every face whose
    /// normal points along the motion took `z1800` from three corpses leaving the world to four,
    /// and two conformance tests reddened on the way because a resting body's recovery drift
    /// points up and that rule then refused the very face holding it. It was a guess rather than a
    /// transcription, and the rule here is that a guess which loses to the measurement goes.
    ///
    /// **What would settle it is the engine**, whose mindist never has to choose because it keeps
    /// the pair — and the point where a contact record becomes an impulse is the piece
    /// `docs/findings/51` still lists as unread.
    /// </remarks>
    public (Vector3 Normal, float Depth)? Penetration(Vector3 point, Vector3 motion) =>
        Touching(point, motion) is { } hit ? (hit.Normal, hit.Depth) : null;

    /// <summary>The same, and WHICH world feature it is against.</summary>
    /// <param name="point">Where the point is, in Source units.</param>
    /// <param name="motion">How the point has been moving; zero when that is not known.</param>
    /// <returns>The normal, the depth and a stable id for the face, or null when outside.</returns>
    /// <remarks>
    /// **The id is the point of this overload, and it is what IVP's mindist keeps.** A contact
    /// solved across steps needs to know it is the SAME contact, and a normal re-derived every step
    /// cannot say so: a body settling into a surface changes which of a ledge's faces is
    /// shallowest, so anything keyed on the normal loses its accumulated state exactly when the
    /// body is coming to rest. Measured — the persistent hold built for that reason could not
    /// support a corpse at all, and switching off the depth term that was really holding it dropped
    /// a ragdoll through the map to −1519.
    ///
    /// **A ledge index and a plane index within it**, packed, because both are stable for as long
    /// as the body rests on that face; terrain triangles get their own range above them. This is
    /// the identity half of a closest-feature pair and not the pair itself — the engine also
    /// retains WHICH feature of the moving body is closest, and this project still tests every hull
    /// vertex against the world every step.
    /// </remarks>
    public (Vector3 Normal, float Depth, int Feature)? Touching(Vector3 point, Vector3 motion)
    {
        bool found = false;
        float deepest = 0f;
        Vector3 face = default;
        int feature = -1;

        _ = motion;

        // Both tiers, at the point itself — a degenerate segment, so the same gather serves.
        List<int> candidates = Candidates(point, point);

        for (int candidate = 0; candidate < candidates.Count; candidate++)
        {
            IvpWorldLedge ledge = _ledges[candidates[candidate]];

            if ((ledge.Contents & Mask) == 0 ||
                (point - ledge.Center).LengthSquared() > ledge.Radius * ledge.Radius)
            {
                continue;
            }

            bool shallowest = false;
            float shallowDepth = float.MaxValue;
            Vector3 shallowFace = default;
            int shallowPlane = -1;

            for (int plane = 0; plane < ledge.Planes.Count; plane++)
            {
                (Vector3 normal, float distance) = ledge.Planes[plane];

                float outside = Vector3.Dot(normal, point) - distance;

                if (outside > 0f)
                {
                    // Outside one face of a convex piece is outside the piece. No further test can
                    // put the point back in, so this ledge is finished.
                    shallowest = false;
                    break;
                }

                // **A face the point is heading OUT of is not the face it came in through.**
                // Pushing along the motion continues the journey; see the remarks.
                if (-outside < shallowDepth)
                {
                    shallowDepth = -outside;
                    shallowFace = normal;
                    shallowPlane = plane;
                    shallowest = true;
                }
            }

            if (shallowest && (!found || shallowDepth > deepest))
            {
                found = true;
                deepest = shallowDepth;
                face = shallowFace;

                // **Packed so one integer names the face**, which is all a retained contact needs
                // to recognise itself next step. A ledge has far fewer than `PlanesPerLedge` faces
                // in practice; the multiplier only has to be larger than any real count.
                feature = (candidates[candidate] * PlanesPerLedge) + shallowPlane;
            }
        }

        (Vector3 Normal, float Depth)? brush = found ? (face, deepest) : null;

        (Vector3 Normal, float Depth)? both = Terrain(point, brush);

        if (both is not { } hit)
        {
            return null;
        }

        // **Terrain wins its own identity**, because a triangle is a different kind of feature.
        // `Terrain` returns whichever of the two is deeper, so it took over exactly when there was
        // no brush hit or the depth grew — a comparison, not a float equality.
        if (brush is not { } chosen || hit.Depth > chosen.Depth)
        {
            feature = TerrainFeature;
        }

        return (hit.Normal, hit.Depth, feature);
    }

    /// <summary>More planes than any real ledge has, so a packed id cannot collide.</summary>
    private const int PlanesPerLedge = 4096;

    /// <summary>One id for terrain, which this does not yet tell apart triangle by triangle.</summary>
    /// <remarks>
    /// **Deliberately coarse and stated as such.** `Terrain` returns a normal and a depth and not
    /// which triangle produced them, so every terrain contact on a body shares an identity here. A
    /// body resting on a hillside therefore accumulates one retained contact where it should have
    /// one per triangle it touches — better than the normal-keyed version it replaces, and not the
    /// engine's, which names the feature exactly.
    /// </remarks>
    private const int TerrainFeature = int.MaxValue;

    /// <summary>Where a moving point first enters the world, if it does.</summary>
    /// <param name="from">Where the point is now.</param>
    /// <param name="to">Where it would be after this move.</param>
    /// <returns>The surface normal it would enter through, or null when the path is clear.</returns>
    /// <remarks>
    /// **A point sample cannot answer this and it was measured failing.** A body at 1,131 units a
    /// second moves seventeen units in a tick, and a floor brush sixteen units thick fits entirely
    /// between where a point is and where it will be — so testing the far end finds it out the
    /// other side, clear, and the body sails through a floor it never technically touched. The
    /// segment is what has to be tested, and this is that.
    ///
    /// **This is what IVP's mindist system provides for free**, by tracking a pair's closing
    /// distance and rescheduling its check before contact rather than sampling positions
    /// (`docs/findings/51`, the contact-pair re-check scheduler).
    ///
    /// **Exact for both kinds of geometry, not sampled.** A convex ledge is clipped by its own
    /// planes, which is the standard slab clip and gives the entry face directly; a terrain
    /// triangle is a plane crossing plus the same containment test its point case uses.
    /// </remarks>
    public Vector3? Entry(Vector3 from, Vector3 to) => Sweep(from, to)?.Normal;

    /// <summary>Where a moving point first enters the world, and how far along it got.</summary>
    /// <param name="from">Where the point is now.</param>
    /// <param name="to">Where it would be after this move.</param>
    /// <returns>The surface it enters and the fraction of the way, or null when the path is clear.</returns>
    /// <remarks>
    /// **The fraction is what lets a caller stop a body AT the surface** rather than after it. A
    /// discrete solver that only learns "something was crossed" has already crossed it.
    /// </remarks>
    public (Vector3 Normal, float Fraction)? Sweep(Vector3 from, Vector3 to)
    {
        Vector3 travel = to - from;

        if (travel.LengthSquared() <= DegenerateArea)
        {
            return null;
        }

        float nearest = float.MaxValue;
        Vector3? normal = null;

        foreach (int index in Candidates(from, to))
        {
            Examined++;

            // **The ledge's own bounding sphere, tested before its planes.** This is the sphere
            // the ledge tree already carries, so it costs nothing to keep and it is what makes the
            // oversized list affordable: those are examined by every sweep of every hull point,
            // and on `koth_harvest_final` there are ninety of them.
            if ((_ledges[index].Contents & Mask) == 0 ||
                !Reaches(_ledges[index], from, travel) ||
                Clip(_ledges[index], from, travel) is not { } clipped ||
                clipped.Fraction >= nearest)
            {
                continue;
            }

            nearest = clipped.Fraction;
            normal = clipped.Normal;
        }

        foreach (int index in TriangleCandidates(from, to))
        {
            IvpWorldTriangle triangle = _triangles[index];

            float above = Vector3.Dot(triangle.Normal, from) - triangle.Distance;
            float below = Vector3.Dot(triangle.Normal, to) - triangle.Distance;

            // Crossing from the front to the back, which is the only direction that is an entry.
            if (above < 0f || below > 0f || above - below <= FloatEpsilon)
            {
                continue;
            }

            float fraction = above / (above - below);

            if (fraction >= nearest || !Within(triangle, from + (travel * fraction)))
            {
                continue;
            }

            nearest = fraction;
            normal = triangle.Normal;
        }

        return normal is { } face ? (face, nearest) : null;
    }

    /// <summary>Whether a segment comes within a ledge's own bounding sphere at all.</summary>
    /// <remarks>
    /// **The cheap half of the sweep, and it decides whether the expensive half runs.** The
    /// distance from the sphere's centre to the segment is compared against the radius; a ledge the
    /// path never approaches is rejected in a handful of multiplies instead of a loop over every
    /// one of its planes.
    /// </remarks>
    private static bool Reaches(IvpWorldLedge ledge, Vector3 from, Vector3 travel)
    {
        Vector3 toCentre = ledge.Center - from;

        float length = travel.LengthSquared();

        // Where along the segment the centre projects, clamped to its ends.
        float along = length > 0f
            ? Math.Clamp(Vector3.Dot(toCentre, travel) / length, 0f, 1f)
            : 0f;

        Vector3 nearest = from + (travel * along);

        return (ledge.Center - nearest).LengthSquared() <= ledge.Radius * ledge.Radius;
    }

    /// <summary>Clips a segment by one convex ledge, giving the face it enters through.</summary>
    /// <remarks>
    /// **The standard slab clip, and the entry face falls out of it.** The segment enters at the
    /// LAST plane it crosses inwards and leaves at the first it crosses outwards; if those cross
    /// over, it missed. Nothing here needs the ledge's triangles, which is the whole reason a
    /// convex piece is stored as planes.
    /// </remarks>
    private static (float Fraction, Vector3 Normal)? Clip(
        IvpWorldLedge ledge, Vector3 from, Vector3 travel)
    {
        float enter = 0f;
        float leave = 1f;
        Vector3 face = default;
        bool entered = false;

        for (int plane = 0; plane < ledge.Planes.Count; plane++)
        {
            (Vector3 normal, float distance) = ledge.Planes[plane];

            float start = Vector3.Dot(normal, from) - distance;
            float along = Vector3.Dot(normal, travel);

            if (MathF.Abs(along) <= FloatEpsilon)
            {
                if (start > 0f)
                {
                    // Parallel to this face and outside it: the whole segment misses.
                    return null;
                }

                continue;
            }

            float at = -start / along;

            if (along < 0f)
            {
                if (at > enter)
                {
                    enter = at;
                    face = normal;
                    entered = true;
                }
            }
            else if (at < leave)
            {
                leave = at;
            }

            if (enter > leave)
            {
                return null;
            }
        }

        return entered && enter is >= 0f and <= 1f ? (enter, face) : null;
    }

    /// <summary>The terrain half of the same question, taking the shallower answer of the two.</summary>
    /// <remarks>
    /// **A triangle is a surface and not a solid, so "inside" has to be given a thickness.** A point
    /// is in contact when it sits behind a triangle's plane, within the triangle's own edges, and no
    /// further behind than a slab — deeper than that it has fallen through and there is nothing
    /// honest to push it back to. The engine avoids the question by building an outer hull around
    /// the virtual mesh (`virtualmeshparams_t::buildOuterHull`); this is the slab that stands in for
    /// one, and it is a stated departure.
    ///
    /// **The SHALLOWEST contact wins across both halves**, brush and terrain alike, so a corpse in a
    /// corner where a brush meets a hillside is pushed out the short way.
    /// </remarks>
    private (Vector3 Normal, float Depth)? Terrain(
        Vector3 point, (Vector3 Normal, float Depth)? best)
    {
        if (!_triangleGrid.TryGetValue(
            (Cell(point.X), Cell(point.Y), Cell(point.Z)), out List<int>? nearby))
        {
            return best;
        }

        for (int candidate = 0; candidate < nearby.Count; candidate++)
        {
            IvpWorldTriangle triangle = _triangles[nearby[candidate]];

            float outside = Vector3.Dot(triangle.Normal, point) - triangle.Distance;

            if (outside > 0f || outside < -TerrainDepth)
            {
                continue;
            }

            if (!Within(triangle, point))
            {
                continue;
            }

            if (best is null || -outside < best.Value.Depth)
            {
                best = (triangle.Normal, -outside);
            }
        }

        return best;
    }

    /// <summary>Whether a point projects inside a triangle, by the sign of its three edge tests.</summary>
    /// <remarks>
    /// **Projected along the triangle's own normal rather than dropped onto a plane**, so a vertical
    /// cliff face works exactly like a floor. All three cross products must agree in sign, which is
    /// the standard containment test and needs no barycentric division.
    /// </remarks>
    private static bool Within(IvpWorldTriangle triangle, Vector3 point) =>
        Vector3.Dot(Vector3.Cross(triangle.B - triangle.A, point - triangle.A), triangle.Normal) >= 0f &&
        Vector3.Dot(Vector3.Cross(triangle.C - triangle.B, point - triangle.B), triangle.Normal) >= 0f &&
        Vector3.Dot(Vector3.Cross(triangle.A - triangle.C, point - triangle.C), triangle.Normal) >= 0f;

    /// <summary>Every ledge whose cell the segment passes through, plus the oversized ones.</summary>
    /// <remarks>
    /// **The segment's whole cell RANGE, not its endpoints.** A grid query that asked only where
    /// the point starts and ends would miss a wall standing between them, which is the same class
    /// of mistake as sampling the far end of the move.
    /// </remarks>
    private List<int> Candidates(Vector3 from, Vector3 to)
    {
        Gather(_grid, from, to, CellSize, _candidates);

        Gather(_coarse, from, to, CoarseCellSize, _coarseCandidates);

        _candidates.AddRange(_coarseCandidates);

        return _candidates;
    }

    /// <summary>The same, for terrain triangles.</summary>
    private List<int> TriangleCandidates(Vector3 from, Vector3 to)
    {
        Gather(_triangleGrid, from, to, CellSize, _triangleCandidates);

        return _triangleCandidates;
    }

    /// <summary>Fills a reused buffer with everything filed in the cells the segment's box covers.</summary>
    /// <remarks>
    /// **A reused list rather than an iterator, and that is not a micro-optimisation here.** These
    /// run once per hull point per sub-step per body — hundreds of thousands of times to catch one
    /// corpse up — and a `yield return` walk allocates an enumerator on every one of them.
    /// </remarks>
    private static void Gather(
        Dictionary<(int X, int Y, int Z), List<int>> grid,
        Vector3 from,
        Vector3 to,
        float size,
        List<int> into)
    {
        into.Clear();

        int minimumX = (int)MathF.Floor(MathF.Min(from.X, to.X) / size);
        int maximumX = (int)MathF.Floor(MathF.Max(from.X, to.X) / size);
        int minimumY = (int)MathF.Floor(MathF.Min(from.Y, to.Y) / size);
        int maximumY = (int)MathF.Floor(MathF.Max(from.Y, to.Y) / size);
        int minimumZ = (int)MathF.Floor(MathF.Min(from.Z, to.Z) / size);
        int maximumZ = (int)MathF.Floor(MathF.Max(from.Z, to.Z) / size);

        for (int x = minimumX; x <= maximumX; x++)
        {
            for (int y = minimumY; y <= maximumY; y++)
            {
                for (int z = minimumZ; z <= maximumZ; z++)
                {
                    if (grid.TryGetValue((x, y, z), out List<int>? bucket))
                    {
                        into.AddRange(bucket);
                    }
                }
            }
        }
    }

    private readonly List<int> _candidates = [];

    private readonly List<int> _triangleCandidates = [];

    private readonly List<int> _coarseCandidates = [];

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

    /// <summary>The coarse tier's cell size, for ledges too big for the fine one.</summary>
    /// <remarks>
    /// **Sixteen times the fine cell**, so a ledge that would have needed thousands of fine cells
    /// needs a handful of these. The alternative it replaced was a list every sweep tested in full.
    /// </remarks>
    private const float CoarseCellSize = CellSize * 16f;

    /// <summary>How far behind a terrain triangle still counts as touching it, in Source units.</summary>
    /// <remarks>
    /// **The slab that stands in for the engine's outer hull.** `virtualmeshparams_t` carries a
    /// `buildOuterHull` flag and vphysics closes the mesh with one; a bare triangle soup has no
    /// inside, so contact needs a thickness. Sixty-four units is half a player and several times
    /// the twelve a body falls in one tick at terminal velocity, so nothing that should have landed
    /// slips past it.
    /// </remarks>
    private const float TerrainDepth = 64f;

    /// <summary><c>FLT_EPSILON</c>, the floor the engine's own guards use.</summary>
    private const float FloatEpsilon = 1.1920929e-07f;

    /// <summary>Below this squared cross-product length a triangle has no usable normal.</summary>
    private const float DegenerateArea = 1e-12f;

    /// <summary>How parallel two faces must be to count as one — about a quarter of a degree.</summary>
    private const float PlaneCosine = 0.99999f;

    /// <summary>And how close their offsets must be, in Source units.</summary>
    private const float PlaneOffset = 0.01f;
}
