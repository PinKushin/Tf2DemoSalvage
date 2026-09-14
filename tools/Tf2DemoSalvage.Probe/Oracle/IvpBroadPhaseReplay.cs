using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// IVP's broad phase — <c>FUN_180098880</c> and its node-making copy <c>FUN_180096eb0</c> — as lanes of bits: eight objects refiled
/// over a sequence of steps, the calls it makes out, and where each node is left (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-broad-phase` probe from the shipped `vphysics.dll` and
/// replayed by `IvpBroadPhaseConformanceTests`. **The filter, the creators and the watchers are the probe's own, on both sides**: the
/// binary calls them through fabricated tables, and <see cref="Run"/> gives the port managed ones that do the same — a filter
/// answering from the case's masks, two creators of which the one asked first declines a third of pairs, and watchers registered on
/// both nodes (`FUN_18009de20`) and taken off both when deleted (`FUN_18009ef40`). Each call the broad phase makes out is an event:
/// <c>(kind &lt;&lt; 24) | (creator &lt;&lt; 16) | (first &lt;&lt; 8) | second</c>.
/// </remarks>
public static class IvpBroadPhaseReplay
{
    /// <summary>How many objects a case files.</summary>
    public const int ObjectCount = 8;

    /// <summary>How many steps a case takes; the probe makes the first <see cref="ObjectCount"/> build each object's node.</summary>
    public const int StepCount = 16;

    /// <summary>How many of a step's events are carried; the rest are in its digest.</summary>
    public const int CarriedEvents = 12;

    /// <summary>A step that refiles its object — <c>FUN_180098880</c>.</summary>
    public const int Refile = 0;

    /// <summary>A step that gives its object a new node — <c>FUN_180096eb0</c>.</summary>
    public const int Rebuild = 1;

    /// <summary>A step that refiles its object while a pair creation is marked running.</summary>
    public const int RefileCreating = 2;

    /// <summary>An event: the filter asked.</summary>
    public const int Filtered = 1;

    /// <summary>An event: a creator declined.</summary>
    public const int Declined = 2;

    /// <summary>An event: a creator made a watcher.</summary>
    public const int Created = 3;

    /// <summary>An event: a watcher deleted.</summary>
    public const int Deleted = 4;

