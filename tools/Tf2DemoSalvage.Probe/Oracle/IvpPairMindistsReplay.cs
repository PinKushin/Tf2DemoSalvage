using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// A pair's mindists — <c>FUN_180096680</c> with its constructor <c>FUN_1800975d0</c> — as lanes of bits: two objects with synthesized
/// ledge trees, a pair refreshed over six steps, the mindists it holds after each, and what was told on the way (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-pair-mindists` probe from the shipped `vphysics.dll` and
/// replayed by `IvpPairMindistsConformanceTests`. **Each object is resting** (`+0x78` is 8), so its object cache is the matrix the case
/// gives and never refreshed; **the tail of the constructor is recorded, not run** — the probe detours `FUN_1800977f0` and
/// `FUN_180097940` to recorders, and the port's environment hands its twins the same events — and **the pair's delegator is the
/// probe's**, taking a deleted mindist out of the pair by its back-index as the watcher's `FUN_1800b5fd0` does. A mindist is named by
/// its two ledges' node lanes: <c>(first &lt;&lt; 8) | second</c>. The trees hold no hull ledges, so no larger mindist is made.
/// </remarks>
public static class IvpPairMindistsReplay
{
    /// <summary>How many node lanes each object's tree has.</summary>
    public const int NodeCount = 15;

    /// <summary>How many refreshes a case runs.</summary>
    public const int StepCount = 6;

    /// <summary>How many of a step's mindists, and of its events, are carried; the rest are in the digests.</summary>
    public const int Carried = 24;

    /// <summary>A ledge stub's size in the synthesized surfaces: back-offset, children bits, and a zero first triangle header.</summary>
    public const int StubSize = 0x20;

    /// <summary>An event: the constructor's exact tail, <c>FUN_1800977f0</c>.</summary>
    public const int Exact = 1;

    /// <summary>An event: the constructor's phantom tail, <c>FUN_180097940</c>.</summary>
    public const int Phantom = 2;

