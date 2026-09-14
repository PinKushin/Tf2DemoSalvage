using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// A friction system's heap solve as lanes of bits — its contact list sorted (<c>FUN_1800a9bf0</c>'s first loop) and solved
/// (<c>FUN_1800a9bf0</c> from <c>+0x7c</c> on, with <c>FUN_1800a9520</c>, <c>FUN_1800aa5c0</c>, <c>FUN_1800aa9f0</c>,
/// <c>FUN_1800aa1a0</c> and <c>FUN_1800aa010</c> under it) (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-heap-solve` probe from the shipped `vphysics.dll` and
/// replayed by `IvpHeapSolveConformanceTests`.
///
/// **A case describes up to eight contacts among up to four cores, and lists them `copies` times over**, so a heap past the 150
/// contacts the solve freezes at needs no more lanes. A contact is named by its place in the list as given; the per-contact
/// outputs hold the first eight places, and two digests fold in every place, so a long list is compared whole.
/// </remarks>
public static class IvpHeapSolveReplay
{
    /// <summary>The most contacts a case describes.</summary>
    public const int MostContacts = 8;

    /// <summary>The most cores a case has.</summary>
    public const int MostCores = 4;

    /// <summary>The most times a case's contacts are listed.</summary>
    public const int MostCopies = 20;

    /// <summary>The prefix of a core's vectors as the solve left them, apart from the same vectors as the case gave them.</summary>
    internal const string SolvedCore = "solved-core-";

    /// <summary>FNV-1a's 64-bit offset basis.</summary>
    private const ulong Offset = 0xcbf29ce484222325UL;

    /// <summary>FNV-1a's 64-bit prime.</summary>
    private const ulong Prime = 0x100000001b3UL;

    private static readonly string[] CoreVectors = ["velocity", "spin", "pending-velocity", "pending-spin"];

    private static readonly string[] ContactOutputs =
        ["order", "normal-push", "streak", "record-index", "record-first-share", "record-second-share", "record-streak", "record-gap"];