    /// <summary>An event: a creator told an object's node is going.</summary>
    public const int Removed = 5;

    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } =
    [
        new("friction-group", IvpReplayKind.Whole32, ObjectCount),
        new("hull-time", IvpReplayKind.Real64, ObjectCount),
        new("hull-gradient", IvpReplayKind.Real32, ObjectCount),
        new("hull-value", IvpReplayKind.Real32, ObjectCount),
        new("filter", IvpReplayKind.Whole32, ObjectCount),
        new("step", IvpReplayKind.Real64, 1),
        new("kind", IvpReplayKind.Whole32, StepCount),
        new("object", IvpReplayKind.Whole32, StepCount),
        new("now", IvpReplayKind.Real64, StepCount),
        new("radius", IvpReplayKind.Real32, StepCount),
        new("center", IvpReplayKind.Real64, StepCount * 3),
        new("linear", IvpReplayKind.Real32, StepCount),
        new("surface", IvpReplayKind.Real32, StepCount),
        new("flags", IvpReplayKind.Whole32, StepCount),
        new("core-bits", IvpReplayKind.Whole32, StepCount),
    ];

    /// <summary>What each step leaves.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("events", IvpReplayKind.Whole32, StepCount * CarriedEvents),
        new("event-count", IvpReplayKind.Whole32, StepCount),
        new("event-digest", IvpReplayKind.Real64, StepCount),
        new("node-center", IvpReplayKind.Real32, StepCount * 3),
        new("node-radius", IvpReplayKind.Real32, StepCount),
        new("key", IvpReplayKind.Whole32, StepCount * 5),
        new("hull-key", IvpReplayKind.Real32, StepCount),
        new("runs", IvpReplayKind.Whole32, StepCount),
        new("watchers", IvpReplayKind.Whole32, StepCount * ObjectCount),
        new("hull-counts", IvpReplayKind.Whole32, StepCount * ObjectCount),
    ];

    /// <summary>An event's lane.</summary>
    /// <param name="kind">The kind.</param>
    /// <param name="creator">The creator's index, or zero.</param>
    /// <param name="first">The first object's index.</param>
    /// <param name="second">The second object's index, or zero.</param>
    /// <returns>The lane.</returns>
    public static int Event(int kind, int creator, int first, int second) => (kind << 24) | (creator << 16) | (first << 8) | second;

    /// <summary>Whether the probe's creator at an index declines a pair.</summary>
    /// <param name="creator">The creator's index.</param>
    /// <param name="first">The first object's index.</param>
    /// <param name="second">The second object's index.</param>
    /// <returns>Whether it declines.</returns>
    public static bool Declines(int creator, int first, int second) => creator == 1 && (first + second) % 3 == 0;

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        List<int> events = [];
        IvpCollisionObject[] objects = new IvpCollisionObject[ObjectCount];
        Dictionary<IvpCollisionObject, int> indices = new(ReferenceEqualityComparer.Instance);
        IvpRigidBody[] groups = new IvpRigidBody[ObjectCount];
        IvpCollisionEnvironment environment = new()
        {
            Filter = (first, second) =>
            {
                events.Add(Event(Filtered, 0, indices[first], indices[second]));
                return ((IvpImpactReplay.Whole32(inputs, "filter", indices[first]) >> indices[second]) & 1) != 0;
            },
            Step = IvpImpactReplay.Real64(inputs, "step", 0),
        };

        for (int index = 0; index < ObjectCount; index++)
        {
            groups[index] = new IvpRigidBody();
        }

        for (int index = 0; index < ObjectCount; index++)
        {
            objects[index] = new IvpCollisionObject
            {
                Core = new IvpRigidBody(),
                FrictionCore = groups[IvpImpactReplay.Whole32(inputs, "friction-group", index)],
                Environment = environment,
            };
            objects[index].Hull.Time = IvpImpactReplay.Real64(inputs, "hull-time", index);
            objects[index].Hull.Gradient = IvpImpactReplay.Real32(inputs, "hull-gradient", index);
            objects[index].Hull.Value = IvpImpactReplay.Real32(inputs, "hull-value", index);
            indices[objects[index]] = index;
        }

        environment.Creators.Add(new Creator(0, events, indices));
        environment.Creators.Add(new Creator(1, events, indices));

        Dictionary<string, long[]> outputs = NewOutputs();

        for (int step = 0; step < StepCount; step++)
        {
            IvpCollisionObject collisionObject = objects[IvpImpactReplay.Whole32(inputs, "object", step)];
            IvpRigidBody core = collisionObject.Core!;
            int bits = IvpImpactReplay.Whole32(inputs, "core-bits", step);

            events.Clear();
            environment.Now = IvpImpactReplay.Real64(inputs, "now", step);
            core.Radius = IvpImpactReplay.Real32(inputs, "radius", step);
            core.CoreMatrix = core.CoreMatrix with
            {
                Translation = (
                    IvpImpactReplay.Real64(inputs, "center", step * 3),
                    IvpImpactReplay.Real64(inputs, "center", (step * 3) + 1),
                    IvpImpactReplay.Real64(inputs, "center", (step * 3) + 2)),
            };
            core.LinearSpeed = IvpImpactReplay.Real32(inputs, "linear", step);
            core.SurfaceSpeedBound = IvpImpactReplay.Real32(inputs, "surface", step);
            core.Immovable = (bits & 0x2) != 0;
            core.SkipsGravity = (bits & 0x10) != 0;
            collisionObject.MovementState = IvpImpactReplay.Whole32(inputs, "flags", step);

            switch (IvpImpactReplay.Whole32(inputs, "kind", step))
            {
                case Rebuild:
                    IvpBroadPhase.Rebuild(environment, collisionObject);
                    break;
                case RefileCreating:
                    environment.MindistManager.CreatingPairs = true;
                    IvpBroadPhase.Refile(environment, collisionObject);
                    environment.MindistManager.CreatingPairs = false;
                    break;
                default:
                    IvpBroadPhase.Refile(environment, collisionObject);
                    break;
            }

            RecordEvents(outputs, step, events);

            if (collisionObject.Node is { } node)
            {
                outputs["node-center"][step * 3] = IvpImpactReplay.Lane(node.Center.X);
                outputs["node-center"][(step * 3) + 1] = IvpImpactReplay.Lane(node.Center.Y);
                outputs["node-center"][(step * 3) + 2] = IvpImpactReplay.Lane(node.Center.Z);
                outputs["node-radius"][step] = IvpImpactReplay.Lane(node.Radius);

                if (node.Cell is { } cell)
                {
                    IvpOvTreeReplay.WriteKey(outputs["key"], step, cell.X, cell.Y, cell.Z, cell.Level, cell.Exponent);
                }

                if (node.HullManager is { } manager && node.HullSlot is int slot)
                {
                    outputs["hull-key"][step] = IvpImpactReplay.Lane(manager.Synapses.ValueOf(slot));
                }
            }

            outputs["runs"][step] = environment.BroadPhaseRuns;

            for (int index = 0; index < ObjectCount; index++)
            {
                outputs["watchers"][(step * ObjectCount) + index] = objects[index].Node?.Watchers.Count ?? 0;
                outputs["hull-counts"][(step * ObjectCount) + index] = objects[index].Hull.Synapses.Count;
            }
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

    /// <summary>A step's events into its lanes: the first <see cref="CarriedEvents"/>, the count and an FNV-1a digest.</summary>
    internal static void RecordEvents(Dictionary<string, long[]> outputs, int step, List<int> events)
    {
        ulong hash = 14695981039346656037UL;

        for (int index = 0; index < events.Count; index++)
        {
            if (index < CarriedEvents)
            {
                outputs["events"][(step * CarriedEvents) + index] = events[index];
            }

            hash = unchecked((hash ^ (uint)events[index]) * 1099511628211UL);
        }

        outputs["event-count"][step] = events.Count;
        outputs["event-digest"][step] = unchecked((long)hash);
    }

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

    /// <summary>The probe's creator: a watcher on both nodes, unless it is the one that declines this pair.</summary>
    private sealed class Creator(int index, List<int> events, Dictionary<IvpCollisionObject, int> indices) : IIvpCollisionCreator
    {
        public IvpCollision? Create(IvpCollisionObject first, IvpCollisionObject second)
        {
            if (Declines(index, indices[first], indices[second]))
            {
                events.Add(Event(Declined, index, indices[first], indices[second]));
                return null;
            }

            events.Add(Event(Created, index, indices[first], indices[second]));

            Watcher watcher = new(this, first, second, events, indices);

            first.Node!.Register(watcher);
            second.Node!.Register(watcher);
            return watcher;
        }

        public void CollisionRemoved(IvpCollision collision)
        {
            (IvpCollisionObject first, IvpCollisionObject second) = collision.Objects;

            first.Node!.Unregister(collision);
            second.Node!.Unregister(collision);
        }

        public void ObjectRemoved(IvpCollisionObject removed)
        {
            events.Add(Event(Removed, index, indices[removed], 0));

            if (removed.Node is not { } node)
            {
                return;
            }

            for (int place = node.Watchers.Count - 1; place >= 0; place--)
            {
                node.Watchers[place].Delete();
            }
        }
    }

    /// <summary>The probe's watcher: off both nodes through its creator when deleted, the first object's first.</summary>
    private sealed class Watcher(
        Creator creator, IvpCollisionObject first, IvpCollisionObject second, List<int> events, Dictionary<IvpCollisionObject, int> indices) : IvpCollision
    {
        public override (IvpCollisionObject First, IvpCollisionObject Second) Objects => (first, second);

        public override void Delete()
        {
            events.Add(Event(Deleted, 0, indices[first], indices[second]));
            creator.CollisionRemoved(this);
        }
    }
}
