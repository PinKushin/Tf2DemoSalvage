using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// The heap solve's core-level routines as lanes of bits — a push through a record (<c>FUN_1800a9280</c>), the limits
/// (<c>FUN_180076710</c>), the staged changes flushed (<c>FUN_180077950</c>) and dropped (<c>FUN_180076670</c>), and a core's
/// kinetic energy (<c>FUN_180077e80</c>) — each run on fresh copies of the same two cores (B369, D172).
/// </summary>
/// <remarks>
/// **The form is <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-heap-core` probe from the shipped `vphysics.dll` and
/// replayed by `IvpHeapCoreConformanceTests`. A stage's output fields are named `stage-side-field`.
/// </remarks>
public static class IvpHeapCoreReplay
{
    private static readonly string[] Sides = ["first-", "second-"];

    private static readonly string[] Stages = ["pushed-", "limited-", "flushed-", "dropped-"];

    private static readonly string[] CoreVectors = ["velocity", "spin", "pending-velocity", "pending-spin"];

    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } = BuildInputs();

    /// <summary>What a case leaves.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } = BuildOutputs();

    /// <summary>Runs the port on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        IvpAnomalyLimits limits = new(
            IvpImpactReplay.Real32(inputs, "max-velocity", 0), 0, IvpImpactReplay.Real32(inputs, "max-spin", 0), 0, 0f, 0f);
        double inverseStep = IvpImpactReplay.Real64(inputs, "inverse-step", 0);
        Dictionary<string, long[]> outputs = new(StringComparer.Ordinal);
        long[] movable = inputs["movable"];

        IvpRigidBody first = Core(inputs, Sides[0]);
        IvpRigidBody second = Core(inputs, Sides[1]);
        IvpContactRecord record = new()
        {
            Normal = IvpImpactReplay.Vector(inputs, "normal", 0),
            FirstTurn = IvpImpactReplay.Vector(inputs, "turns", 0),
            SecondTurn = IvpImpactReplay.Vector(inputs, "turns", 3),
            FirstCore = movable[0] != 0 ? first : null,
            SecondCore = movable[1] != 0 ? second : null,
        };

        record.Push(IvpImpactReplay.Real64(inputs, "push", 0), limits, inverseStep);
        AddCores(outputs, Stages[0], first, second);

        first = Core(inputs, Sides[0]);
        second = Core(inputs, Sides[1]);
        IvpPush.Limit(first, limits, inverseStep);
        IvpPush.Limit(second, limits, inverseStep);
        AddCores(outputs, Stages[1], first, second);

        first = Core(inputs, Sides[0]);
        second = Core(inputs, Sides[1]);
        IvpPush.Flush(first);
        IvpPush.Flush(second);
        AddCores(outputs, Stages[2], first, second);

        first = Core(inputs, Sides[0]);
        second = Core(inputs, Sides[1]);
        IvpPush.Drop(first);
        IvpPush.Drop(second);
        AddCores(outputs, Stages[3], first, second);

        (float X, float Y, float Z) velocity = IvpImpactReplay.Vector(inputs, "energy-velocity", 0);
        (float X, float Y, float Z) spin = IvpImpactReplay.Vector(inputs, "energy-spin", 0);

        first = Core(inputs, Sides[0]);
        second = Core(inputs, Sides[1]);
        outputs["energy"] =
            [IvpImpactReplay.Lane(first.KineticEnergy(velocity, spin)), IvpImpactReplay.Lane(second.KineticEnergy(velocity, spin))];

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

    /// <summary>The names of one core's vector fields, in the order the probe writes and reads them.</summary>
    internal static IReadOnlyList<string> VectorNames => CoreVectors;

    /// <summary>The stage prefixes, in the order the routines run.</summary>
    internal static IReadOnlyList<string> StageNames => Stages;

    /// <summary>The side prefixes, first then second.</summary>
    internal static IReadOnlyList<string> SideNames => Sides;

    private static IvpRigidBody Core(IReadOnlyDictionary<string, long[]> inputs, string side) =>
        new()
        {
            Mass = IvpImpactReplay.Real32(inputs, side + "mass", 0),
            Inertia = IvpImpactReplay.Vector(inputs, side + "inertia", 0),
            InverseInertia = IvpImpactReplay.Vector(inputs, side + "inverse-inertia", 0),
            InverseMass = IvpImpactReplay.Real32(inputs, side + "inverse-mass", 0),
            Velocity = IvpImpactReplay.Vector(inputs, side + "velocity", 0),
            AngularVelocity = IvpImpactReplay.Vector(inputs, side + "spin", 0),
            PendingVelocity = IvpImpactReplay.Vector(inputs, side + "pending-velocity", 0),
            PendingAngularVelocity = IvpImpactReplay.Vector(inputs, side + "pending-spin", 0),
        };

    private static void AddCores(Dictionary<string, long[]> outputs, string stage, IvpRigidBody first, IvpRigidBody second)
    {
        foreach ((string side, IvpRigidBody core) in new[] { (Sides[0], first), (Sides[1], second) })
        {
            outputs[stage + side + "velocity"] = IvpImpactReplay.Lanes(core.Velocity);
            outputs[stage + side + "spin"] = IvpImpactReplay.Lanes(core.AngularVelocity);
            outputs[stage + side + "pending-velocity"] = IvpImpactReplay.Lanes(core.PendingVelocity);
            outputs[stage + side + "pending-spin"] = IvpImpactReplay.Lanes(core.PendingAngularVelocity);
        }
    }

    private static List<IvpReplayField> BuildInputs()
    {
        List<IvpReplayField> fields =
        [
            new("inverse-step", IvpReplayKind.Real64, 1),
            new("max-velocity", IvpReplayKind.Real32, 1),
            new("max-spin", IvpReplayKind.Real32, 1),
            new("push", IvpReplayKind.Real64, 1),
            new("normal", IvpReplayKind.Real32, 3),
            new("turns", IvpReplayKind.Real32, 6),
            new("movable", IvpReplayKind.Whole32, 2),
            new("energy-velocity", IvpReplayKind.Real32, 3),
            new("energy-spin", IvpReplayKind.Real32, 3),
        ];

        foreach (string side in Sides)
        {
            fields.Add(new(side + "mass", IvpReplayKind.Real32, 1));
            fields.Add(new(side + "inertia", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "inverse-inertia", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "inverse-mass", IvpReplayKind.Real32, 1));

            foreach (string vector in CoreVectors)
            {
                fields.Add(new(side + vector, IvpReplayKind.Real32, 3));
            }
        }

        return fields;
    }

    private static List<IvpReplayField> BuildOutputs()
    {
        List<IvpReplayField> fields = [];

        foreach (string stage in Stages)
        {
            foreach (string side in Sides)
            {
                foreach (string vector in CoreVectors)
                {
                    fields.Add(new(stage + side + vector, IvpReplayKind.Real32, 3));
                }
            }
        }

        fields.Add(new("energy", IvpReplayKind.Real64, 2));

        return fields;
    }
}
