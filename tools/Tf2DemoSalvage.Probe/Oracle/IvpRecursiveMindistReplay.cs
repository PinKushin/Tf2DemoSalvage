using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// IVP's larger mindist — built by <c>IvpPairMindists::Refresh</c> for a hull ledge with children, frozen through
/// <c>IvpRecursiveMindist::Freeze</c>, collided through <c>IvpRecursiveMindist::Collide</c>, told its hull passed through a record's slot 1
/// into <c>IvpRecursiveMindist::HullPassed</c>, and deleted — as lanes of bits (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-recursive-mindist` probe from the shipped `vphysics.dll` and
/// replayed by `IvpRecursiveMindistReplayConformanceTests`. The objects are <see cref="IvpPairMindistsReplay"/>'s, their trees holding hull
/// ledges with children and every ledge stub decodable as one triangle whose header and edge words carry the case's bit 31; each object has
/// a hull manager and state bits, and the environment has the binary's own range manager.
///
/// **Recorded rather than run**: a new mindist's exact tail, which names it and links it exact without a minimize
/// (<c>IvpMindistManager::Revalidate</c>); the plain collision <c>IvpMindist::Collide</c>, which names the mindist; and the minimize with no
/// budget, <c>IvpMindistMinimize::MinimizeWithoutBudget</c>, which names it, ORs the step's bits into its flags and writes the step's
/// length. **The outer delegator is the case's**: slot 0 takes a mindist out of the pair, slot 2 names each change it is told, and slot 3
/// answers the case's count.
///
/// Step 0 refreshes the pair. Each later step refreshes it again, or picks a larger mindist in the state its action needs — exact to
/// freeze or collide, opened to be told its hull passed — and acts on it, a step with no such mindist doing nothing. The case ends by
/// deleting the pair.
/// </remarks>
public static class IvpRecursiveMindistReplay
{
    /// <summary>How many steps a case runs.</summary>
    public const int StepCount = 8;

    /// <summary>A ledge stub's size: its header, then one triangle's header and three edge words.</summary>
    public const int StubSize = 0x20;

    /// <summary>An action: the pair refreshed.</summary>
    public const int ActionRefresh = 0;

    /// <summary>An action: an exact larger mindist's slot 7.</summary>
    public const int ActionFreeze = 1;

    /// <summary>An action: an exact larger mindist's slot 8, with the step's feature kinds.</summary>
    public const int ActionCollide = 2;

    /// <summary>An action: an opened larger mindist's record told its hull passed.</summary>
    public const int ActionHullPassed = 3;

    /// <summary>An event: the plain collision ran on a mindist.</summary>
    public const int Collided = 4;

    /// <summary>An event: the minimize with no budget ran on a mindist.</summary>
    public const int Rechecked = 5;

    /// <summary>An event: the outer delegator was told a change, <c>(6 &lt;&lt; 16) | (change &amp; 0xffff)</c>.</summary>
    public const int Added = 6;

