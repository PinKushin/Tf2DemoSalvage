using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// A broad-phase pair's watcher — made by the default creator (<c>FUN_1800a06f0</c>, <c>FUN_1800b5dd0</c>), refreshed when a hull passes
/// one of its records (<c>FUN_1800b6170</c>, <c>FUN_1800b6080</c>), and ended by its destructor (<c>FUN_1800b5e80</c>) or its creator's
/// removal notice (<c>FUN_1800a07a0</c>) — as lanes of bits (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-pair-watcher` probe from the shipped `vphysics.dll` and
/// replayed by `IvpPairWatcherConformanceTests`. The two objects are <see cref="IvpPairMindistsReplay"/>'s — resting, synthesized
/// surfaces, the mindist constructor's tails recorded rather than run — with a hull manager, an OV node and the binary's own range
/// manager added. Step 0 files each node in its object's hull manager, as the broad phase does before any creator runs, registers the
/// case's other collisions on each node, and makes the watcher; each later step tells one of its records that its hull passed; the
/// case ends one of three ways. **The other collisions are the probe's**: each, told to go by the creator's removal notice, names
/// itself into the ending's events and takes itself off its node through `FUN_18009ef40`. **The pair's delegator is the binary's own**
/// (`FUN_1800b5fd0`), so a mindist's removal shows only in the pair and the counters.
/// </remarks>
public static class IvpPairWatcherReplay
{
    /// <summary>How many steps a case runs: the watcher made, then refreshed five times.</summary>
    public const int StepCount = 6;

    /// <summary>The most other collisions a node holds before the watcher.</summary>
    public const int MostOthers = 3;

    /// <summary>An ending: the watcher's own slot 0 with 1.</summary>
    public const int EndDeleted = 0;

    /// <summary>An ending: the creator told the first object is leaving.</summary>
    public const int EndFirstRemoved = 1;

    /// <summary>An ending: the creator told the second object is leaving.</summary>
    public const int EndSecondRemoved = 2;

