using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// A REAL ragdoll dropped on REAL map collision, with no viewer in the way (B58).
/// </summary>
/// <remarks>
/// **This exists because a synthetic fixture was steering parity decisions, which is backwards.**
/// The owner: *"it has to be a parity issue, you need to actually run the demo to check or make
/// synthetic test demos that run"*. A two-unit cube on a single tilted triangle is a shape this
/// project invented; the engine's behaviour is what a seventeen-body player ragdoll does on a
/// compiled map, and an afternoon went into tuning against the cube instead.
///
/// **The other instrument was the viewer, and it costs ninety seconds a run.** It loads textures,
/// materials, lightmaps and models before a corpse is stepped at all, takes the desktop, and
/// orphans itself when killed — three separate "hangs" this session turned out to be a viewer
/// detached from the wrapper that launched it, or a wait on the machine-wide lock. None of that is
/// needed to ask where a corpse comes to rest.
///
/// **So this runs the production path and nothing else**: `MapLevel.Read` for the collision the
/// viewer uses, `RagdollBody.Build` for the bodies and joints a `.phy` declares, and
/// `RagdollSimulation` stepped at the demo's own tick rate. Seconds, deterministic, no window.
///
/// <code>
///   corpse-drop                                  koth_harvest_final, scout, the measured deaths
///   corpse-drop &lt;map&gt; &lt;model&gt; x y z [fx fy fz]   one drop, at a place you choose
/// </code>
///
/// **The static props ARE here, and they were not at first — that was a wrong answer.**
/// `MapLevel.Read` builds brushes and terrain only; a prop needs a `.phy` each, which needs the
/// pakfile and the archives, so `LoadedMap` adds them and this skipped them. 456 of 456 solid props
/// on `koth_harvest_final` were missing, and a corpse seeded among the mining crates fell through
/// where the viewer rests it at z 4.8.
///
/// **The 2277 seed is a KNOWN-WRONG default, kept because the wrongness is itself the finding —
/// see `docs/findings/51`.** That seed is where the demo's ragdoll SPAWNS, not where it comes to
/// rest, and dropping it from REST at a spawn point tests a fall the real corpse never makes: the
/// real one carries momentum through that spot already and lands elsewhere. This probe reports it
/// leaving the world, and that is a fact about this SYNTHETIC drop, not a physics divergence the
/// viewer's own run of `z1800` exhibits. Read "5 seeds, 4 settle" as "4 of the 4 seeds that are
/// genuinely rest points settle" — the fifth answers a question nobody is asking.
/// </remarks>
public sealed class CorpseDropProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "corpse-drop";

    /// <inheritdoc/>
    public string Summary =>
        "where a real ragdoll settles on real map collision: corpse-drop [map model x y z]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine("The game is not installed, so no model can be read.");
            return;
        }

        string mapName = arguments.Count > 0 ? arguments[0] : "koth_harvest_final";
        string model = arguments.Count > 1 ? arguments[1] : "models/player/scout.mdl";

        if (locator.Find(mapName) is not { } mapPath)
        {
            output.WriteLine($"No map named '{mapName}'.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        if (RagdollFor(game, model) is not { } ragdoll)
        {
            output.WriteLine($"{model}: no ragdoll.");
            return;
        }

        byte[] map = File.ReadAllBytes(mapPath);

        MapLevel level = MapLevel.Read(map, NullLogger.Instance);

        // **The static props go in here too, or this instrument lies.** `MapLevel.Read` builds
        // brushes and terrain; the props need a `.phy` each, which needs the pakfile and the
        // archives, so `LoadedMap` adds them and this used to skip them. That cost a wrong answer:
        // a corpse seeded at (−972.6, −1400.3, 77.5) fell to −4359 here and rests at z 4.8 in the
        // viewer, because the mining crates it lands on were absent. An instrument that disagrees
        // with the thing it is standing in for is worth less than no instrument.
        // **Before the props, because two maps reported the SAME extreme ledges to the unit** and
        // only one population is shared between maps: the prop models. Whether the world's own
        // ledges already reach those coordinates decides which half is misplaced (B400).
        int brushLedges = level.Physics.Ledges.Count;
        Vector3 brushLow = new(float.MaxValue), brushHigh = new(float.MinValue);

        foreach (Vector3 centre in level.Physics.Ledges.Select(ledge => ledge.Center))
        {
            brushLow = Vector3.Min(brushLow, centre);
            brushHigh = Vector3.Max(brushHigh, centre);
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  before props: {brushLedges} ledges, centres x {brushLow.X:0}..{brushHigh.X:0} " +
            $"y {brushLow.Y:0}..{brushHigh.Y:0} z {brushLow.Z:0}..{brushHigh.Z:0}"));

        PakFile pak = PakFile.ReadFrom(map);

        (int placed, int solid) = MapPropCollision.Add(
            level.Physics,
            map,
            file => Read(pak, game.Archives, file),
            NullLogger.Instance);

        output.WriteLine(
            $"{mapName}: {level.Physics.Ledges.Count} ledges ({placed} of {solid} solid props), " +
            $"{level.Physics.TriangleCount} terrain triangles; " +
            $"{model} has {ragdoll.Elements.Count} bodies");

        foreach ((float X, float Y, float Z, float FX, float FY, float FZ) at in Places(arguments))
        {
            Drop(
                output, level, ragdoll, game.Surfaces,
                (at.X, at.Y, at.Z), (at.FX, at.FY, at.FZ), map);
        }
    }

    /// <summary>Names the BSP brush that floors a column, and looks for it among the ledges.</summary>
    /// <remarks>
    /// **The last fork in B400.** Three readings agree that a column is floored — the brush clip,
    /// the leaf walk and the owner's own game — and no ledge in the whole collide contains the
    /// point. Either that brush never became a ledge, or its ledge is somewhere else. Printing the
    /// brush's own planes and asking how many ledges carry each of them settles which.
    ///
    /// **A second reader of `LUMP_BRUSHES`, deliberately.** `BspLeafTree` walks it through the
    /// tree; this walks it linearly. Two routes to one answer is the control that makes a
    /// disagreement mean something.
    /// </remarks>
    private static void Brush(TextWriter output, byte[] map, Vector3 under, MapLevel level)
    {
        BspHeader header = BspHeader.Parse(map);

        ReadOnlySpan<byte> brushes = BspLumpData.Read(map, header.Lump(BrushesLump)).Span;
        ReadOnlySpan<byte> sides = BspLumpData.Read(map, header.Lump(BrushSidesLump)).Span;
        ReadOnlySpan<byte> planes = BspLumpData.Read(map, header.Lump(PlanesLump)).Span;

        for (int at = 0, index = 0; at + BrushStride <= brushes.Length; at += BrushStride, index++)
        {
            int first = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(brushes[at..]);
            int count =
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(brushes[(at + 4)..]);
            int contents =
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(brushes[(at + 8)..]);

            List<(Vector3 Normal, float Distance)> faces = [];
            bool within = count > 0;
            int displaced = 0;

            for (int side = 0; side < count && within; side++)
            {
                int sideAt = (first + side) * BrushSideStride;

                if (sideAt < 0 || sideAt + BrushSideStride > sides.Length)
                {
                    within = false;
                    break;
                }

                int plane = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                    sides[sideAt..]) * PlaneStride;

                if (plane < 0 || plane + PlaneStride > planes.Length)
                {
                    within = false;
                    break;
                }

                Vector3 normal = new(
                    BitConverter.ToSingle(planes[plane..]),
                    BitConverter.ToSingle(planes[(plane + 4)..]),
                    BitConverter.ToSingle(planes[(plane + 8)..]));

                float distance = BitConverter.ToSingle(planes[(plane + 12)..]);

                // **`dispinfo` is why a brush can be solid to a trace and absent from the collide.**
                // A side carrying a displacement is not collided as a brush at all: vbsp leaves it
                // to the virtual terrain the engine builds from `LUMP_DISPINFO` at load
                // (`PhysCreateVirtualTerrain`), so the ledge nobody can find was never meant to
                // exist (B400).
                short displacement = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(
                    sides[(sideAt + 4)..]);

                if (displacement > 0)
                {
                    displaced++;
                }

                faces.Add((normal, distance));

                if (Vector3.Dot(normal, under) - distance > 0f)
                {
                    within = false;
                }
            }

            if (!within)
            {
                continue;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  BRUSH {index} contains it: contents 0x{contents:X}, {count} sides, " +
                $"{displaced} of them carrying a displacement"));

            foreach ((Vector3 normal, float distance) in faces)
            {
                // **Within 1.5, which is looser than the WORLD needs and was a wrong correction.**
                // `VPHYSICS_SHRINK 0.5` (`ivp.cpp:37`) applies to brush ENTITY models only; the
                // world is built with `NO_SHRINK` (`ivp.cpp:1531`), so an exact match was expected
                // all along and loosening this destroyed the signal that said so (B400).
                int carrying = level.Physics.Ledges.Count(ledge => ledge.Planes.Any(face =>
                    Vector3.Dot(face.Normal, normal) > 0.999f &&
                    MathF.Abs(face.Distance - distance) < 1.5f));

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    face ({normal.X:0.##}, {normal.Y:0.##}, {normal.Z:0.##}) d {distance:0.#} " +
                    $"-> {carrying} ledges carry it"));

                // **Where the carriers actually are.** If one of them is this brush translated,
                // the offset is the defect; if they are scattered across the map, they are other
                // brushes that happen to share an axis-aligned plane (B400).
                if (normal.Z > 0.9f)
                {
                    foreach (IvpWorldLedge carrier in level.Physics.Ledges
                        .Where(ledge => ledge.Planes.Any(face =>
                            Vector3.Dot(face.Normal, normal) > 0.999f &&
                            MathF.Abs(face.Distance - distance) < 1.5f))
                        .OrderBy(ledge => (ledge.Center - under).LengthSquared())
                        .Take(4))
                    {
                        output.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"      carrier at ({carrier.Center.X:0}, {carrier.Center.Y:0}, " +
                            $"{carrier.Center.Z:0}) radius {carrier.Radius:0.#} " +
                            $"planes {carrier.Planes.Count}"));
                    }
                }
            }

            // **Is any ledge THIS brush?** Sharing one axis-aligned plane means little on a map
            // built from boxes; sharing most of them is identity. The best match says whether the
            // brush reached the collide in some other shape or never reached it at all.
            int best = 0;

            foreach (IvpWorldLedge ledge in level.Physics.Ledges)
            {
                int shared = faces.Count(face => ledge.Planes.Any(carried =>
                    Vector3.Dot(carried.Normal, face.Normal) > 0.999f &&
                    MathF.Abs(carried.Distance - face.Distance) < 1.5f));

                best = Math.Max(best, shared);
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    the best-matching ledge carries {best} of this brush's {faces.Count} faces"));

            // **The nearest ledge's own planes, printed.** Two instruments disagreeing about a
            // match is a third question; the planes themselves answer it without a comparison.
            // **A box's middle from its own planes**: an axis-aligned face at `+n` puts the far
            // side at `d`, one at `-n` puts the near side at `-d`. Averaging `-n*d` over opposite
            // faces does NOT cancel and gave a point nowhere near the brush — an instrument fault
            // caught by the number looking wrong.
            Vector3 low = new(float.MinValue), high = new(float.MaxValue);

            foreach ((Vector3 normal, float distance) in faces)
            {
                if (normal.X > 0.9f) { high.X = distance; }
                else if (normal.X < -0.9f) { low.X = -distance; }
                else if (normal.Y > 0.9f) { high.Y = distance; }
                else if (normal.Y < -0.9f) { low.Y = -distance; }
                else if (normal.Z > 0.9f) { high.Z = distance; }
                else if (normal.Z < -0.9f) { low.Z = -distance; }
            }

            Vector3 middle = new(
                (low.X + high.X) / 2f, (low.Y + high.Y) / 2f, (low.Z + high.Z) / 2f);

            IvpWorldLedge nearest = level.Physics.Ledges
                .OrderBy(ledge => (ledge.Center - middle).LengthSquared())
                .First();

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    nearest ledge to the brush's middle ({middle.X:0}, {middle.Y:0}, " +
                $"{middle.Z:0}) sits at ({nearest.Center.X:0}, {nearest.Center.Y:0}, " +
                $"{nearest.Center.Z:0}) with {nearest.Planes.Count} planes:"));

            foreach ((Vector3 normal, float distance) in nearest.Planes.Take(8))
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"      ({normal.X:0.##}, {normal.Y:0.##}, {normal.Z:0.##}) d {distance:0.##}"));
            }

            // **Who actually OCCUPIES this brush's box, by vertex bounds.** A plane comparison
            // asks whether two descriptions agree; a bounds overlap asks whether anything at all
            // is there, which is the question a fall-through poses. Independent of the shrink,
            // of the normal direction, and of the match tolerance (B400).
            int overlapping = 0;

            foreach (IvpWorldLedge ledge in level.Physics.Ledges)
            {
                if (ledge.Vertices.Count == 0)
                {
                    continue;
                }

                Vector3 least = ledge.Vertices[0], most = ledge.Vertices[0];

                foreach (Vector3 vertex in ledge.Vertices)
                {
                    least = Vector3.Min(least, vertex);
                    most = Vector3.Max(most, vertex);
                }

                if (most.X < low.X || least.X > high.X ||
                    most.Y < low.Y || least.Y > high.Y ||
                    most.Z < low.Z || least.Z > high.Z)
                {
                    continue;
                }

                overlapping++;

                if (overlapping <= 6)
                {
                    output.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"      overlaps: x[{least.X:0.#},{most.X:0.#}] " +
                        $"y[{least.Y:0.#},{most.Y:0.#}] z[{least.Z:0.#},{most.Z:0.#}] " +
                        $"{ledge.Planes.Count} planes, {ledge.Vertices.Count} vertices"));
                }
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    brush box x[{low.X:0.#},{high.X:0.#}] y[{low.Y:0.#},{high.Y:0.#}] " +
                $"z[{low.Z:0.#},{high.Z:0.#}] overlaps {overlapping} ledge bounds"));
        }

        Census(output, brushes, sides, planes, level);
    }

    /// <summary>Axis-aligned box brushes against ledge bounds, by identity (B400).</summary>
    /// <remarks>
    /// **A plane comparison asks whether two descriptions agree; a box comparison asks whether the
    /// geometry is THERE.** It survives every doubt the plane census raised — the shrink, bevel
    /// sides inflating a brush's face count, the match tolerance and the normal direction — because
    /// a six-plane axis-aligned brush and its convex have the same eight corners, whatever the
    /// convex builder did with the planes in between.
    ///
    /// **Only axis-aligned six-sided brushes are counted**, deliberately: an oblique brush's convex
    /// has the same bounds as its brush too, but so does anything else that happens to occupy the
    /// same box, and a denominator that cannot be wrong is worth more than a bigger one.
    /// </remarks>
    private static void Boxes(
        TextWriter output,
        ReadOnlySpan<byte> brushes,
        ReadOnlySpan<byte> sides,
        ReadOnlySpan<byte> planes,
        MapLevel level)
    {
        List<(Vector3 Low, Vector3 High)> bounds = [];

        foreach (IReadOnlyList<Vector3> hull in level.Physics.Ledges
            .Select(ledge => ledge.Vertices)
            .Where(vertices => vertices.Count > 0))
        {
            Vector3 least = hull[0], most = hull[0];

            foreach (Vector3 vertex in hull)
            {
                least = Vector3.Min(least, vertex);
                most = Vector3.Max(most, vertex);
            }

            bounds.Add((least, most));
        }

        List<(Vector3 Low, Vector3 High)> brushBoxes = [];

        int boxes = 0;
        int found = 0;
        int listed = 0;

        for (int at = 0, index = 0; at + BrushStride <= brushes.Length; at += BrushStride, index++)
        {
            int first = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(brushes[at..]);
            int count =
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(brushes[(at + 4)..]);
            int contents =
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(brushes[(at + 8)..]);

            if ((contents & IvpWorldCollision.MaskSolid) == 0 || count != 6)
            {
                continue;
            }

            Vector3 low = new(float.MinValue), high = new(float.MaxValue);
            int axial = 0;

            for (int side = 0; side < 6; side++)
            {
                int sideAt = (first + side) * BrushSideStride;

                if (sideAt < 0 || sideAt + BrushSideStride > sides.Length)
                {
                    break;
                }

                int plane = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                    sides[sideAt..]) * PlaneStride;

                if (plane < 0 || plane + PlaneStride > planes.Length)
                {
                    break;
                }

                Vector3 normal = new(
                    BitConverter.ToSingle(planes[plane..]),
                    BitConverter.ToSingle(planes[(plane + 4)..]),
                    BitConverter.ToSingle(planes[(plane + 8)..]));

                float distance = BitConverter.ToSingle(planes[(plane + 12)..]);

                if (normal.X > 0.999f) { high.X = distance; axial++; }
                else if (normal.X < -0.999f) { low.X = -distance; axial++; }
                else if (normal.Y > 0.999f) { high.Y = distance; axial++; }
                else if (normal.Y < -0.999f) { low.Y = -distance; axial++; }
                else if (normal.Z > 0.999f) { high.Z = distance; axial++; }
                else if (normal.Z < -0.999f) { low.Z = -distance; axial++; }
            }

            if (axial != 6)
            {
                continue;
            }

            boxes++;
            brushBoxes.Add((low, high));

            bool present = false;

            foreach ((Vector3 least, Vector3 most) in bounds)
            {
                if ((least - low).Length() < 1f && (most - high).Length() < 1f)
                {
                    present = true;
                    break;
                }
            }

            if (present)
            {
                found++;
            }
            else if (++listed <= 8)
            {
                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    no ledge is BRUSH {index}: x[{low.X:0},{high.X:0}] " +
                    $"y[{low.Y:0},{high.Y:0}] z[{low.Z:0},{high.Z:0}] contents 0x{contents:X}"));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  boxes: {found} of {boxes} axis-aligned solid box brushes have a ledge with their " +
            $"exact bounds"));

        Conventions(output, brushBoxes, bounds);
    }

    /// <summary>Which axis convention makes the ledge boxes the brush boxes (B400).</summary>
    /// <remarks>
    /// **The box census answers "are they there"; this answers "where did they go".** IVP is a
    /// third convention — axes, units and handedness at once — and a map that is symmetric about an
    /// axis, which every 5CP map is, hides a mirror in every extent and every histogram. Counting
    /// exact box identities under each candidate transform cannot hide it: only the right one
    /// matches, and the identity transform is the control that says the instrument works at all.
    /// </remarks>
    private static void Conventions(
        TextWriter output,
        List<(Vector3 Low, Vector3 High)> brushBoxes,
        List<(Vector3 Low, Vector3 High)> bounds)
    {
        (string Name, Func<Vector3, Vector3> Map)[] candidates =
        [
            ("as read", point => point),
            ("mirror x", point => new Vector3(-point.X, point.Y, point.Z)),
            ("mirror y", point => new Vector3(point.X, -point.Y, point.Z)),
            ("mirror z", point => new Vector3(point.X, point.Y, -point.Z)),
            ("swap x y", point => new Vector3(point.Y, point.X, point.Z)),
            ("swap y z", point => new Vector3(point.X, point.Z, point.Y)),
            ("swap y z, mirror y", point => new Vector3(point.X, -point.Z, point.Y)),
            ("swap y z, mirror z", point => new Vector3(point.X, point.Z, -point.Y)),
            ("mirror y and z", point => new Vector3(point.X, -point.Y, -point.Z)),
            ("mirror x and y", point => new Vector3(-point.X, -point.Y, point.Z)),
            ("mirror x and z", point => new Vector3(-point.X, point.Y, -point.Z)),
        ];

        foreach ((string name, Func<Vector3, Vector3> map) in candidates)
        {
            HashSet<(int, int, int, int, int, int)> keys = [];

            foreach ((Vector3 least, Vector3 most) in bounds)
            {
                Vector3 first = map(least), second = map(most);

                keys.Add((
                    (int)MathF.Round(MathF.Min(first.X, second.X)),
                    (int)MathF.Round(MathF.Min(first.Y, second.Y)),
                    (int)MathF.Round(MathF.Min(first.Z, second.Z)),
                    (int)MathF.Round(MathF.Max(first.X, second.X)),
                    (int)MathF.Round(MathF.Max(first.Y, second.Y)),
                    (int)MathF.Round(MathF.Max(first.Z, second.Z))));
            }

            int matched = brushBoxes.Count(box => keys.Contains((
                (int)MathF.Round(box.Low.X), (int)MathF.Round(box.Low.Y),
                (int)MathF.Round(box.Low.Z), (int)MathF.Round(box.High.X),
                (int)MathF.Round(box.High.Y), (int)MathF.Round(box.High.Z))));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    {name}: {matched} of {brushBoxes.Count} box brushes matched"));
        }
    }

    /// <summary>Every solid brush, against the ledge set, by plane (B400).</summary>
    /// <remarks>
    /// **One column is an anecdote.** If most solid brushes have a ledge carrying their faces and a
    /// minority have none, the minority is the defect and its shared property is the lead. Plane
    /// buckets keep it cheap: a ledge files each of its faces once, and a brush asks its own faces
    /// which ledges carry them.
    /// </remarks>
    private static void Census(
        TextWriter output,
        ReadOnlySpan<byte> brushes,
        ReadOnlySpan<byte> sides,
        ReadOnlySpan<byte> planes,
        MapLevel level)
    {
        Dictionary<(int X, int Y, int Z, int D), List<int>> byFace = [];

        for (int index = 0; index < level.Physics.Ledges.Count; index++)
        {
            foreach ((Vector3 normal, float distance) in level.Physics.Ledges[index].Planes)
            {
                (int, int, int, int) key = Key(normal, distance);

                if (!byFace.TryGetValue(key, out List<int>? filed))
                {
                    filed = [];
                    byFace[key] = filed;
                }

                filed.Add(index);
            }
        }

        int solidBrushes = 0;
        int unmatched = 0;
        Dictionary<int, int> unmatchedContents = [];

        Dictionary<int, int> hits = [];

        for (int at = 0; at + BrushStride <= brushes.Length; at += BrushStride)
        {
            int first = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(brushes[at..]);
            int count =
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(brushes[(at + 4)..]);
            int contents =
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(brushes[(at + 8)..]);

            if ((contents & IvpWorldCollision.MaskSolid) == 0)
            {
                continue;
            }

            solidBrushes++;
            hits.Clear();

            int faces = 0;

            for (int side = 0; side < count; side++)
            {
                int sideAt = (first + side) * BrushSideStride;

                if (sideAt < 0 || sideAt + BrushSideStride > sides.Length)
                {
                    break;
                }

                int plane = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(
                    sides[sideAt..]) * PlaneStride;

                if (plane < 0 || plane + PlaneStride > planes.Length)
                {
                    break;
                }

                Vector3 normal = new(
                    BitConverter.ToSingle(planes[plane..]),
                    BitConverter.ToSingle(planes[(plane + 4)..]),
                    BitConverter.ToSingle(planes[(plane + 8)..]));

                faces++;

                if (!byFace.TryGetValue(
                    Key(normal, BitConverter.ToSingle(planes[(plane + 12)..])),
                    out List<int>? carrying))
                {
                    continue;
                }

                foreach (int ledge in carrying)
                {
                    hits[ledge] = hits.TryGetValue(ledge, out int seen) ? seen + 1 : 1;
                }
            }

            int best = hits.Count == 0 ? 0 : hits.Values.Max();

            if (faces > 0 && best * 2 < faces)
            {
                unmatched++;
                unmatchedContents[contents] =
                    unmatchedContents.TryGetValue(contents, out int was) ? was + 1 : 1;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  census: {unmatched} of {solidBrushes} solid brushes have NO ledge carrying even " +
            $"half their faces — and the WORLD is built with NO_SHRINK " +
            $"(`BuildWorldPhysModel( collisionList[i], NO_SHRINK, VPHYSICS_MERGE )`, " +
            $"`utils/vbsp/ivp.cpp:1531`), so an exact plane match IS expected here"));

        Boxes(output, brushes, sides, planes, level);

        // **The shape of the hulls themselves, which needs no brush to compare against.** vbsp
        // writes one convex per brush and a brush is a handful of planes; a set whose hulls carry
        // dozens of planes each is not brush-shaped, and that is a fact about this reader rather
        // than about the map (B400).
        int[] buckets = new int[6];

        foreach (IvpWorldLedge ledge in level.Physics.Ledges)
        {
            int slot = ledge.Planes.Count switch
            {
                <= 6 => 0,
                <= 12 => 1,
                <= 24 => 2,
                <= 48 => 3,
                <= 96 => 4,
                _ => 5,
            };

            buckets[slot]++;
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  ledge plane counts: <=6 {buckets[0]}, <=12 {buckets[1]}, <=24 {buckets[2]}, " +
            $"<=48 {buckets[3]}, <=96 {buckets[4]}, more {buckets[5]}"));

        // **The control every test above assumed and none of them checked**: a hull's own vertex
        // average is inside it, so `dot(normal, centre) - distance` must be negative on every face
        // if the normals point OUTWARD. A hull that fails to contain its own middle has inward
        // normals, and then every containment test in this project reads backwards (B400).
        int holdsItsOwn = 0;
        int sampled = 0;

        foreach (IvpWorldLedge ledge in level.Physics.Ledges)
        {
            if (ledge.Vertices.Count == 0 || ledge.Planes.Count == 0)
            {
                continue;
            }

            Vector3 middle = Vector3.Zero;

            foreach (Vector3 vertex in ledge.Vertices)
            {
                middle += vertex;
            }

            middle /= ledge.Vertices.Count;
            sampled++;

            if (ledge.Planes.All(face => Vector3.Dot(face.Normal, middle) - face.Distance <= 0.01f))
            {
                holdsItsOwn++;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {holdsItsOwn} of {sampled} ledges contain their own vertex average — the rest have " +
            $"planes that do not enclose their own hull"));

        foreach ((int contents, int count) in unmatchedContents.OrderByDescending(p => p.Value).Take(6))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"    unmatched contents 0x{contents:X}: {count}"));
        }
    }

    /// <summary>A plane, quantized so two readings of one surface land in one bucket.</summary>
    private static (int X, int Y, int Z, int D) Key(Vector3 normal, float distance) =>
        ((int)MathF.Round(normal.X * 64f),
         (int)MathF.Round(normal.Y * 64f),
         (int)MathF.Round(normal.Z * 64f),

         // **Quantized to two units, so the 0.5 shrink cannot land a brush and its own ledge in
         // different buckets.** At half-unit precision the census reported almost every brush
         // unmatched, which was the instrument and not the map.
         (int)MathF.Round(distance / 2f));

    /// <summary><c>LUMP_BRUSHES</c>.</summary>
    private const int BrushesLump = 18;

    /// <summary><c>LUMP_BRUSHSIDES</c>.</summary>
    private const int BrushSidesLump = 19;

    /// <summary><c>LUMP_PLANES</c>.</summary>
    private const int PlanesLump = 1;

    /// <summary>Bytes per <c>dbrush_t</c>: first side, side count, contents.</summary>
    private const int BrushStride = 12;

    /// <summary>Bytes per <c>dbrushside_t</c>: plane number, texinfo, dispinfo, bevel.</summary>
    private const int BrushSideStride = 8;

    /// <summary>Bytes per <c>dplane_t</c>: a normal, a distance and a type.</summary>
    private const int PlaneStride = 20;

    /// <summary>Where to drop, from the command line or the measured deaths on `z1800`.</summary>
    /// <remarks>
    /// **The defaults are the seed positions of the corpses that actually misbehave**, read out of
    /// the viewer's own log rather than chosen: two of these are the pair that still leave the
    /// world, and the rest settle. A probe whose default case is the known-bad one is worth more
    /// than one that needs arguments to say anything.
    /// </remarks>
    private static IEnumerable<(float X, float Y, float Z, float FX, float FY, float FZ)> Places(
        IReadOnlyList<string> arguments)
    {
        if (arguments.Count >= 5 &&
            float.TryParse(arguments[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
            float.TryParse(arguments[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
            float.TryParse(arguments[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            yield return (x, y, z, Number(arguments, 5), Number(arguments, 6), Number(arguments, 7));
            yield break;
        }

        // **Seed positions and the blow's whole VECTOR, both read out of the viewer's own log.**
        // The blow is what separates the two that leave the world from the seven that rest, and its
        // direction is half of that: 2348 is punched with −8,495 of DOWNWARD force, straight into
        // the ground it then goes through. A magnitude with a guessed direction reproduced the
        // wrong corpse entirely, which is why `CorpsePhysics.Blows` now keeps the vector.
        yield return (361.7f, -1614.3f, 57.6f, -2129.2f, -16651.1f, -473.8f);   // 2185, leaves
        yield return (-11.5f, -1558.8f, 47.3f, -17897.2f, 13523.2f, -8495.5f);  // 2348, leaves
        yield return (-972.6f, -1400.3f, 77.5f, 0f, 0f, 0f);                    // 2277, SPAWN not rest — leaves the world here, and that is expected (see remarks above)
        yield return (256.9f, -1416.1f, 55.2f, 0f, 0f, 0f);                     // 2080, no blow
        yield return (-953.8f, -1556.3f, 77.5f, 0f, 0f, 0f);                    // 2132, rests
    }

    /// <summary>One optional number off the command line; absent means zero.</summary>
    private static float Number(IReadOnlyList<string> arguments, int index) =>
        index < arguments.Count &&
        float.TryParse(
            arguments[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : 0f;

    /// <summary>Steps one ragdoll from a standing pose at a spot until it stops moving.</summary>
    /// <remarks>
    /// **The death force is applied, and that reversed an earlier decision here.** This dropped
    /// every corpse from rest, on the reasoning that a blow would make it a test of the blow rather
    /// than of the collision — but the two corpses that leave the world on `z1800` are exactly the
    /// two that were hit hard, and both SETTLED in this probe. An instrument that cannot reproduce
    /// the defect is not measuring the defect.
    /// </remarks>
    private static void Drop(
        TextWriter output,
        MapLevel level,
        RagdollBody ragdoll,
        SurfaceTable surfaces,
        (float X, float Y, float Z) at,
        (float X, float Y, float Z) blow,
        byte[] map)
    {
        // **The pose is CHAINED down the hierarchy, and getting that wrong made this instrument
        // lie.** `OriginParentSpace` is where an element sits in its PARENT's space, so adding it
        // to the drop point puts every body a single offset from one spot — a ragdoll whose joints
        // are all violated before the first step, which then tears itself apart and falls through
        // the map. Measured: one drop reported LEFT THE WORLD here while the same corpse rests at
        // z 4.8 in the viewer.
        //
        // **Each element is placed relative to its own parent instead**, walking the list in order,
        // which is safe because `RagdollBody.Build` emits parents before children.
        (Vector3, Quaternion)[] start = new (Vector3, Quaternion)[ragdoll.Elements.Count];

        for (int index = 0; index < start.Length; index++)
        {
            RagdollElement element = ragdoll.Elements[index];

            Vector3 origin = element.ParentIndex >= 0 && element.ParentIndex < index
                ? start[element.ParentIndex].Item1 + element.OriginParentSpace
                : new Vector3(at.X, at.Y, at.Z);

            start[index] = (origin, Quaternion.Identity);
        }

        // **A straight sweep down before anything is simulated, because an absence needs a
        // control.** "The corpse fell through" and "there was nothing under it" produce the same
        // trace, and only this tells them apart: it asks the same `IvpWorldCollision` the solver
        // asks, by the same route, for the surface directly beneath the drop point.
        Vector3 above = new(at.X, at.Y, at.Z);

        string inside = level.Physics.Touching(above, default) is { } already
            ? $"INSIDE a solid, {already.Depth:0.#} deep, feature {already.Feature}"
            : "in open space";

        output.WriteLine(
            level.Physics.Sweep(above, above with { Z = at.Z - Probing }) is { } floor
                ? $"  {inside}; floor beneath: z {at.Z - (floor.Fraction * Probing):0.#} " +
                  $"normal ({floor.Normal.X:0.##}, {floor.Normal.Y:0.##}, {floor.Normal.Z:0.##})"
                : $"  {inside}; NOTHING beneath ({at.X:0.#}, {at.Y:0.#}, {at.Z:0.#}) " +
                  $"for {Probing:0} units");

        // **The second reading of the same question, and it is the control the first one needed**
        // (B400). `IvpWorldCollision` is built from `LUMP_PHYSCOLLIDE` plus the displacements;
        // `MapLevel.Sweep` asks the BSP tree and the displacements instead — the route the chase
        // camera uses, which demonstrably does not pass through floors. "Nothing beneath" from one
        // and a floor from the other is a hole in the PHYSICS world, not in the map; nothing from
        // both is a spot that really is open space, and the corpse falling is correct.
        float tree = level.Sweep(
            (at.X, at.Y, at.Z), (at.X, at.Y, at.Z - Probing), halfExtent: 0f);

        output.WriteLine(
            tree < 1f
                ? $"  the BSP tree says floor at z {at.Z - (tree * Probing):0.#}"
                : $"  the BSP tree ALSO finds nothing for {Probing:0} units");

        // **A THIRD reading, because two disagreeing instruments name no culprit** (B400). `Sweep`
        // clips against brushes; `IsClear` walks the leaves the tree files those brushes under. A
        // column where the brush clip stops and the leaf walk does not says the brush is being
        // clipped from a leaf that does not contain it, which is a tree fault rather than a
        // missing ledge — and the ledge count already matches `LUMP_BRUSHES` exactly.
        if (level.Leaves is { } leaves && tree < 1f)
        {
            float stopped = at.Z - (tree * Probing);

            string leafWalk = leaves.IsClear(at.X, at.Y, stopped + 16f, at.X, at.Y, stopped - 16f)
                ? "CLEAR — the brush clip and the leaves disagree"
                : "blocked, agreeing with the brush clip";

            output.WriteLine($"  across that floor, the leaf walk says {leafWalk}");

            // **How many ledges are nowhere the map is?** The tree's root node carries the world's
            // own bounds, so a ledge centre outside them cannot be geometry this map compiled — and
            // the ledge COUNT already matches `LUMP_BRUSHES`, so a misplaced one is a brush that
            // exists in the collide and is not where the brush is (B400).
            if (leaves.Node(0) is { } world)
            {
                int outside = 0;

                foreach (Vector3 centre in level.Physics.Ledges.Select(ledge => ledge.Center))
                {
                    if (centre.X < world.Min.X || centre.X > world.Max.X ||
                        centre.Y < world.Min.Y || centre.Y > world.Max.Y ||
                        centre.Z < world.Min.Z || centre.Z > world.Max.Z)
                    {
                        outside++;
                    }
                }

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  the map's own bounds are x {world.Min.X:0}..{world.Max.X:0} " +
                    $"y {world.Min.Y:0}..{world.Max.Y:0} z {world.Min.Z:0}..{world.Max.Z:0}; " +
                    $"{outside} of {level.Physics.Ledges.Count} ledge centres fall outside them"));
            }
        }

        // **Where the ledges ARE, because "no floor here" and "no ledges anywhere near here" are
        // different faults** and the sweep cannot tell them apart. Bounds catch a conversion that
        // left the whole set in metres at the origin; the local count catches a set that is in the
        // right place and simply thin where this point is.
        Vector3 low = new(float.MaxValue), high = new(float.MinValue);
        int near = 0;

        foreach (Vector3 centre in level.Physics.Ledges.Select(ledge => ledge.Center))
        {
            low = Vector3.Min(low, centre);
            high = Vector3.Max(high, centre);

            if (MathF.Abs(centre.X - at.X) < 512f && MathF.Abs(centre.Y - at.Y) < 512f)
            {
                near++;
            }
        }

        output.WriteLine(
            level.Physics.Ledges.Count == 0
                ? "  the physics world holds NO ledges at all"
                : $"  ledge centres span x {low.X:0} to {high.X:0}, y {low.Y:0} to {high.Y:0}, " +
                  $"z {low.Z:0} to {high.Z:0}; {near} within 512 units of this column");

        // **Does each ledge's own bounding sphere CONTAIN its hull?** The sweep rejects a ledge on
        // that sphere before looking at a single plane, so a sphere that under-reports deletes
        // real geometry from every query — and the map's own node spheres are not built from the
        // hull this reader assembles.
        int escaping = 0;
        float worst = 0f;

        foreach (IvpWorldLedge ledge in level.Physics.Ledges)
        {
            foreach (Vector3 vertex in ledge.Vertices)
            {
                float over = (vertex - ledge.Center).Length() - ledge.Radius;

                if (over > 0.01f)
                {
                    escaping++;
                    worst = MathF.Max(worst, over);
                    break;
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {escaping} of {level.Physics.Ledges.Count} ledges have hull points OUTSIDE their " +
            $"own bounding sphere, worst by {worst:0.#} units"));

        // **What each ledge is MADE of, counted.** The sweep rejects a ledge whose contents miss
        // `IvpWorldCollision.MaskSolid` before it looks at a plane, so a floor filed under a mask
        // this project excludes is invisible to a corpse and to nothing else (B400).
        Dictionary<int, int> byContents = [];

        foreach (int contents in level.Physics.Ledges.Select(ledge => ledge.Contents))
        {
            byContents[contents] = byContents.TryGetValue(contents, out int seen) ? seen + 1 : 1;
        }

        foreach ((int contents, int count) in byContents.OrderByDescending(pair => pair.Value))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  contents 0x{contents:X}: {count} ledges" +
                $"{((contents & IvpWorldCollision.MaskSolid) == 0 ? "  <- SKIPPED by every sweep" : string.Empty)}"));
        }

        // **The broadphase, bypassed.** `Sweep` gathers candidates from a grid and rejects on a
        // bounding sphere before it clips anything, so "nothing beneath" can mean the ledge was
        // never offered rather than that no ledge covers the column. Walking EVERY ledge's planes
        // for a point just under the camera's floor tells the two apart (B400).
        Vector3 under = new(at.X, at.Y, (at.Z - (tree * Probing)) - 2f);
        int covering = 0;

        foreach (IvpWorldLedge ledge in level.Physics.Ledges)
        {
            bool within = ledge.Planes.Count > 0;

            foreach ((Vector3 normal, float distance) in ledge.Planes)
            {
                if (Vector3.Dot(normal, under) - distance > 0f)
                {
                    within = false;
                    break;
                }
            }

            if (within)
            {
                covering++;

                output.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  a ledge DOES contain ({under.X:0}, {under.Y:0}, {under.Z:0}): centre " +
                    $"({ledge.Center.X:0}, {ledge.Center.Y:0}, {ledge.Center.Z:0}) radius " +
                    $"{ledge.Radius:0.#} contents 0x{ledge.Contents:X} planes {ledge.Planes.Count}"));
            }
        }

        if (covering == 0)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  NO ledge of {level.Physics.Ledges.Count} contains " +
                $"({under.X:0}, {under.Y:0}, {under.Z:0}), broadphase bypassed"));

            Brush(output, map, under, level);
        }

        // **Is the floor's own FACE in the set at all?** The sweep can only miss a ledge it holds;
        // a ledge with an upward face at the camera's floor height, near this column, would say
        // the geometry is present and the query is at fault. None would say it never arrived.
        int floors = 0;

        foreach (IvpWorldLedge ledge in level.Physics.Ledges)
        {
            if (MathF.Abs(ledge.Center.X - at.X) > 512f || MathF.Abs(ledge.Center.Y - at.Y) > 512f)
            {
                continue;
            }

            foreach ((Vector3 normal, float distance) in ledge.Planes)
            {
                if (normal.Z > 0.9f && MathF.Abs(distance - (at.Z - (tree * Probing))) < 16f)
                {
                    floors++;
                    break;
                }
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {floors} ledges within 512 units carry an upward face at the camera's floor height"));

        // **The same query, shortened.** A long sweep and a short one over the same surface must
        // agree; if only the short one finds the floor, the fault is in how far the query reaches
        // rather than in the geometry it reaches for.
        float camera = at.Z - (tree * Probing);

        foreach (float height in new[] { 8f, 32f, 128f, 512f })
        {
            Vector3 upper = new(at.X, at.Y, camera + height);
            Vector3 lower = new(at.X, at.Y, camera - 8f);

            string found = level.Physics.Sweep(upper, lower) is { } shorter
                ? string.Create(
                    CultureInfo.InvariantCulture, $"hit, normal z {shorter.Normal.Z:0.##}")
                : "nothing";

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    from {height:0} above the camera's floor: {found}"));
        }

        // **The furthest ledges from the map's middle, because their extremes were IDENTICAL on
        // two different maps** — y −15436 and z 14688 to the unit on both cp_granary and
        // cp_process_f12. Map geometry does not agree across maps; a constant does.
        foreach (IvpWorldLedge ledge in level.Physics.Ledges
            .OrderByDescending(ledge => ledge.Center.LengthSquared())
            .Take(4))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    FAR ledge at ({ledge.Center.X:0}, {ledge.Center.Y:0}, {ledge.Center.Z:0}) " +
                $"radius {ledge.Radius:0.#} contents 0x{ledge.Contents:X} " +
                $"planes {ledge.Planes.Count} vertices {ledge.Vertices.Count}"));
        }

        foreach (IvpWorldLedge ledge in level.Physics.Ledges
            .OrderBy(ledge => (ledge.Center - above).LengthSquared())
            .Take(5))
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    ledge at ({ledge.Center.X:0}, {ledge.Center.Y:0}, {ledge.Center.Z:0}) " +
                $"radius {ledge.Radius:0.#} contents 0x{ledge.Contents:X} " +
                $"planes {ledge.Planes.Count}"));
        }

        RagdollSimulation simulation = RagdollSimulation.Create(ragdoll, Step, start, surfaces);

        simulation.Environment.World = level.Physics;

        // **The killing blow, whole, because the corpses that misbehave are the ones that got one.**
        // Both of `z1800`'s remaining escapees carry a large `m_vecForce`, and dropping from rest
        // cannot reproduce either: this probe settled both. The DIRECTION is half of it — 2348 is
        // punched with −8,495 of downward force, straight into the ground it then goes through — so
        // a magnitude with a guessed diagonal reproduced a different corpse entirely, which is why
        // `CorpsePhysics.Blows` now carries the vector.
        if ((blow.X * blow.X) + (blow.Y * blow.Y) + (blow.Z * blow.Z) > 0f)
        {
            simulation.Kill(blow, forceBone: 0);
        }

        float lowest = float.MaxValue;
        int settled = -1;

        for (int tick = 0; tick < Ticks; tick++)
        {
            simulation.Step();

            float speed = Fastest(simulation);
            float height = simulation.Environment.Bodies[0].Position.Z is var z ? (float)z : 0f;

            lowest = MathF.Min(lowest, height);

            if (settled < 0 && tick > 20 && speed < Still)
            {
                settled = tick;
            }

            if (Trace && tick % 33 == 0)
            {
                (float oppose, float separate, float rub) = simulation.Environment.Split;

                IvpRigidBody at0 = simulation.Environment.Bodies[0];

                output.WriteLine(
                    $"    tick {tick,3} z {at0.Position.Z,9:0.#} speed {speed,7:0.#} " +
                    $"contacts {simulation.Environment.Contacts,3} " +
                    $"deepest {simulation.Environment.DeepestContact,7:0.##} " +
                    $"oppose {oppose,11:0.#} separate {separate,11:0.#} rub {rub,11:0.#} " +
                    $"friction wanted {simulation.Environment.Rubbing.Wanted,9:0.#} " +
                    $"allowed {simulation.Environment.Rubbing.Allowed,9:0.#} " +
                    $"clamped {simulation.Environment.Rubbing.Clamped,3} " +
                    $"gravity {simulation.Environment.Gained.Gravity,10:0.#} " +
                    $"joints {simulation.Environment.Gained.Joints,11:0.#} " +
                    $"lifted {simulation.Environment.Lifted,10:0.#}");
            }
        }

        IvpRigidBody root = simulation.Environment.Bodies[0];

        output.WriteLine(
            $"  from ({at.X:0.#}, {at.Y:0.#}, {at.Z:0.#}) -> " +
            $"({root.Position.X:0.#}, {root.Position.Y:0.#}, {root.Position.Z:0.#}) " +
            $"lowest {lowest:0.#} speed {Fastest(simulation):0.##} " +
            $"{(simulation.Asleep ? "asleep" : "AWAKE")} " +
            $"{(settled < 0 ? "NEVER SETTLED" : $"settled at tick {settled}")} " +
            $"{(root.Position.Z < Lost ? "LEFT THE WORLD" : string.Empty)}");

        // **What it came to rest ON, which decides whose defect a corpse that never sleeps is.**
        // After B400 the corpses that land on brush geometry settle and sleep while two on
        // `koth_harvest_final` do not, and both readings of that split — terrain, or brushes — were
        // equally consistent with the trace. The distance from the resting body to the nearest
        // ledge and to the nearest terrain triangle separates them without a model of either.
        Vector3 rest = new(
            (float)root.Position.X, (float)root.Position.Y, (float)root.Position.Z);

        float toLedge = float.MaxValue;

        foreach (IvpWorldLedge ledge in level.Physics.Ledges)
        {
            foreach (Vector3 vertex in ledge.Vertices)
            {
                toLedge = MathF.Min(toLedge, (vertex - rest).Length());
            }
        }

        float toTerrain = float.MaxValue;

        foreach (DisplacementTriangle triangle in level.Displacements.Triangles())
        {
            toTerrain = MathF.Min(toTerrain, Away(triangle.A, rest));
            toTerrain = MathF.Min(toTerrain, Away(triangle.B, rest));
            toTerrain = MathF.Min(toTerrain, Away(triangle.C, rest));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    resting near: {toLedge:0.#} units to the nearest ledge vertex, " +
            $"{toTerrain:0.#} to the nearest terrain vertex"));
    }

    /// <summary>How far a terrain vertex is from a point, in Source units.</summary>
    /// <remarks>
    /// **A displacement vertex is a float triple rather than a <c>Vector3</c>**, so the subtraction
    /// is written out once here instead of at three call sites.
    /// </remarks>
    private static float Away((float X, float Y, float Z) vertex, Vector3 to) =>
        new Vector3(vertex.X - to.X, vertex.Y - to.Y, vertex.Z - to.Z).Length();

    private static float Fastest(RagdollSimulation simulation) =>
        simulation.Environment.Bodies.Max(
            body => MathF.Sqrt(
                (body.Velocity.X * body.Velocity.X) +
                (body.Velocity.Y * body.Velocity.Y) +
                (body.Velocity.Z * body.Velocity.Z)));

    /// <summary>Reads a game file, the map's own pakfile first — as the asset path does.</summary>
    private static byte[]? Read(PakFile pak, GameArchives archives, string file)
    {
        try
        {
            return pak.ReadFile(file) ?? archives.Read(file);
        }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        {
            return null;
        }
    }

    private static RagdollBody? RagdollFor(GameContent game, string model)
    {
        if (game.Archives.Read(model) is not { } modelBytes ||
            game.Archives.Read(Path.ChangeExtension(model, ".phy")) is not { } physicsBytes)
        {
            return null;
        }

        try
        {
            return RagdollBody.Build(
                PhysicsModel.Read(physicsBytes), StudioBones.Read(modelBytes));
        }
        catch (Exception failure) when (failure is InvalidDataException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>The demo's own tick interval, which is what the engine steps physics at.</summary>
    private const float Step = 1f / 66f;

    /// <summary>Ten seconds, which is longer than any corpse takes to stop.</summary>
    private const int Ticks = 660;

    /// <summary>Below this a body counts as stopped, in Source units per second.</summary>
    private const float Still = 1f;

    /// <summary>How far straight down the floor control looks, in Source units.</summary>
    private const float Probing = 2000f;

    /// <summary>The height the viewer's own diagnostic calls leaving the world.</summary>
    private const float Lost = -50f;

    /// <summary>Whether to print the speed every half second, for diagnosing a solve.</summary>
    /// <remarks>
    /// **A corpse's speed over TIME says which fault it has, where its resting place does not.**
    /// Growing means the solve injects energy, steady means something drives it, and falling means
    /// it is simply slow to settle — three different bugs behind one symptom.
    /// </remarks>
    private const bool Trace = true;
}
