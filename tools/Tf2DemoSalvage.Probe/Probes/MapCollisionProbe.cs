using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// What a map's baked physics collision holds — the thing a corpse would land on (B58).
/// </summary>
/// <remarks>
/// **The last unread input a ragdoll needs, measured before anything is built on it**
/// (`docs/memory/measure-the-route-before-building-on-it.md`). A TF2 corpse is simulated by the
/// client against the map's own collision, which the compiler bakes into `LUMP_PHYSCOLLIDE`
/// (lump 29, `bspfile.h:310`) and the engine hands to `CreatePolyObjectStatic`. This project reads
/// 34 of the 64 lumps and that is not one of them.
///
/// **The layout is fully specified by Valve's own loader** (`bsplib.cpp:1577-1625`), including its
/// terminator, which is the part a guess would get wrong:
///
/// <code>
/// // physics data is variable length.  The last physmodel is a NULL pointer
/// // with modelIndex -1, dataSize -1
/// struct dphysmodel_t { int modelIndex; int dataSize; int keydataSize; int solidCount; };
/// </code>
///
/// **Each entry is one brush model** — index 0 is the world, the rest are `func_` brush entities —
/// followed by `dataSize` bytes of solids (each a length-prefixed blob) and then `keydataSize`
/// bytes of KeyValues TEXT.
///
/// **So it splits exactly the way a `.phy` does**, which is the finding worth having: the hulls are
/// the closed `IVPS` format this project already skips in a model's `.phy`, and the text beside
/// them is plain KeyValues carrying the surface properties. One format, needed twice.
///
/// <code>
///   map-collision [map]
/// </code>
/// </remarks>
public sealed class MapCollisionProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "map-collision";

    /// <inheritdoc/>
    public string Summary =>
        "the map's baked physics collision, which a corpse lands on: map-collision [map]";

    /// <summary><c>LUMP_PHYSCOLLIDE</c>, <c>bspfile.h:310</c>.</summary>
    private const int PhysCollideLump = 29;

    /// <summary>How far the census drops each ray, in Source units.</summary>
    private const float Drop = 1280f;

    /// <summary>How far apart the two worlds may stop before it counts as a disagreement.</summary>
    /// <remarks>
    /// **Set by the CONTROL's resolution, not by taste, and eight units was below it.**
    /// `BspLeafTree.Sweep` samples its ray every sixteen units and reports the first sample inside
    /// a solid, so it stops up to sixteen units LATE and never early. At eight this reported every
    /// one of 1,089 columns at the centre of `koth_harvest_final` as a disagreement — the sampler,
    /// not the geometry. Thirty-two is twice the step, so anything above it is a surface one world
    /// has and the other does not.
    ///
    /// **The confound is one-sided and that is what makes the measurement usable.** A late stop
    /// makes the camera look LOWER, so "the physics world stops lower" is the direction the sampler
    /// cannot manufacture — and that is the direction a corpse falls through.
    /// </remarks>
    private const float Apart = 32f;

    /// <summary>How many of the ledges over the column to print before counting the rest.</summary>
    private const int Listed = 12;

    /// <summary>Half the width of the whole-map hole scan, in grid steps of twice <see cref="Apart"/>.</summary>
    private const int Across = 40;

    /// <summary>Where the whole-map scan drops from, in Source units.</summary>
    private const float Ceiling = 512f;

    /// <summary>And where it drops to.</summary>
    private const float Floor = -512f;

    /// <summary>How far above and below a triangle the control ray starts and ends.</summary>
    private const float Overhead = 64f;

    /// <summary>How far either side of the column a lump ledge still counts as nearby.</summary>
    private const float Near = 256f;

    /// <summary>Above this a ledge is map geometry rather than one of the world's floor slabs.</summary>
    private const float AboveGround = -512f;

    /// <summary>Within this of a terrain vertex, a hole is at a displacement's own rim.</summary>
    /// <remarks>
    /// **Half a quad on the coarsest displacement, so a rim column cannot be mistaken for open
    /// ground.** A power-2 displacement on a 512-unit face has 128-unit quads; anything within 64
    /// of a terrain vertex is inside or immediately beside the terrain rather than out in a room.
    /// </remarks>
    private const float Rim = 64f;

    /// <summary><c>LUMP_BRUSHES</c> — <c>BspLumpIndex.Brushes</c>, which is internal.</summary>
    private const int BrushesLump = 18;

    /// <summary>Bytes per <c>dbrush_t</c>: first side, side count, contents.</summary>
    private const int BrushStride = 12;

    /// <summary><c>CONTENTS_PLAYERCLIP</c> — <c>bspflags.h</c>.</summary>
    private const int PlayerClip = 0x10000;

    /// <summary><c>LUMP_DISPINFO</c> — <c>BspLumpIndex.DispInfo</c>, which is internal.</summary>
    private const int DispInfoLump = 26;

    /// <summary>Bytes per <c>ddispinfo_t</c> — <c>BspStructLayout.DispInfoStride</c>, which is internal.</summary>
    private const int DispInfoStride = 176;

    /// <summary>Byte offset of <c>power</c> inside one — <c>BspStructLayout.DispPowerOffset</c>.</summary>
    private const int DispPowerOffset = 20;

    /// <summary>Byte offset of <c>m_AllowedVerts</c>, the LAST member of <c>ddispinfo_t</c>.</summary>
    /// <remarks>
    /// **Addressed from the end of the struct, which is the one thing that makes it safe to compute
    /// rather than count.** `uint32 m_AllowedVerts[ALLOWEDVERTS_SIZE]` with `ALLOWEDVERTS_SIZE =
    /// PAD_NUMBER(MAX_DISPVERTS, 32) / 32`, and `MAX_DISPVERTS` is 17 × 17 for power 4, so 289
    /// padded to 320 gives ten words — forty bytes, ending at 176. Counting forwards past
    /// `CDispNeighbor[4]` and `CDispCornerNeighbors[4]` means reproducing two nested classes'
    /// padding, which is exactly the arithmetic
    /// `docs/memory/address-a-struct-by-name-not-from-its-end.md` says goes wrong.
    /// </remarks>
    private const int AllowedVertsOffset = DispInfoStride - (AllowedVertsWords * 4);

    /// <summary><c>ALLOWEDVERTS_SIZE</c> — ten <c>uint32</c>, enough for a power-4 grid's 289.</summary>
    private const int AllowedVertsWords = 10;

    /// <summary>Bytes of <c>dphysmodel_t</c> — four ints.</summary>
    private const int ModelHeaderSize = 16;

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (arguments.Count > 0)
        {
            if (locator.Find(arguments[0]) is not { } named)
            {
                output.WriteLine($"No map named '{arguments[0]}'.");
                return;
            }

            // **`map-collision <map> x y z` asks what is under one point**, which is the question a
            // corpse that free-falls while its neighbours land is really asking.
            (float X, float Y, float Z)? spot = arguments.Count >= 4 &&
                float.TryParse(arguments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(arguments[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                ? (x, y, z)
                : null;

            Report(output, named, verbose: true, spot);
            return;
        }

        if (locator.Find("koth_harvest_final") is not { } anyMap)
        {
            output.WriteLine("No installed maps found.");
            return;
        }

        Report(output, anyMap, verbose: true);

        string folder = Path.GetDirectoryName(anyMap) ?? string.Empty;
        string[] maps = [.. Directory.EnumerateFiles(folder, "*.bsp").Order(StringComparer.Ordinal)];

        int withCollision = 0;

        foreach (string map in maps)
        {
            if (Report(output, map, verbose: false).Models > 0)
            {
                withCollision++;
            }
        }

        output.WriteLine();

        // **The control, and it must equal the map count.** Every compiled map has a world brush
        // model with collision; a number below the total means the walk failed rather than that
        // some maps ship without physics.
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{withCollision} of {maps.Length} installed maps yielded at least one physics model " +
            $"(the control; must match)."));
    }

    /// <summary>Walks one map's collision lump.</summary>
    /// <param name="output">Where to report.</param>
    /// <param name="path">The map file.</param>
    /// <param name="verbose">Whether to print the per-map detail.</param>
    /// <param name="under">A point to ask what is beneath, or null to skip that.</param>
    /// <returns>How many models and solids were found.</returns>
    private static (int Models, int Solids) Report(
        TextWriter output, string path, bool verbose, (float X, float Y, float Z)? under = null)
    {
        ReadOnlyMemory<byte> file;

        try
        {
            file = File.ReadAllBytes(path);
        }
        catch (IOException error)
        {
            output.WriteLine($"{Path.GetFileName(path)}: unreadable — {error.Message}");
            return (0, 0);
        }

        BspHeader header;
        ReadOnlyMemory<byte> lump;

        try
        {
            header = BspHeader.Parse(file.Span);
            lump = BspLumpData.Read(file, header.Lump(PhysCollideLump));
        }
        catch (InvalidDataException error)
        {
            output.WriteLine($"{Path.GetFileName(path)}: lump unreadable — {error.Message}");
            return (0, 0);
        }

        if (lump.Length == 0)
        {
            if (verbose)
            {
                output.WriteLine($"{Path.GetFileName(path)}: no physics collision lump.");
            }

            return (0, 0);
        }

        (int Models, int Solids) walked = Walk(output, Path.GetFileName(path), lump.Span, verbose);

        // **What is UNDER a point, through the PRODUCTION world.** A corpse that free-falls while
        // its neighbours land is asking exactly this, and asking it of a brush-only world answers a
        // different question — which it did: a corpse that lands and one that does not both
        // reported "nothing", because the ground under both is terrain.
        if (under is { } spot)
        {
            MapLevel level = MapLevel.Read(file, NullLogger.Instance);
            IvpWorldCollision world = level.Physics;

            // **The CAMERA's world, as the control.** `MapLevel.Sweep` answers the same "is there
            // ground here" question through an entirely different route — the BSP tree's brushes
            // and the displacement collision — so a point where the camera is stopped and a corpse
            // is not localises the gap to this project's physics world rather than to the map.
            float swept = level.Sweep(
                (spot.X, spot.Y, spot.Z), (spot.X, spot.Y, spot.Z - 512f), halfExtent: 1f);

            // **Split into its two halves, because "the camera stops here" does not say WHICH
            // geometry stopped it** — and the whole question is which of the two the physics world
            // is missing.
            // **The denominator, because a count with nothing to compare it against says nothing.**
            // The lump declares how many displacements the map HAS; `Displacements.Count` says how
            // many this project built a collision surface for. A gap between them is the whole
            // question when a corpse falls through ground the camera is stopped by.
            // **Per model, because the world is model 0 and everything else is an entity.** A total
            // hides which model owns what, and the world's own hull is the one a corpse stands on.
            foreach (MapPhysicsModel model in
                BspPhysicsCollision.Read(BspLumpData.Read(file, header.Lump(PhysCollideLump))))
            {
                int ledges = 0;
                int triangles = 0;

                foreach (IReadOnlyList<PhysicsLedge> hull in model.Hulls)
                {
                    ledges += hull.Count;

                    foreach (PhysicsLedge ledge in hull)
                    {
                        triangles += ledge.Triangles.Count;
                    }
                }

                if (model.ModelIndex == 0 || ledges > 100)
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  model {model.ModelIndex}: declares {model.SolidCount} solids, " +
                        $"read {model.Solids.Count}, {ledges} ledges, {triangles} triangles"));

                    // **Per SOLID, with its extent.** A solid whose ledges all sit near the origin
                    // while its sibling spans the map is one that needs placing, and a total cannot
                    // show that.
                    for (int solid = 0; solid < model.Hulls.Count; solid++)
                    {
                        float lowX = float.MaxValue, lowY = float.MaxValue, lowZ = float.MaxValue;
                        float highX = float.MinValue, highY = float.MinValue, highZ = float.MinValue;

                        foreach (PhysicsLedge ledge in model.Hulls[solid])
                        {
                            foreach (System.Numerics.Vector3 point in ledge.Points)
                            {
                                // Through the production conversion, so this reports the space the
                                // world is actually built in rather than a second reading of it.
                                System.Numerics.Vector3 at = IvpWorldCollision.ToSource(point);

                                lowX = MathF.Min(lowX, at.X);
                                lowY = MathF.Min(lowY, at.Y);
                                lowZ = MathF.Min(lowZ, at.Z);
                                highX = MathF.Max(highX, at.X);
                                highY = MathF.Max(highY, at.Y);
                                highZ = MathF.Max(highZ, at.Z);
                            }
                        }

                        output.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"    solid {solid}: {model.Hulls[solid].Count} ledges, " +
                            $"x {lowX:0}..{highX:0}, y {lowY:0}..{highY:0}, z {lowZ:0}..{highZ:0}"));
                    }
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  displacements: {level.Terrain?.Count ?? 0} declared by the lump, " +
                $"{level.Displacements.Count} built, " +
                $"{level.Displacements.TriangleCount} triangles"));

            float terrain = level.Displacements.Sweep(
                spot.X, spot.Y, spot.Z, spot.X, spot.Y, spot.Z - 512f, halfExtent: 1f);

            System.Numerics.Vector3 from = new(spot.X, spot.Y, spot.Z);
            System.Numerics.Vector3 to = new(spot.X, spot.Y, spot.Z - 512f);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  under ({spot.X}, {spot.Y}, {spot.Z}): " +
                $"{world.Ledges.Count} ledges and {world.TriangleCount} triangles in the world; " +
                $"{(world.Sweep(from, to) is { } face ? $"HIT at {face.Fraction:0.###}, normal {face.Normal.X:0.##} {face.Normal.Y:0.##} {face.Normal.Z:0.##}" : "NOTHING")} " +
                $"within 512 units down; the camera's own sweep stops at " +
                $"{swept.ToString("0.###", CultureInfo.InvariantCulture)} of the way " +
                $"(terrain alone {terrain.ToString("0.###", CultureInfo.InvariantCulture)})"));

            // **Is a missing floor the FILE's or the READER's?** The tree walk and a linear step
            // through the same bytes are independent routes to the same ledges, so they disagree
            // only if one of them is wrong — see LinearLedgeCount.
            foreach (MapPhysicsModel counted in
                BspPhysicsCollision.Read(BspLumpData.Read(file, header.Lump(PhysCollideLump))))
            {
                if (counted.ModelIndex != 0)
                {
                    continue;
                }

                for (int solid = 0; solid < counted.Solids.Count; solid++)
                {
                    PhysicsBrushSolid where = counted.Solids[solid];

                    _ = PhysicsHull.Read(
                        BspLumpData.Read(file, header.Lump(PhysCollideLump))
                            .Span.Slice(where.Offset, where.Length),
                        out int dropped);

                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  model 0 solid {solid}: {counted.Hulls[solid].Count} ledges read, " +
                        $"{dropped} LEAVES DROPPED (a dropped leaf is a hole in the floor)"));
                }
            }

            // **The census, because one point has no denominator.** A single spot where a corpse
            // falls through says nothing about whether this project's physics world is broadly
            // right or broadly wrong. Dropping a ray at every node of a grid and comparing what the
            // physics world finds against what the CAMERA's own sweep finds does — and the camera's
            // world is built from entirely different lumps, so it is a real control rather than a
            // second reading of the same bytes.
            //
            // **It compares WHERE each world stops, not whether each stops, and starts AT the point
            // rather than above it. Both corrections were needed and each hid the other.** Asking
            // only "did something get hit" reported 1089 of 1089 agreeing at a spot where a corpse
            // free-falls a thousand units; starting 256 units higher then made the first surface
            // the ROOF over the point, so every ray answered a question about a ceiling. A test
            // whose condition lets a correct world and a broken one predict the same observation is
            // insensitive to the manipulation, and this one was, twice over.
            //
            // **A one-unit box against a point is the residual difference between the two**, so the
            // tolerance is above that and any disagreement reported here is geometry.
            int agree = 0;
            int cameraOnly = 0;
            int physicsOnly = 0;
            int neither = 0;
            int apart = 0;
            int physicsLower = 0;
            int overTerrain = 0;
            float worst = 0f;
            float worstX = 0f;
            float worstY = 0f;

            int[] deepest = new int[10];

            for (int gx = -16; gx <= 16; gx++)
            {
                for (int gy = -16; gy <= 16; gy++)
                {
                    float px = spot.X + (gx * 64f);
                    float py = spot.Y + (gy * 64f);

                    System.Numerics.Vector3 high = new(px, py, spot.Z);
                    System.Numerics.Vector3 low = new(px, py, spot.Z - Drop);

                    (System.Numerics.Vector3 Normal, float Fraction)? hit = world.Sweep(high, low);

                    float stopped = level.Sweep(
                        (px, py, spot.Z), (px, py, spot.Z - Drop), halfExtent: 1f);

                    if (level.Displacements.Sweep(
                        px, py, spot.Z, px, py, spot.Z - Drop, halfExtent: 1f) < 1f)
                    {
                        overTerrain++;
                    }

                    bool physics = hit is not null;
                    bool camera = stopped < 1f;

                    // **Where the camera stopped, on the nodes the two worlds disagree about.** A
                    // leaf-contents test stops on the void outside the map as readily as on a
                    // floor, so a drop that misses real ground and leaves the map counts as a hit —
                    // and that confound is worth exactly as much as this histogram says it is.
                    if (camera && !physics)
                    {
                        deepest[Math.Min(9, (int)(stopped * 10f))]++;
                    }

                    if (physics && camera)
                    {
                        // **In UNITS, because a fraction of a 1,280-unit drop hides a floor.** The
                        // two worlds are the same map; a surface either world has that the other
                        // lacks shows up here as a gap, and eight units is under the slop a
                        // resting contact already leaves.
                        float signed = (hit!.Value.Fraction - stopped) * Drop;
                        float gap = MathF.Abs(signed);

                        if (gap > Apart)
                        {
                            apart++;

                            if (signed > 0f)
                            {
                                physicsLower++;
                            }

                            if (gap > worst)
                            {
                                worst = gap;
                                worstX = px;
                                worstY = py;
                            }
                        }
                        else
                        {
                            agree++;
                        }
                    }
                    else if (camera)
                    {
                        cameraOnly++;
                    }
                    else if (physics)
                    {
                        physicsOnly++;
                    }
                    else
                    {
                        neither++;
                    }
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  census of 1089 drops around the point: {agree} agree within {Apart:0} units, " +
                $"{apart} stop more than that apart, {cameraOnly} camera only, " +
                $"{physicsOnly} physics only, {neither} neither; " +
                $"{overTerrain} of the columns have terrain under them at all"));

            if (apart > 0)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  worst disagreement {worst:0.#} units, at ({worstX:0}, {worstY:0}); " +
                    $"the physics world stops LOWER on {physicsLower} of {apart} " +
                    $"(a floor the corpse world is missing)"));
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  where the camera stopped on the {cameraOnly} it found alone, by tenth of the " +
                $"drop: {string.Join(' ', deepest)}"));

            // **And the same question over the WHOLE map, because the shape of the holes names the
            // cause.** A missing subtree of the ledge tree would leave a coherent region unserved;
            // a reader that drops the odd ledge would leave holes scattered everywhere. One
            // neighbourhood cannot tell those apart and this can, for the cost of one more pass.
            //
            // **The floor is what is counted, not any surface.** A column is a hole when the
            // physics world finds nothing above `Floor` while the camera's world does — so a roof
            // over the point, which is what defeated the first census, cannot register.
            int columns = 0;
            int rims = 0;

            // Read once rather than per column: the list is the same every time and rebuilding it
            // inside the scan turns a linear pass into a quadratic one.
            IReadOnlyList<DisplacementTriangle> ground = level.Displacements.Triangles();
            int holes = 0;

            for (int gx = -Across; gx <= Across; gx++)
            {
                for (int gy = -Across; gy <= Across; gy++)
                {
                    float px = gx * Apart * 2f;
                    float py = gy * Apart * 2f;

                    System.Numerics.Vector3 sky = new(px, py, Ceiling);
                    System.Numerics.Vector3 pit = new(px, py, Floor);

                    if (level.Sweep((px, py, Ceiling), (px, py, Floor), halfExtent: 1f) >= 1f)
                    {
                        continue;
                    }

                    columns++;

                    if (world.Sweep(sky, pit) is not null)
                    {
                        continue;
                    }

                    holes++;

                    // **How far a hole is from the nearest terrain, which says what KIND of hole it
                    // is.** A gap a few units from a displacement's edge is the rim of the brush
                    // that displacement was built on — vbsp takes those brushes out of
                    // `LUMP_PHYSCOLLIDE` and hands the ground to the virtual mesh, so vphysics has
                    // no collision there either and neither should we. A gap in open ground, far
                    // from any terrain, is ours.
                    float away = float.MaxValue;

                    for (int corner = 0; corner < ground.Count; corner++)
                    {
                        (float X, float Y, float Z) at = ground[corner].A;

                        away = MathF.Min(
                            away,
                            ((at.X - px) * (at.X - px)) + ((at.Y - py) * (at.Y - py)));
                    }

                    if (away <= Rim * Rim)
                    {
                        rims++;
                    }
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  over the whole map: {holes} of {columns} columns that the camera's world floors " +
                $"have no floor at all in the physics world, and {rims} of those are within " +
                $"{Rim:0} units of a terrain vertex"));

            // **The static props near the point, because their collision is in NEITHER lump.** A
            // `prop_static` is a model placed by the map, and the engine builds a physics object
            // for it out of that model's own `.phy` at level load — nothing about it appears in
            // `LUMP_PHYSCOLLIDE`, which holds brushes. A corpse resting on a wooden platform that
            // is a prop has nothing under it in a world built from brushes and terrain alone.
            foreach (BspStaticProp prop in BspStaticProps.Read(file))
            {
                float away = MathF.Sqrt(
                    ((prop.X - spot.X) * (prop.X - spot.X)) +
                    ((prop.Y - spot.Y) * (prop.Y - spot.Y)) +
                    ((prop.Z - spot.Z) * (prop.Z - spot.Z)));

                if (away < 200f)
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"  static prop {away.ToString("0", CultureInfo.InvariantCulture)} units away: " +
                        $"{prop.Model} at {prop.X:0} {prop.Y:0} {prop.Z:0}"));
                }
            }

            // **And whether the point is already INSIDE something, which `Entry` cannot say.** A
            // segment that begins inside a convex piece never crosses into it, so the sweep reports
            // clear — the same answer it gives for empty space. Asking both is what separates "no
            // geometry here" from "the corpse is standing in it".
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  at the point itself: " +
                $"{(world.Penetration(from) is { } deep ? $"INSIDE by {deep.Depth:0.##}, normal {deep.Normal.X:0.##} {deep.Normal.Y:0.##} {deep.Normal.Z:0.##}" : "outside everything")}"));

            // **How many faces the ledges have, across the whole world.** A compiled brush is
            // rarely a plain box, so a world whose ledges are all six-sided is one whose detail has
            // been lost somewhere between the file and here — and a world with a healthy spread of
            // face counts is one where a missing floor has to be explained by something else. It
            // costs one pass over a list that is already built.
            Dictionary<int, int> faces = [];

            for (int index = 0; index < world.Ledges.Count; index++)
            {
                int count = world.Ledges[index].Planes.Count;

                faces[count] = faces.GetValueOrDefault(count) + 1;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  ledges by face count: {string.Join(", ", faces.OrderBy(each => each.Key).Take(Listed).Select(each => $"{each.Key}:{each.Value}"))}" +
                $"{(faces.Count > Listed ? $", and {faces.Count - Listed} more counts" : string.Empty)}"));

            // **The ledges whose own bounding sphere reaches the column, listed.** This is what
            // separates the two explanations for a missing floor and nothing else does: if the
            // ledges are simply absent then the reader dropped them, and if they are present then
            // the sweep is failing to find geometry that is right there. Reported through
            // `world.Ledges` — the list the solver queries — rather than by re-reading the lump.
            int reaching = 0;

            foreach (IvpWorldLedge ledge in world.Ledges)
            {
                float acrossX = ledge.Center.X - spot.X;
                float acrossY = ledge.Center.Y - spot.Y;

                if ((acrossX * acrossX) + (acrossY * acrossY) > ledge.Radius * ledge.Radius)
                {
                    continue;
                }

                reaching++;

                if (reaching <= Listed)
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"    ledge reaching the column: centre " +
                        $"({ledge.Center.X:0}, {ledge.Center.Y:0}, {ledge.Center.Z:0}) " +
                        $"radius {ledge.Radius:0.#}, {ledge.Planes.Count} planes, " +
                        $"contents {ledge.Contents}"));
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {reaching} of {world.Ledges.Count} ledges have a bounding sphere over " +
                $"({spot.X:0}, {spot.Y:0})"));

            // **And the same question of the RAW lump, by the ledges' own points.** The sphere test
            // above is what the solver culls by, so it can only say the solver found nothing; this
            // says whether the geometry is in the file at all. A ledge whose own point box spans
            // the column and whose top is near where the camera stopped is one the world build
            // dropped; no such ledge means `LUMP_PHYSCOLLIDE` genuinely does not cover the spot and
            // the floor the camera stops on is something else.
            int spanning = 0;
            int standing = 0;

            foreach (MapPhysicsModel model in
                BspPhysicsCollision.Read(BspLumpData.Read(file, header.Lump(PhysCollideLump))))
            {
                for (int hull = 0; hull < model.Hulls.Count; hull++)
                {
                    IReadOnlyList<PhysicsLedge> hulls = model.Hulls[hull];

                    for (int index = 0; index < hulls.Count; index++)
                    {
                        PhysicsLedge ledge = hulls[index];

                        (System.Numerics.Vector3 Low, System.Numerics.Vector3 High) box =
                            Box(ledge.Points);

                        if (box.High.Z > AboveGround)
                        {
                            standing++;
                        }

                        if (box.Low.X > spot.X + Near || box.High.X < spot.X - Near ||
                            box.Low.Y > spot.Y + Near || box.High.Y < spot.Y - Near)
                        {
                            continue;
                        }

                        spanning++;

                        if (spanning <= Listed)
                        {
                            output.WriteLine(string.Create(
                                CultureInfo.InvariantCulture,
                                $"    lump ledge near the column: model {model.ModelIndex} " +
                                $"solid {hull}, {ledge.Points.Count} points, " +
                                $"x {box.Low.X:0}..{box.High.X:0}, " +
                                $"y {box.Low.Y:0}..{box.High.Y:0}, " +
                                $"z {box.Low.Z:0.#}..{box.High.Z:0.#}"));
                        }
                    }
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {spanning} lump ledges have their own points within {Near:0} units of the " +
                $"column, at any height; {standing} of all of them reach above z {AboveGround:0}"));

            // **The ledges' own denominator, from LUMP_BRUSHES.** vbsp walks the world's leaves,
            // takes every brush they reference whose contents match, and writes one convex per
            // brush — `VisitLeaves_r( planes, dmodels[0].headnode ); planes.AddBrushes();`
            // (`ivp.cpp:1278-1279`). So the ledge count should track the count of solid world
            // brushes, allowing for the merge; a count far below it means this project's reader is
            // dropping geometry, and one near it means the lump holds what it holds.
            ReadOnlySpan<byte> brushes =
                BspLumpData.Read(file, header.Lump(BrushesLump)).Span;

            int solidBrushes = 0;
            int clipBrushes = 0;

            for (int at = 0; at + BrushStride <= brushes.Length; at += BrushStride)
            {
                int contents = BinaryPrimitives.ReadInt32LittleEndian(brushes[(at + 8)..]);

                if ((contents & IvpWorldCollision.MaskSolid) != 0)
                {
                    solidBrushes++;
                }
                else if ((contents & PlayerClip) != 0)
                {
                    clipBrushes++;
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  LUMP_BRUSHES declares {solidBrushes} solid and {clipBrushes} playerclip " +
                $"brushes, against {world.Ledges.Count} ledges read"));

            // **And the terrain, asked the same way.** `virtualterrain {}` in the world's own
            // KeyValues says the displacements are NOT in this lump — the engine builds them into a
            // virtual mesh at load — so a column with no ledge under it is only a hole if there is
            // no displacement over it either. Nearest by distance in the plane, which is the
            // question "is the ground here made of terrain" and not "did the sweep hit".
            float nearest = float.MaxValue;
            float nearestZ = 0f;
            int listedTriangles = 0;
            DisplacementTriangle closest = default;

            foreach (DisplacementTriangle triangle in level.Displacements.Triangles())
            {
                (float X, float Y, float Z) middle = (
                    (triangle.A.X + triangle.B.X + triangle.C.X) / 3f,
                    (triangle.A.Y + triangle.B.Y + triangle.C.Y) / 3f,
                    (triangle.A.Z + triangle.B.Z + triangle.C.Z) / 3f);

                float across = ((middle.X - spot.X) * (middle.X - spot.X)) +
                    ((middle.Y - spot.Y) * (middle.Y - spot.Y));

                if (across < nearest)
                {
                    nearest = across;
                    nearestZ = middle.Z;
                    closest = triangle;
                }

                // **The SHAPE of the coverage edge, not just its distance.** A single nearest
                // triangle says the ground stops somewhere; the corners of every triangle around
                // the column say which way it stops and whether the edge is a displacement's own
                // boundary or a ragged line through the middle of one.
                if (across < Near * Near && listedTriangles < Listed)
                {
                    listedTriangles++;

                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"    terrain near the column: " +
                        $"({triangle.A.X:0}, {triangle.A.Y:0}, {triangle.A.Z:0}) " +
                        $"({triangle.B.X:0}, {triangle.B.Y:0}, {triangle.B.Z:0}) " +
                        $"({triangle.C.X:0}, {triangle.C.Y:0}, {triangle.C.Z:0})"));
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  nearest terrain triangle: {MathF.Sqrt(nearest):0.#} units away in the plane, " +
                $"its middle at z {nearestZ:0.#}; corners " +
                $"({closest.A.X:0}, {closest.A.Y:0}, {closest.A.Z:0}) " +
                $"({closest.B.X:0}, {closest.B.Y:0}, {closest.B.Z:0}) " +
                $"({closest.C.X:0}, {closest.C.Y:0}, {closest.C.Z:0})"));

            // **The control, and it is not optional here.** "The terrain sweep found nothing" is
            // the same observation for a genuine gap in the ground and for a containment test that
            // rejects points it should accept, and only asking the sweep for something that MUST be
            // there separates them: a ray dropped through the middle of a triangle the set already
            // holds. A miss on that means the instrument is broken and every terrain reading above
            // is worthless (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md#an-empty-search-needs-a-control`).
            float middleX = (closest.A.X + closest.B.X + closest.C.X) / 3f;
            float middleY = (closest.A.Y + closest.B.Y + closest.C.Y) / 3f;

            float onIt = level.Displacements.Sweep(
                middleX, middleY, nearestZ + Overhead,
                middleX, middleY, nearestZ - Overhead,
                halfExtent: 1f);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  control, dropped through that triangle's own middle: " +
                $"{(onIt < 1f ? $"HIT at {onIt:0.###}" : "MISSED, so the terrain sweep is the fault")}"));

            // **The terrain's own denominator, from the powers the lump declares.** A displacement
            // of power N is a grid of 2^N by 2^N quads and therefore exactly 2 * 4^N triangles, so
            // what the map ASKS for is arithmetic rather than a second measurement — and a count
            // that falls short of it names missing ground directly, where "the sweep found nothing"
            // cannot say whether the triangle is absent or the test is wrong.
            int wanted = 0;
            int lowest = int.MaxValue;
            int highest = 0;
            int disallowed = 0;
            int wasDisallowed = 0;
            int stitched = 0;

            ReadOnlySpan<byte> infos =
                BspLumpData.Read(file, header.Lump(DispInfoLump)).Span;

            for (int at = 0; at + DispInfoStride <= infos.Length; at += DispInfoStride)
            {
                int power = BinaryPrimitives.ReadInt32LittleEndian(infos[(at + DispPowerOffset)..]);

                if (power is < 2 or > 4)
                {
                    continue;
                }

                wanted += 2 << (2 * power);
                lowest = Math.Min(lowest, power);
                highest = Math.Max(highest, power);

                // **`m_AllowedVerts` is how Valve stops two displacements of different power from
                // parting company along their shared edge**, and it is the last 40 bytes of the
                // struct: *"This is built based on the layout and sizes of our neighbors and tells
                // us which vertices are allowed to be active"* (`bspfile.h:665`).
                // `TesselateDisplacement` reads it per vertex and skips the ones that are off, so a
                // fine displacement next to a coarse one drops its extra edge verts and meets the
                // neighbour exactly. A uniform grid keeps them, and the two edges then differ by
                // however far the fine one bulges.
                int side = (1 << power) + 1;

                for (int vertex = 0; vertex < side * side; vertex++)
                {
                    int word = at + AllowedVertsOffset + ((vertex >> 5) * 4);

                    if (word + 4 > infos.Length)
                    {
                        break;
                    }

                    if ((BinaryPrimitives.ReadUInt32LittleEndian(infos[word..]) &
                        (1u << (vertex & 31))) == 0)
                    {
                        disallowed++;
                    }
                }

                if (disallowed > wasDisallowed)
                {
                    stitched++;
                    wasDisallowed = disallowed;
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  terrain triangles: {level.Displacements.TriangleCount} built against " +
                $"{wanted} the lump's powers ask for, over " +
                $"{infos.Length / DispInfoStride} displacements of power " +
                $"{(lowest > highest ? 0 : lowest)}..{highest}; " +
                $"{disallowed} vertices are NOT allowed to be active, over {stitched} " +
                $"displacements that are stitched to a coarser neighbour"));
        }

        return walked;
    }

    /// <summary>A point list's bounding box, through the production conversion into Source units.</summary>
    /// <param name="points">The ledge's points, in IVP metres.</param>
    /// <returns>The lowest and highest corner, in Source units.</returns>
    private static (System.Numerics.Vector3 Low, System.Numerics.Vector3 High) Box(
        IEnumerable<System.Numerics.Vector3> points)
    {
        System.Numerics.Vector3 low = new(float.MaxValue);
        System.Numerics.Vector3 high = new(float.MinValue);

        foreach (System.Numerics.Vector3 point in points)
        {
            System.Numerics.Vector3 at = IvpWorldCollision.ToSource(point);

            low = System.Numerics.Vector3.Min(low, at);
            high = System.Numerics.Vector3.Max(high, at);
        }

        return (low, high);
    }

    /// <summary>Walks the lump's chain of models.</summary>
    /// <param name="output">Where to report.</param>
    /// <param name="name">The map's file name.</param>
    /// <param name="lump">The lump's bytes.</param>
    /// <param name="verbose">Whether to print the per-map detail.</param>
    /// <returns>How many models and solids were found.</returns>
    /// <remarks>
    /// **Every read is bounds-checked, because a `.bsp` is a stranger's file** (D32). Maps arrive
    /// from fastdl, supplied by whoever runs the server and reviewed by nobody, and this walk is
    /// driven entirely by lengths the file itself declares — which is the exact shape of the
    /// allocate-before-validate defects already fixed elsewhere here.
    /// </remarks>
    private static (int Models, int Solids) Walk(
        TextWriter output,
        string name,
        ReadOnlySpan<byte> lump,
        bool verbose)
    {
        int at = 0;
        int models = 0;
        int solids = 0;
        int textBytes = 0;
        string firstText = string.Empty;

        while (at + ModelHeaderSize <= lump.Length)
        {
            int modelIndex = BinaryPrimitives.ReadInt32LittleEndian(lump[at..]);
            int dataSize = BinaryPrimitives.ReadInt32LittleEndian(lump[(at + 4)..]);
            int keydataSize = BinaryPrimitives.ReadInt32LittleEndian(lump[(at + 8)..]);
            int solidCount = BinaryPrimitives.ReadInt32LittleEndian(lump[(at + 12)..]);

            at += ModelHeaderSize;

            // Valve's own terminator: "The last physmodel is a NULL pointer with modelIndex -1,
            // dataSize -1" (`bsplib.cpp:1575`). Tested before the sizes are trusted for anything.
            if (modelIndex == -1 && dataSize == -1)
            {
                break;
            }

            if (dataSize < 0 || keydataSize < 0 || solidCount < 0 ||
                (long)at + dataSize + keydataSize > lump.Length)
            {
                output.WriteLine(
                    $"{name}: a physics model declares sizes past the end of the lump — stopping.");

                break;
            }

            models++;
            solids += solidCount;

            if (firstText.Length == 0 && keydataSize > 0)
            {
                firstText = Encoding.ASCII
                    .GetString(lump.Slice(at + dataSize, Math.Min(keydataSize, 1200)))
                    .ReplaceLineEndings("\n      ");
            }

            textBytes += keydataSize;
            at += dataSize + keydataSize;
        }

        if (verbose)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: {models} physics models, {solids} solids, " +
                $"{textBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes of KeyValues text"));

            // **The hulls, through the PRODUCTION reader rather than this probe's own walk.** The
            // walk above answers "is the lump shaped the way Valve's loader says"; this answers
            // "does the thing the viewer will collide against read", and a probe that reimplemented
            // the second would only ever agree with itself.
            int ledges = 0;
            int triangles = 0;
            int empty = 0;

            // **Does every ledge's own point lie inside the sphere its tree node carries?** That is
            // the test that decides whether `node+0x08` is a centre and `node+0x14` a radius, or
            // twenty bytes read as a plausible number. A broadphase built on a wrong reading would
            // reject real contacts, and nothing on screen would say so — a corpse would fall through
            // the floor in some places and not others.
            int inside = 0;
            int outside = 0;
            float worst = 0f;

            foreach (MapPhysicsModel model in BspPhysicsCollision.Read(lump.ToArray()))
            {
                foreach (IReadOnlyList<PhysicsLedge> hull in model.Hulls)
                {
                    if (hull.Count == 0)
                    {
                        empty++;
                    }

                    ledges += hull.Count;

                    foreach (PhysicsLedge ledge in hull)
                    {
                        triangles += ledge.Triangles.Count;

                        foreach (System.Numerics.Vector3 point in ledge.Points)
                        {
                            float distance = (point - ledge.Center).Length();

                            if (distance <= ledge.Radius)
                            {
                                inside++;
                            }
                            else
                            {
                                outside++;
                                worst = Math.Max(worst, distance - ledge.Radius);
                            }
                        }
                    }
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  hulls: {ledges.ToString("N0", CultureInfo.InvariantCulture)} ledges, " +
                $"{triangles.ToString("N0", CultureInfo.InvariantCulture)} triangles, " +
                $"{empty} solids that read as nothing"));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  node spheres: {inside.ToString("N0", CultureInfo.InvariantCulture)} points inside, " +
                $"{outside.ToString("N0", CultureInfo.InvariantCulture)} outside, " +
                $"worst overshoot {worst.ToString("0.######", CultureInfo.InvariantCulture)}"));

            // **What is UNDER a given point, which is the question a corpse asks.** A body that
            // free-falls from its death position while its neighbours land is either standing where
            // this project's world has nothing, or standing on something it cannot see — and those
            // two are indistinguishable from the corpse's own behaviour.

            if (firstText.Length > 0)
            {
                output.WriteLine("  --- the first model's text, verbatim ---");
                output.WriteLine($"      {firstText}");
                output.WriteLine("  --- ends ---");
            }
        }

        return (models, solids);
    }

}