    /// <summary>
    /// The flag bits a mindist's constructor writes: <c>IvpMindist::IvpMindist</c> clears the low byte and ANDs with <c>0xcfc000ff</c>, so
    /// bits 30 and 31 keep whatever the allocation held. A flags lane carries only the others.
    /// </summary>
    public const int ConstructedFlags = 0x3fffffff;

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
        new("exact-count", IvpReplayKind.Whole32, StepCount),
        new("invalid-count", IvpReplayKind.Whole32, StepCount),
        new("chosen", IvpReplayKind.Whole32, StepCount),
        new("chosen-flags", IvpReplayKind.Whole32, StepCount),
        new("chosen-open", IvpReplayKind.Whole32, StepCount),
        new("chosen-total", IvpReplayKind.Whole32, StepCount),
        new("members", IvpReplayKind.Whole32, StepCount * IvpPairMindistsReplay.Carried),
        new("member-count", IvpReplayKind.Whole32, StepCount),
        new("member-digest", IvpReplayKind.Real64, StepCount),
        new("record-slot", IvpReplayKind.Whole32, StepCount * 2),
        new("record-key", IvpReplayKind.Real32, StepCount * 2),
        new("min-count", IvpReplayKind.Whole32, StepCount * 2),
        new("end-events", IvpReplayKind.Whole32, IvpPairMindistsReplay.Carried),
        new("end-event-count", IvpReplayKind.Whole32, 1),
        new("end-event-digest", IvpReplayKind.Real64, 1),
        new("end-live", IvpReplayKind.Whole32, 1),
        new("end-deleted", IvpReplayKind.Whole32, 1),
        new("end-min-count", IvpReplayKind.Whole32, 2),
    ];

    /// <summary>A side's surface bytes: the pair replay's tree, each ledge stub made one triangle carrying the case's bit 31.</summary>
    /// <param name="inputs">The case's inputs.</param>
    /// <param name="side">0 or 1.</param>
    /// <returns>The surface, and each node's and each ledge's offset within it (−1 for an unused lane).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    /// <exception cref="InvalidDataException">The kinds do not describe a tree in preorder.</exception>
    /// <remarks>
    /// A stub's point word names the surface's first byte, whose header holds three zero points, and its count is one; the triangle's
    /// header is its own index, zero, with bit 31 from the lane's bit 0, and edge word <c>i</c> starts at point <c>i</c> with bit 31 from the
    /// lane's bit <c>i + 1</c>. The binary reads only the words' signs and the header's index on these paths.
    /// </remarks>
    public static (byte[] Surface, int[] Nodes, int[] Ledges) Surface(IReadOnlyDictionary<string, long[]> inputs, int side)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        string suffix = IvpPairMindistsReplay.Suffix(side);
        (byte[] surface, int[] nodes, int[] ledges) = IvpLedgeTreeReplay.Surface(inputs, suffix, StubSize);
        long[] bits = inputs["virtual" + suffix];

        for (int lane = 0; lane < ledges.Length; lane++)
        {
            int at = ledges[lane];

            if (at < 0)
            {
                continue;
            }

            int word = (int)bits[lane];

            Write(surface, at, -at);
            Write(surface, at + 0xC, 1);
            Write(surface, at + 0x10, (word & 1) != 0 ? int.MinValue : 0);

            for (int edge = 0; edge < 3; edge++)
            {
                Write(surface, at + 0x14 + (edge * 4), ((word >> (edge + 1)) & 1) != 0 ? edge | int.MinValue : edge);
            }
        }

        return (surface, nodes, ledges);
    }

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    /// <exception cref="InvalidDataException">A synthesized surface does not read as a tree, or a far pair was handed off.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        List<int> events = [];
        Dictionary<PhysicsLedgeTreeNode, int>[] lanes = [[], []];
        List<IvpCollision> pair = [];
        Action<IvpMindist> recheck = _ => throw new InvalidDataException("A mindist was rechecked outside a hull pass.");
        IvpCollisionEnvironment environment = new()
        {
            Filter = (_, _) => true,
            BecomeExact = mindist =>
            {
                events.Add(IvpPairMindistsReplay.NameOf(IvpPairMindistsReplay.Exact, mindist, lanes));

                (IvpCollisionObject first, IvpCollisionObject second) = mindist.Objects;

                (first.Environment ?? throw new InvalidDataException("A mindist's object has no environment.")).MindistManager.Revalidate(mindist, first, second);
            },
            BecomePhantom = mindist => events.Add(IvpPairMindistsReplay.NameOf(IvpPairMindistsReplay.Phantom, mindist, lanes)),
            HullPassed = (mindist, overshoot) => IvpMindistHull.HullPassed(mindist, overshoot, Pass(mindist, recheck)),
        };
        IvpCollisionObject[] objects = IvpPairMindistsReplay.NewObjects(inputs, environment, lanes, Surface);
        Outer outer = new(pair, events, lanes, IvpImpactReplay.Whole32(inputs, "outer-answer", 0));
        Dictionary<string, long[]> outputs = NewOutputs();

        environment.Step = IvpImpactReplay.Real64(inputs, "step", 0);

        for (int side = 0; side < 2; side++)
        {
            IvpHullManager hull = objects[side].Hull;

            objects[side].MovementState = 8 | IvpImpactReplay.Whole32(inputs, "state", side);
            hull.Time = IvpImpactReplay.Real64(inputs, "hull-time", side);
            hull.Gradient = IvpImpactReplay.Real32(inputs, "hull-gradient", side);
            hull.Value = IvpImpactReplay.Real32(inputs, "hull-value", side);
            hull.NextPsiValue = IvpImpactReplay.Real32(inputs, "next-psi", side);
        }

        for (int step = 0; step < StepCount; step++)
        {
            events.Clear();
            IvpPairMindistsReplay.Place(inputs, environment, objects, step);

            for (int side = 0; side < 2; side++)
            {
                IvpRigidBody core = objects[side].Core ?? throw new InvalidDataException("A pair object has no core.");

                core.LinearSpeed = IvpImpactReplay.Real32(inputs, "linear", (step * 2) + side);
                core.SurfaceSpeedBound = IvpImpactReplay.Real32(inputs, "surface", (step * 2) + side);
            }

            int action = step == 0 ? ActionRefresh : IvpImpactReplay.Whole32(inputs, "action", step);
            IvpRecursiveMindist? chosen = null;

            if (action == ActionRefresh)
            {
                IvpPairMindists.Refresh(
                    objects[0], objects[1], IvpImpactReplay.Real64(inputs, step == 0 ? "gap" : "refresh-gap", step == 0 ? 0 : step), pair,
                    null, null, null, null, outer);
            }
            else
            {
                chosen = Choose(pair, action == ActionHullPassed ? IvpMindistHull.RecursiveState : IvpMindistHull.ExactState, IvpImpactReplay.Whole32(inputs, "pick", step));

                if (chosen is not null)
                {
                    Act(chosen, action, inputs, step, objects, environment, events, lanes, value => recheck = value);
                }
            }

            RecordStep(outputs, step, pair, events, lanes, environment, objects, chosen);
        }

        events.Clear();

        for (int index = pair.Count - 1; index >= 0; index--)
        {
            pair[index].Delete();
        }

        IvpPairMindistsReplay.Record(outputs, "end-event", 0, events);
        outputs["end-live"][0] = environment.LiveMindists;
        outputs["end-deleted"][0] = environment.DeletedMindists;

        for (int side = 0; side < 2; side++)
        {
            outputs["end-min-count"][side] = objects[side].Hull.Synapses.Count;
        }

        return outputs;
    }

    /// <summary>The larger mindist a step acts on: of the pair's larger mindists in a state, the one the pick names, counting modulo their number.</summary>
    /// <param name="pair">The pair, in order.</param>
    /// <param name="state">The flags' state bits wanted.</param>
    /// <param name="pick">The step's pick.</param>
    /// <returns>The mindist, or null when the pair holds none in that state.</returns>
    public static IvpRecursiveMindist? Choose(IReadOnlyList<IvpCollision> pair, int state, int pick)
    {
        ArgumentNullException.ThrowIfNull(pair);

        List<IvpRecursiveMindist> candidates = [];

        foreach (IvpCollision collision in pair)
        {
            if (collision is IvpRecursiveMindist larger && (larger.Flags & IvpMindistHull.StateMask) == state)
            {
                candidates.Add(larger);
            }
        }

        return candidates.Count == 0 ? null : candidates[(int)((uint)pick % (uint)candidates.Count)];
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

    private static void Act(
        IvpRecursiveMindist chosen, int action, IReadOnlyDictionary<string, long[]> inputs, int step, IvpCollisionObject[] objects,
        IvpCollisionEnvironment environment, List<int> events, Dictionary<PhysicsLedgeTreeNode, int>[] lanes, Action<Action<IvpMindist>> setRecheck)
    {
        switch (action)
        {
            case ActionFreeze:
                chosen.Freeze(environment.MindistManager, environment.EventQueue);
                break;
            case ActionCollide:
                int kinds = IvpImpactReplay.Whole32(inputs, "kinds", step);

                chosen.SetSynapse(0, new IvpSynapse(new IvpLedgeEdge(0, (kinds >> 16) & 0xff), (IvpFeatureKind)(kinds & 0xff)));
                chosen.SetSynapse(1, new IvpSynapse(new IvpLedgeEdge(0, (kinds >> 24) & 0xff), (IvpFeatureKind)((kinds >> 8) & 0xff)));
                chosen.Collide(mindist => events.Add(IvpPairMindistsReplay.NameOf(Collided, mindist, lanes)));
                break;
            default:
                int bits = IvpImpactReplay.Whole32(inputs, "recheck-bits", step);
                float length = IvpImpactReplay.Real32(inputs, "recheck-length", step);
                int record = IvpImpactReplay.Whole32(inputs, "record", step);

                setRecheck(mindist =>
                {
                    events.Add(IvpPairMindistsReplay.NameOf(Rechecked, mindist, lanes));
                    mindist.Flags |= bits;
                    mindist.Length = length;
                });
                chosen.HullRecord(record).HullPassed(objects[record].Hull, 0f);
                break;
        }
    }

    private static void RecordStep(
        Dictionary<string, long[]> outputs, int step, List<IvpCollision> pair, List<int> events, Dictionary<PhysicsLedgeTreeNode, int>[] lanes,
        IvpCollisionEnvironment environment, IvpCollisionObject[] objects, IvpRecursiveMindist? chosen)
    {
        IvpPairMindistsReplay.Record(outputs, "pair", step, pair.ConvertAll(collision => IvpPairMindistsReplay.NameOf(0, (IvpMindist)collision, lanes)));
        IvpPairMindistsReplay.Record(outputs, "event", step, events);
        outputs["live"][step] = environment.LiveMindists;
        outputs["created"][step] = environment.CreatedMindists;
        outputs["deleted"][step] = environment.DeletedMindists;
        outputs["refreshes"][step] = environment.WatcherRefreshes;
        outputs["exact-count"][step] = environment.MindistManager.Exact.Count;
        outputs["invalid-count"][step] = environment.MindistManager.Invalid.Count;
        outputs["chosen"][step] = chosen is null ? -1 : IvpPairMindistsReplay.NameOf(0, chosen, lanes);

        List<int> members = [];
        int state = chosen is null ? 0 : chosen.Flags & IvpMindistHull.StateMask;
        bool filed = chosen is not null && (state == IvpMindistHull.RecursiveState || state == IvpMindistHull.FiledState);

        if (chosen is not null)
        {
            outputs["chosen-flags"][step] = chosen.Flags & ConstructedFlags;
            outputs["chosen-open"][step] = chosen.OpenSide;
            outputs["chosen-total"][step] = chosen.Total;

            foreach (IvpCollision member in chosen.Children)
            {
                members.Add(IvpPairMindistsReplay.NameOf(0, (IvpMindist)member, lanes));
            }
        }

        IvpPairMindistsReplay.Record(outputs, "member", step, members);

        for (int side = 0; side < 2; side++)
        {
            int at = (step * 2) + side;
            IvpHullManager hull = objects[side].Hull;
            int slot = filed ? chosen!.HullRecord(side).HullSlot ?? throw new InvalidDataException("An opened mindist's record is not filed.") : -1;

            outputs["record-slot"][at] = slot;
            outputs["record-key"][at] = filed ? IvpImpactReplay.Lane(hull.Synapses.ValueOf(slot)) : 0;
            outputs["min-count"][at] = hull.Synapses.Count;
        }
    }

    private static IvpHullPass Pass(IvpMindist mindist, Action<IvpMindist> recheck)
    {
        (IvpCollisionObject first, IvpCollisionObject second) = mindist.Objects;
        IvpCollisionEnvironment environment = first.Environment ?? throw new InvalidDataException("A mindist's object has no environment.");
        IvpRigidBody firstCore = first.Core ?? throw new InvalidDataException("A pair object has no core.");
        IvpRigidBody secondCore = second.Core ?? throw new InvalidDataException("A pair object has no core.");

        return new IvpHullPass
        {
            Now = environment.Now,
            Step = environment.Step,
            First = first,
            Second = second,
            FirstBody = firstCore,
            SecondBody = secondCore,
            FirstBounds = IvpRangeManager.Bounds(firstCore),
            SecondBounds = IvpRangeManager.Bounds(secondCore),
            HandOff = _ => throw new InvalidDataException("A far pair was handed off in a larger mindist case."),
            Recheck = recheck,
        };
    }

    private static List<IvpReplayField> BuildInputs()
    {
        List<IvpReplayField> fields = [];

        IvpPairMindistsReplay.AddObjectFields(fields);
        fields.AddRange(
        [
            new("virtual-first", IvpReplayKind.Whole32, IvpPairMindistsReplay.NodeCount),
            new("virtual-second", IvpReplayKind.Whole32, IvpPairMindistsReplay.NodeCount),
            new("step", IvpReplayKind.Real64, 1),
            new("state", IvpReplayKind.Whole32, 2),
            new("hull-time", IvpReplayKind.Real64, 2),
            new("hull-gradient", IvpReplayKind.Real32, 2),
            new("hull-value", IvpReplayKind.Real32, 2),
            new("next-psi", IvpReplayKind.Real32, 2),
            new("outer-answer", IvpReplayKind.Whole32, 1),
            new("gap", IvpReplayKind.Real64, 1),
        ]);
        IvpPairMindistsReplay.AddPlacementFields(fields, StepCount);
        fields.AddRange(
        [
            new("linear", IvpReplayKind.Real32, StepCount * 2),
            new("surface", IvpReplayKind.Real32, StepCount * 2),
            new("action", IvpReplayKind.Whole32, StepCount),
            new("pick", IvpReplayKind.Whole32, StepCount),
            new("kinds", IvpReplayKind.Whole32, StepCount),
            new("record", IvpReplayKind.Whole32, StepCount),
            new("recheck-bits", IvpReplayKind.Whole32, StepCount),
            new("recheck-length", IvpReplayKind.Real32, StepCount),
            new("refresh-gap", IvpReplayKind.Real64, StepCount),
        ]);

        return fields;
    }

    private static void Write(byte[] bytes, int at, int value) => BitConverter.TryWriteBytes(bytes.AsSpan(at), value);

    /// <summary>The case's outer delegator: slot 0 takes a mindist out of the pair, slot 2 names each change, slot 3 answers the case's count.</summary>
    private sealed class Outer(List<IvpCollision> pair, List<int> events, Dictionary<PhysicsLedgeTreeNode, int>[] lanes, int answer) : IIvpCollisionDelegator
    {
        public void CollisionRemoved(IvpCollision collision)
        {
            events.Add(IvpPairMindistsReplay.NameOf(IvpPairMindistsReplay.Removed, (IvpMindist)collision, lanes));
            IvpCollisionList.Remove(pair, collision);
        }

        public void MindistsAdded(int change) => events.Add((Added << 16) | (change & 0xffff));

        public int MindistsBeneath() => answer;
    }
}