    /// <summary>An event: one of a node's other collisions was deleted — <c>(4 &lt;&lt; 16) | (side &lt;&lt; 8) | index</c>.</summary>
    public const int OtherDeleted = 4;

    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } = BuildInputs();

    /// <summary>What each step, and the ending, leaves.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } =
    [
        new("pair", IvpReplayKind.Whole32, StepCount * IvpPairMindistsReplay.Carried),
        new("pair-count", IvpReplayKind.Whole32, StepCount),
        new("pair-digest", IvpReplayKind.Real64, StepCount),
        new("events", IvpReplayKind.Whole32, StepCount * IvpPairMindistsReplay.Carried),
        new("event-count", IvpReplayKind.Whole32, StepCount),
        new("event-digest", IvpReplayKind.Real64, StepCount),
        new("live", IvpReplayKind.Whole32, StepCount),
        new("created", IvpReplayKind.Whole32, StepCount),
        new("deleted", IvpReplayKind.Whole32, StepCount),
        new("refreshes", IvpReplayKind.Whole32, StepCount),
        new("record-slot", IvpReplayKind.Whole32, StepCount * 2),
        new("record-key", IvpReplayKind.Real32, StepCount * 2),
        new("node-watchers", IvpReplayKind.Whole32, StepCount * 2),
        new("watcher-index", IvpReplayKind.Whole32, StepCount * 2),
        new("end-events", IvpReplayKind.Whole32, IvpPairMindistsReplay.Carried),
        new("end-event-count", IvpReplayKind.Whole32, 1),
        new("end-event-digest", IvpReplayKind.Real64, 1),
        new("end-live", IvpReplayKind.Whole32, 1),
        new("end-deleted", IvpReplayKind.Whole32, 1),
        new("end-watchers", IvpReplayKind.Whole32, 2),
        new("end-hull-count", IvpReplayKind.Whole32, 2),
    ];

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    /// <exception cref="InvalidDataException">A synthesized surface does not read as a tree, or the creator made no watcher.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        List<int> events = [];
        Dictionary<PhysicsLedgeTreeNode, int>[] lanes = [[], []];
        IvpCollisionEnvironment environment = IvpPairMindistsReplay.NewEnvironment(events, lanes);
        IvpCollisionObject[] objects = IvpPairMindistsReplay.NewObjects(inputs, environment, lanes);
        IvpPairCreator creator = new();
        IvpPairWatcher? watcher = null;
        Dictionary<string, long[]> outputs = NewOutputs();

        environment.Step = IvpImpactReplay.Real64(inputs, "step", 0);

        for (int side = 0; side < 2; side++)
        {
            IvpHullManager hull = objects[side].Hull;

            objects[side].Node = new IvpOvNode(objects[side]);
            hull.Time = IvpImpactReplay.Real64(inputs, "hull-time", side);
            hull.Gradient = IvpImpactReplay.Real32(inputs, "hull-gradient", side);
            hull.Value = IvpImpactReplay.Real32(inputs, "hull-value", side);
        }

        for (int step = 0; step < StepCount; step++)
        {
            events.Clear();
            IvpPairMindistsReplay.Place(inputs, environment, objects, step);

            for (int side = 0; side < 2; side++)
            {
                IvpRigidBody core = objects[side].Core!;

                core.LinearSpeed = IvpImpactReplay.Real32(inputs, "linear", (step * 2) + side);
                core.SurfaceSpeedBound = IvpImpactReplay.Real32(inputs, "surface", (step * 2) + side);
            }

            if (watcher is null)
            {
                for (int side = 0; side < 2; side++)
                {
                    IvpOvNode node = objects[side].Node!;

                    node.File(objects[side].Hull, environment.Now, IvpImpactReplay.Real64(inputs, "node-gap", side));

                    for (int index = 0; index < IvpImpactReplay.Whole32(inputs, "others", side); index++)
                    {
                        node.Register(new Other(objects[side], side, index, events));
                    }
                }

                watcher = creator.Create(objects[0], objects[1]) as IvpPairWatcher ?? throw new InvalidDataException("The creator made no watcher.");
            }
            else
            {
                int passed = IvpImpactReplay.Whole32(inputs, "passed", step);

                (passed == 0 ? watcher.FirstRecord : watcher.SecondRecord).HullPassed(objects[passed].Hull, 0f);
            }

            List<int> names = [];

            foreach (IvpCollision collision in watcher.Pair)
            {
                names.Add(IvpPairMindistsReplay.NameOf(0, (IvpMindist)collision, lanes));
            }

            IvpPairMindistsReplay.Record(outputs, "pair", step, names);
            IvpPairMindistsReplay.Record(outputs, "event", step, events);
            outputs["live"][step] = environment.LiveMindists;
            outputs["created"][step] = environment.CreatedMindists;
            outputs["deleted"][step] = environment.DeletedMindists;
            outputs["refreshes"][step] = environment.WatcherRefreshes;

            for (int side = 0; side < 2; side++)
            {
                int at = (step * 2) + side;
                int slot = (side == 0 ? watcher.FirstRecord : watcher.SecondRecord).HullSlot ?? throw new InvalidDataException("A watcher record is not filed.");

                outputs["record-slot"][at] = slot;
                outputs["record-key"][at] = IvpImpactReplay.Lane(objects[side].Hull.Synapses.ValueOf(slot));
                outputs["node-watchers"][at] = objects[side].Node!.Watchers.Count;
                outputs["watcher-index"][at] = side == 0 ? watcher.FirstIndex : watcher.SecondIndex;
            }
        }

        int ending = IvpImpactReplay.Whole32(inputs, "ending", 0);

        events.Clear();

        if (ending == EndDeleted)
        {
            watcher!.Delete();
        }
        else
        {
            creator.ObjectRemoved(objects[ending - EndFirstRemoved]);
        }

        IvpPairMindistsReplay.Record(outputs, "end-event", 0, events);
        outputs["end-live"][0] = environment.LiveMindists;
        outputs["end-deleted"][0] = environment.DeletedMindists;

        for (int side = 0; side < 2; side++)
        {
            outputs["end-watchers"][side] = objects[side].Node!.Watchers.Count;
            outputs["end-hull-count"][side] = objects[side].Hull.Synapses.Count;
        }

        return outputs;
    }

    /// <summary>A zeroed output dictionary.</summary>
    /// <returns>The outputs.</returns>
    internal static Dictionary<string, long[]> NewOutputs()
    {
        Dictionary<string, long[]> outputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in Outputs)
        {
            outputs[field.Name] = new long[field.Count];
        }

        return outputs;
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

    private static List<IvpReplayField> BuildInputs()
    {
        List<IvpReplayField> fields = [];

        IvpPairMindistsReplay.AddObjectFields(fields);
        fields.AddRange(
        [
            new("step", IvpReplayKind.Real64, 1),
            new("hull-time", IvpReplayKind.Real64, 2),
            new("hull-gradient", IvpReplayKind.Real32, 2),
            new("hull-value", IvpReplayKind.Real32, 2),
            new("node-gap", IvpReplayKind.Real64, 2),
            new("others", IvpReplayKind.Whole32, 2),
        ]);
        IvpPairMindistsReplay.AddPlacementFields(fields, StepCount);
        fields.AddRange(
        [
            new("linear", IvpReplayKind.Real32, StepCount * 2),
            new("surface", IvpReplayKind.Real32, StepCount * 2),
            new("passed", IvpReplayKind.Whole32, StepCount),
            new("ending", IvpReplayKind.Whole32, 1),
        ]);

        return fields;
    }

    /// <summary>The probe's other collision on a node: when deleted, it names itself and takes itself off the node.</summary>
    private sealed class Other(IvpCollisionObject owner, int side, int index, List<int> events) : IvpCollision
    {
        public override (IvpCollisionObject First, IvpCollisionObject Second) Objects => (owner, owner);

        public override void Delete()
        {
            events.Add(IvpPairMindistsReplay.Name(OtherDeleted, side, index));
            owner.Node!.Unregister(this);
        }
    }
}
