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
                $"{(world.Entry(from, to) is { } face ? $"HIT, normal {face.X:0.##} {face.Y:0.##} {face.Z:0.##}" : "NOTHING")} " +
                $"within 512 units down; the camera's own sweep stops at " +
                $"{swept.ToString("0.###", CultureInfo.InvariantCulture)} of the way " +
                $"(terrain alone {terrain.ToString("0.###", CultureInfo.InvariantCulture)})"));

            // **The census, because one point has no denominator.** A single spot where a corpse
            // falls through says nothing about whether this project's physics world is broadly
            // right or broadly wrong. Dropping a ray at every node of a grid and comparing what the
            // physics world finds against what the CAMERA's own sweep finds does — and the camera's
            // world is built from entirely different lumps, so it is a real control rather than a
            // second reading of the same bytes.
            int both = 0;
            int cameraOnly = 0;
            int physicsOnly = 0;
            int neither = 0;

            int[] deepest = new int[10];

            for (int gx = -16; gx <= 16; gx++)
            {
                for (int gy = -16; gy <= 16; gy++)
                {
                    float px = spot.X + (gx * 64f);
                    float py = spot.Y + (gy * 64f);

                    System.Numerics.Vector3 high = new(px, py, spot.Z + 256f);
                    System.Numerics.Vector3 low = new(px, py, spot.Z - 1024f);

                    bool physics = world.Entry(high, low) is not null;
                    float stopped = level.Sweep(
                        (px, py, spot.Z + 256f), (px, py, spot.Z - 1024f), halfExtent: 1f);

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
                        both++;
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
                $"  census of 1089 drops around the point: {both} both, {cameraOnly} camera only, " +
                $"{physicsOnly} physics only, {neither} neither"));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  where the camera stopped on the {cameraOnly} it found alone, by tenth of the " +
                $"drop: {string.Join(' ', deepest)}"));

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
        }

        return walked;
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