    /// <summary>An event: the delegator told a mindist is going.</summary>
    public const int Removed = 3;

    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } = BuildInputs();

    /// <summary>What each step leaves.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("pair", IvpReplayKind.Whole32, StepCount * Carried),
        new("pair-count", IvpReplayKind.Whole32, StepCount),
        new("pair-digest", IvpReplayKind.Real64, StepCount),
        new("events", IvpReplayKind.Whole32, StepCount * Carried),
        new("event-count", IvpReplayKind.Whole32, StepCount),
        new("event-digest", IvpReplayKind.Real64, StepCount),
        new("live", IvpReplayKind.Whole32, StepCount),
        new("created", IvpReplayKind.Whole32, StepCount),
        new("deleted", IvpReplayKind.Whole32, StepCount),
    ];

    /// <summary>A mindist's name, or an event's lane.</summary>
    /// <param name="kind">The event's kind, or zero for a mindist's name.</param>
    /// <param name="first">The first ledge's node lane.</param>
    /// <param name="second">The second ledge's node lane.</param>
    /// <returns>The lane.</returns>
    public static int Name(int kind, int first, int second) => (kind << 16) | (first << 8) | second;

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    /// <exception cref="InvalidDataException">A synthesized surface does not read as a tree.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        List<int> events = [];
        Dictionary<PhysicsLedgeTreeNode, int>[] lanes = [[], []];
        PhysicsLedgeTree[] trees = new PhysicsLedgeTree[2];
        IvpCollisionObject[] objects = new IvpCollisionObject[2];
        List<IvpCollision> pair = [];
        IvpCollisionEnvironment environment = new()
        {
            Filter = (_, _) => true,
            BecomeExact = mindist => events.Add(Name(Exact, lanes[0][mindist.Ledge(0)!], lanes[1][mindist.Ledge(1)!])),
            BecomePhantom = mindist => events.Add(Name(Phantom, lanes[0][mindist.Ledge(0)!], lanes[1][mindist.Ledge(1)!])),
        };
        Delegator delegator = new(pair, events, lanes);

        for (int side = 0; side < 2; side++)
        {
            (byte[] surface, int[] nodes, _) = IvpLedgeTreeReplay.Surface(inputs, Suffix(side), StubSize);

            trees[side] = PhysicsHull.Tree(surface) ?? throw new InvalidDataException("A synthesized surface read as no tree.");

            for (int index = 0; index < NodeCount; index++)
            {
                if (nodes[index] >= 0)
                {
                    lanes[side][trees[side].Node(nodes[index])] = index;
                }
            }

            IvpRigidBody placement = new()
            {
                Orientation = Quaternion(inputs, "cache-rotation", side * 4),
                Position = Triple(inputs, "cache-translation", side * 3),
            };
            IvpObjectCache cache = new();

            cache.Refresh(placement, 0d, 0, null, null);
            objects[side] = new IvpCollisionObject
            {
                Core = new IvpRigidBody { Radius = IvpImpactReplay.Real32(inputs, "core-radius", side) },
                Environment = environment,
                Surface = new IvpPolygonSurfaceManager(trees[side]),
                ExtraRadius = IvpImpactReplay.Real32(inputs, "extra", side),
                MovementState = 8,
                Cache = cache,
            };
        }

        Dictionary<string, long[]> outputs = NewOutputs();

        for (int step = 0; step < StepCount; step++)
        {
            events.Clear();
            environment.Now = IvpImpactReplay.Real64(inputs, "now", step);

            for (int side = 0; side < 2; side++)
            {
                IvpRigidBody core = objects[side].Core!;

                core.LastStepped = IvpImpactReplay.Real64(inputs, "stepped", (step * 2) + side);
                core.Position = Triple(inputs, "position", (step * 2 + side) * 3);
                core.PreviousVelocity = IvpImpactReplay.Vector(inputs, "velocity", (step * 2 + side) * 3);
            }

            IvpPairMindists.Refresh(
                objects[0], objects[1], IvpImpactReplay.Real64(inputs, "gap", step), pair,
                Given(inputs, "given-first", step, trees[0]), Given(inputs, "given-second", step, trees[1]), null, null, delegator);

            List<int> names = pair.ConvertAll(collision => Name(0, lanes[0][((IvpMindist)collision).Ledge(0)!], lanes[1][((IvpMindist)collision).Ledge(1)!]));

            Record(outputs, "pair", step, names);
            Record(outputs, "event", step, events);
            outputs["live"][step] = environment.LiveMindists;
            outputs["created"][step] = environment.CreatedMindists;
            outputs["deleted"][step] = environment.DeletedMindists;
        }

        return outputs;
    }

    /// <summary>A zeroed output dictionary.</summary>
    internal static Dictionary<string, long[]> NewOutputs()
    {
        Dictionary<string, long[]> outputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in Outputs)
        {
            outputs[field.Name] = new long[field.Count];
        }

        return outputs;
    }

    /// <summary>A step's list into its lanes: the first <see cref="Carried"/>, the count and an FNV-1a digest.</summary>
    /// <param name="outputs">The outputs.</param>
    /// <param name="name">The list's lane prefix: <c>pair</c> or <c>event</c>.</param>
    /// <param name="step">The step.</param>
    /// <param name="values">The list.</param>
    internal static void Record(Dictionary<string, long[]> outputs, string name, int step, List<int> values)
    {
        ulong hash = 14695981039346656037UL;
        long[] carried = outputs[name == "pair" ? "pair" : "events"];

        for (int index = 0; index < values.Count; index++)
        {
            if (index < Carried)
            {
                carried[(step * Carried) + index] = values[index];
            }

            hash = unchecked((hash ^ (uint)values[index]) * 1099511628211UL);
        }

        outputs[name + "-count"][step] = values.Count;
        outputs[name + "-digest"][step] = unchecked((long)hash);
    }

    /// <summary>An object's lane suffix.</summary>
    /// <param name="side">0 or 1.</param>
    /// <returns>The suffix.</returns>
    internal static string Suffix(int side) => side == 0 ? "-first" : "-second";

    /// <summary>Every lane where two readings of the same case differ, named.</summary>
    /// <param name="expected">What the binary left.</param>
    /// <param name="actual">What the port left.</param>
    /// <returns>One line per differing lane; empty when the two agree.</returns>
    public static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, long[]> expected, IReadOnlyDictionary<string, long[]> actual) =>
        IvpImpactReplay.Differences(Outputs, expected, actual);

    /// <summary>Writes one case in the fixture's form.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="replay">The case.</param>
    public static void Write(TextWriter writer, IvpReplayCase replay) => IvpImpactReplay.Write(Inputs, Outputs, writer, replay);

    /// <summary>Reads every case a fixture holds.</summary>
    /// <param name="reader">The fixture.</param>
    /// <returns>The cases, in file order.</returns>
    public static IReadOnlyList<IvpReplayCase> Parse(TextReader reader) => IvpImpactReplay.Parse(Inputs, Outputs, reader);

    private static List<IvpReplayField> BuildInputs()
    {
        List<IvpReplayField> fields = [];

        for (int side = 0; side < 2; side++)
        {
            fields.Add(new("kind" + Suffix(side), IvpReplayKind.Whole32, NodeCount));
            fields.Add(new("center" + Suffix(side), IvpReplayKind.Real32, NodeCount * 3));
            fields.Add(new("radius" + Suffix(side), IvpReplayKind.Real32, NodeCount));
            fields.Add(new("box" + Suffix(side), IvpReplayKind.Whole32, NodeCount));
        }

        fields.AddRange(
        [
            new("extra", IvpReplayKind.Real32, 2),
            new("core-radius", IvpReplayKind.Real32, 2),
            new("cache-rotation", IvpReplayKind.Real64, 8),
            new("cache-translation", IvpReplayKind.Real64, 6),
            new("now", IvpReplayKind.Real64, StepCount),
            new("stepped", IvpReplayKind.Real64, StepCount * 2),
            new("position", IvpReplayKind.Real64, StepCount * 6),
            new("velocity", IvpReplayKind.Real32, StepCount * 6),
            new("gap", IvpReplayKind.Real64, StepCount),
            new("given-first", IvpReplayKind.Whole32, StepCount),
            new("given-second", IvpReplayKind.Whole32, StepCount),
        ]);

        return fields;
    }

    private static PhysicsLedgeTreeNode? Given(IReadOnlyDictionary<string, long[]> inputs, string name, int step, PhysicsLedgeTree tree)
    {
        int lane = IvpImpactReplay.Whole32(inputs, name, step);

        if (lane < 0)
        {
            return null;
        }

        (_, int[] nodes, _) = IvpLedgeTreeReplay.Surface(inputs, name == "given-first" ? Suffix(0) : Suffix(1), StubSize);

        return tree.Node(nodes[lane]);
    }

    private static (double X, double Y, double Z) Triple(IReadOnlyDictionary<string, long[]> inputs, string name, int lane) =>
        (IvpImpactReplay.Real64(inputs, name, lane), IvpImpactReplay.Real64(inputs, name, lane + 1), IvpImpactReplay.Real64(inputs, name, lane + 2));

    private static (double X, double Y, double Z, double W) Quaternion(IReadOnlyDictionary<string, long[]> inputs, string name, int lane) =>
        (IvpImpactReplay.Real64(inputs, name, lane), IvpImpactReplay.Real64(inputs, name, lane + 1),
         IvpImpactReplay.Real64(inputs, name, lane + 2), IvpImpactReplay.Real64(inputs, name, lane + 3));

    /// <summary>The probe's delegator: a mindist out of the pair by its back-index, as <c>FUN_1800b5fd0</c> takes it.</summary>
    private sealed class Delegator(List<IvpCollision> pair, List<int> events, Dictionary<PhysicsLedgeTreeNode, int>[] lanes) : IIvpCollisionDelegator
    {
        public void CollisionRemoved(IvpCollision collision)
        {
            IvpMindist mindist = (IvpMindist)collision;

            events.Add(Name(Removed, lanes[0][mindist.Ledge(0)!], lanes[1][mindist.Ledge(1)!]));
            IvpCollisionList.Remove(pair, collision);
        }
    }
}