    private static readonly IvpLedgeEdge Edge = new(0, 0);

    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } = BuildInputs();

    /// <summary>What a case leaves.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } = BuildOutputs();

    /// <summary>The names of one core's vector fields, in the order the probe writes and reads them.</summary>
    internal static IReadOnlyList<string> VectorNames => CoreVectors;

    /// <summary>The per-contact outputs, each one lane per listed place before <see cref="Finish"/> folds them.</summary>
    internal static IReadOnlyList<string> ContactOutputNames => ContactOutputs;

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        int coreCount = IvpImpactReplay.Whole32(inputs, "cores", 0);
        int contactCount = IvpImpactReplay.Whole32(inputs, "contacts", 0);
        int listed = contactCount * IvpImpactReplay.Whole32(inputs, "copies", 0);

        IvpImpactEnvironment environment = new()
        {
            InverseStep = IvpImpactReplay.Real64(inputs, "inverse-step", 0),
            Step = 0d,
            Limits = new IvpAnomalyLimits(
                IvpImpactReplay.Real32(inputs, "max-velocity", 0),
                0,
                IvpImpactReplay.Real32(inputs, "max-spin", 0),
                0,
                IvpImpactReplay.Real32(inputs, "min-friction-mass", 0),
                0f),
            Anomalies = new VphysicsAnomalyManager(new IvpImpactReplay.FixedAnswer(IvpImpactReplay.Whole32(inputs, "freezes", 0) != 0)),
            Materials = new IvpReplayMaterials(new IvpReplayMaterial(0d, 0d, HasSecondFriction: false), 0d, 0d),
            GravityLength = IvpImpactReplay.Real32(inputs, "gravity", 0),
        };

        IvpFrictionSystem system = new(environment);
        IvpRigidBody[] cores = new IvpRigidBody[coreCount];
        IvpCollisionObject[] objects = new IvpCollisionObject[coreCount];

        for (int k = 0; k < coreCount; k++)
        {
            int flags = IvpImpactReplay.Whole32(inputs, "core-flags", k);
            IvpRigidBody core = new()
            {
                Immovable = (flags & 2) != 0,
                FlagBit0 = (flags & 1) != 0,
                Mass = IvpImpactReplay.Real32(inputs, "core-mass", k),
                Inertia = IvpImpactReplay.Vector(inputs, "core-inertia", 3 * k),
                InverseInertia = IvpImpactReplay.Vector(inputs, "core-inverse-inertia", 3 * k),
                InverseMass = IvpImpactReplay.Real32(inputs, "core-inverse-mass", k),
                Velocity = IvpImpactReplay.Vector(inputs, "core-velocity", 3 * k),
                AngularVelocity = IvpImpactReplay.Vector(inputs, "core-spin", 3 * k),
                PendingVelocity = IvpImpactReplay.Vector(inputs, "core-pending-velocity", 3 * k),
                PendingAngularVelocity = IvpImpactReplay.Vector(inputs, "core-pending-spin", 3 * k),
            };

            system.Cores.Add(core);

            if (!core.Immovable)
            {
                system.MovableCores.Add(core);
                core.FrictionInfo = new IvpFrictionInfo(system);
            }

            cores[k] = core;
            objects[k] = new IvpCollisionObject { Core = core, FrictionCore = new IvpRigidBody() };
        }

        IvpContactPoint[] points = new IvpContactPoint[listed];
        Dictionary<IvpContactPoint, int> places = new(ReferenceEqualityComparer.Instance);

        for (int place = 0; place < listed; place++)
        {
            int k = place % contactCount;
            IvpRigidBody first = cores[IvpImpactReplay.Whole32(inputs, "contact-cores", 2 * k)];
            IvpRigidBody second = cores[IvpImpactReplay.Whole32(inputs, "contact-cores", (2 * k) + 1)];
            IvpContactPoint point = new(
                new IvpMindist(new IvpSynapse(Edge, IvpFeatureKind.Point), new IvpSynapse(Edge, IvpFeatureKind.Point), 0f),
                objects[IvpImpactReplay.Whole32(inputs, "contact-cores", 2 * k)],
                Side(),
                objects[IvpImpactReplay.Whole32(inputs, "contact-cores", (2 * k) + 1)],
                Side(),
                0d)
            {
                Gap = IvpImpactReplay.Real32(inputs, "contact-gap", k),
                PushStreak = (short)IvpImpactReplay.Whole32(inputs, "contact-streak", k),
                Record = new IvpContactRecord
                {
                    Normal = IvpImpactReplay.Vector(inputs, "record-normal", 3 * k),
                    FirstTurn = IvpImpactReplay.Vector(inputs, "record-first-turn", 3 * k),
                    SecondTurn = IvpImpactReplay.Vector(inputs, "record-second-turn", 3 * k),
                    FirstCore = first.Immovable ? null : first,
                    SecondCore = second.Immovable ? null : second,
                    InverseMass = IvpImpactReplay.Real32(inputs, "record-inverse-mass", k),
                },
            };

            first.FrictionInfo?.Contacts.Add(point);
            second.FrictionInfo?.Contacts.Add(point);

            if (!system.Pairs.Exists(pair => Joins(pair.FirstCore, pair.SecondCore, first, second)))
            {
                system.Pairs.Add(new IvpFrictionPair(first, second));
            }

            points[place] = point;
            places[point] = place;
        }

        for (int place = listed - 1; place >= 0; place--)
        {
            system.Link(points[place]);
        }

        system.SortContacts();
        system.Solve(IvpImpactReplay.Real32(inputs, "event", 0));

        Dictionary<string, long[]> full = NewContactLanes(listed);
        int position = 0;

        for (IvpContactPoint? point = system.FirstContact; point is not null; point = point.Next)
        {
            full["order"][position++] = places[point];
        }

        for (int place = 0; place < listed; place++)
        {
            IvpContactPoint point = points[place];
            IvpContactRecord record = point.Record!;

            full["normal-push"][place] = IvpImpactReplay.Lane(point.NormalPush);
            full["streak"][place] = point.PushStreak;
            full["record-index"][place] = record.Index;
            full["record-first-share"][place] = ShareOwner(record.FirstFrictionInfo, cores);
            full["record-second-share"][place] = ShareOwner(record.SecondFrictionInfo, cores);
            full["record-streak"][place] = record.SolvePushStreak;
            full["record-gap"][place] = IvpImpactReplay.Lane(record.SolveGap);
        }

        Dictionary<string, long[]> outputs = Finish(full);
        long[] bits = new long[MostCores];

        foreach (string vector in CoreVectors)
        {
            outputs[SolvedCore + vector] = new long[3 * MostCores];
        }

        for (int k = 0; k < coreCount; k++)
        {
            bits[k] = cores[k].FlagBit0 ? 1 : 0;
            Array.Copy(IvpImpactReplay.Lanes(cores[k].Velocity), 0, outputs[SolvedCore + "velocity"], 3 * k, 3);
            Array.Copy(IvpImpactReplay.Lanes(cores[k].AngularVelocity), 0, outputs[SolvedCore + "spin"], 3 * k, 3);
            Array.Copy(IvpImpactReplay.Lanes(cores[k].PendingVelocity), 0, outputs[SolvedCore + "pending-velocity"], 3 * k, 3);
            Array.Copy(IvpImpactReplay.Lanes(cores[k].PendingAngularVelocity), 0, outputs[SolvedCore + "pending-spin"], 3 * k, 3);
        }

        outputs["core-flag-bit0"] = bits;
        outputs["counts"] = [system.ContactCount, system.LeftOut];

        return outputs;
    }

    /// <summary>Every lane where two readings of the same case differ, named — a NaN's sign and payload included.</summary>
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

    /// <summary>One zeroed lane per listed place for each per-contact output.</summary>
    internal static Dictionary<string, long[]> NewContactLanes(int listed)
    {
        Dictionary<string, long[]> lanes = new(StringComparer.Ordinal);

        foreach (string name in ContactOutputs)
        {
            lanes[name] = new long[listed];
        }

        return lanes;
    }

    /// <summary>
    /// The per-contact outputs as the fixture holds them: each cut to its first <see cref="MostContacts"/> places, and two FNV-1a
    /// digests over every place — the order, then everything else in <see cref="ContactOutputs"/>'s order.
    /// </summary>
    internal static Dictionary<string, long[]> Finish(Dictionary<string, long[]> full)
    {
        Dictionary<string, long[]> outputs = new(StringComparer.Ordinal);
        ulong order = Offset;
        ulong contacts = Offset;

        foreach (string name in ContactOutputs)
        {
            long[] lanes = full[name];
            long[] kept = new long[MostContacts];

            Array.Copy(lanes, kept, Math.Min(lanes.Length, MostContacts));
            outputs[name] = kept;

            foreach (long lane in lanes)
            {
                if (name == "order")
                {
                    order = Fold(order, lane);
                }
                else
                {
                    contacts = Fold(contacts, lane);
                }
            }
        }

        outputs["digests"] = [unchecked((long)order), unchecked((long)contacts)];

        return outputs;
    }

    private static ulong Fold(ulong digest, long lane)
    {
        unchecked
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                digest = (digest ^ (((ulong)lane >> shift) & 0xff)) * Prime;
            }

            return digest;
        }
    }

    private static bool Joins(IvpRigidBody pairFirst, IvpRigidBody pairSecond, IvpRigidBody first, IvpRigidBody second) =>
        (ReferenceEquals(pairFirst, first) && ReferenceEquals(pairSecond, second)) ||
        (ReferenceEquals(pairFirst, second) && ReferenceEquals(pairSecond, first));

    private static int ShareOwner(IvpFrictionInfo? share, IvpRigidBody[] cores) =>
        share is null ? -1 : Array.FindIndex(cores, core => ReferenceEquals(core.FrictionInfo, share));

    /// <summary>One triangle — all a contact point's constructor reads of a ledge.</summary>
    private static IvpLedgeSide Side() =>
        new(
            [(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f)],
            new IvpLedgeTopology([(0, 1, 2)], [(0, 0, 0)], [0], [0]),
            IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)),
            (0d, 0d, 0d));

    private static List<IvpReplayField> BuildInputs()
    {
        List<IvpReplayField> fields =
        [
            new("inverse-step", IvpReplayKind.Real64, 1),
            new("max-velocity", IvpReplayKind.Real32, 1),
            new("max-spin", IvpReplayKind.Real32, 1),
            new("min-friction-mass", IvpReplayKind.Real32, 1),
            new("gravity", IvpReplayKind.Real32, 1),
            new("event", IvpReplayKind.Real32, 1),
            new("freezes", IvpReplayKind.Whole32, 1),
            new("cores", IvpReplayKind.Whole32, 1),
            new("contacts", IvpReplayKind.Whole32, 1),
            new("copies", IvpReplayKind.Whole32, 1),
            new("core-flags", IvpReplayKind.Whole32, MostCores),
            new("core-mass", IvpReplayKind.Real32, MostCores),
            new("core-inertia", IvpReplayKind.Real32, 3 * MostCores),
            new("core-inverse-inertia", IvpReplayKind.Real32, 3 * MostCores),
            new("core-inverse-mass", IvpReplayKind.Real32, MostCores),
        ];

        foreach (string vector in CoreVectors)
        {
            fields.Add(new("core-" + vector, IvpReplayKind.Real32, 3 * MostCores));
        }

        fields.Add(new("contact-cores", IvpReplayKind.Whole32, 2 * MostContacts));
        fields.Add(new("contact-gap", IvpReplayKind.Real32, MostContacts));
        fields.Add(new("contact-streak", IvpReplayKind.Whole32, MostContacts));
        fields.Add(new("record-normal", IvpReplayKind.Real32, 3 * MostContacts));
        fields.Add(new("record-first-turn", IvpReplayKind.Real32, 3 * MostContacts));
        fields.Add(new("record-second-turn", IvpReplayKind.Real32, 3 * MostContacts));
        fields.Add(new("record-inverse-mass", IvpReplayKind.Real32, MostContacts));

        return fields;
    }

    private static List<IvpReplayField> BuildOutputs()
    {
        List<IvpReplayField> fields = [];

        foreach (string name in ContactOutputs)
        {
            fields.Add(new(name, name is "normal-push" or "record-gap" ? IvpReplayKind.Real32 : IvpReplayKind.Whole32, MostContacts));
        }

        fields.Add(new("digests", IvpReplayKind.Real64, 2));
        fields.Add(new("core-flag-bit0", IvpReplayKind.Whole32, MostCores));

        foreach (string vector in CoreVectors)
        {
            fields.Add(new(SolvedCore + vector, IvpReplayKind.Real32, 3 * MostCores));
        }

        fields.Add(new("counts", IvpReplayKind.Whole32, 2));

        return fields;
    }
}
