using System;
using System.Collections.Generic;
using System.IO;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>
/// IVP's collision entry as lanes of bits — a contact point's materials (<c>FUN_1800908d0</c>), its record's estimate
/// (<c>FUN_18008db40</c>), the push-out estimate (<c>FUN_18008fca0</c>) and the impact solver's entry (<c>FUN_18008ed60</c>, with
/// <c>FUN_18008fe70</c>), run in that order on one case — with <c>FUN_18008fe70</c> also called alone on a solver buffer, whose
/// cone tangent the entry's solve can round away (B369).
/// </summary>
/// <remarks>
/// **The form and the core lanes are <see cref="IvpImpactReplay"/>'s**, written by the `vphysics-impact` probe's `entry` mode from
/// the shipped `vphysics.dll` and replayed by `IvpImpactEntryConformanceTests`. The materials and the manager give fixed answers
/// on both sides — three materials, each object's own and the one the manager returns for any index, and the manager's friction
/// and elasticity — because what vphysics' implementations answer is not read yet; what a case pins is where each answer goes
/// and the arithmetic done on it.
/// </remarks>
public static class IvpEntryReplay
{
    private static readonly string[] Sides = ["first-", "second-"];

    private static readonly IvpLedgeEdge Edge = new(0, 0);

    /// <summary>What a case is given.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } = BuildInputs();

    /// <summary>What a case leaves.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } = BuildOutputs();

    /// <summary>Runs the four routines on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        IvpRigidBody first = IvpImpactReplay.Core(inputs, Sides[0]);
        IvpRigidBody second = IvpImpactReplay.Core(inputs, Sides[1]);

        first.Radius = IvpImpactReplay.Real32(inputs, Sides[0] + "radius", 0);
        second.Radius = IvpImpactReplay.Real32(inputs, Sides[1] + "radius", 0);

        IvpReplayMaterial[] materials = [Material(inputs, 0), Material(inputs, 1), Material(inputs, 2)];
        IvpReplayMaterials manager = new(
            materials[2], IvpImpactReplay.Real64(inputs, "manager", 0), IvpImpactReplay.Real64(inputs, "manager", 1));
        IvpImpactEnvironment environment = IvpImpactReplay.Environment(inputs, IvpImpactReplay.Real64(inputs, "step", 0), manager);

        IvpCollisionObject firstObject = Object(inputs, Sides[0], first, materials[0]);
        IvpCollisionObject secondObject = Object(inputs, Sides[1], second, materials[1]);
        long[] kinds = inputs["kinds"];
        long[] indices = inputs["material-indices"];
        long[] movable = inputs["movable"];

        IvpContactPoint point = new(
            new IvpMindist(new IvpSynapse(Edge, (IvpFeatureKind)kinds[0]), new IvpSynapse(Edge, (IvpFeatureKind)kinds[1]), 0f),
            firstObject,
            Side((int)indices[0]),
            secondObject,
            Side((int)indices[1]),
            0d)
        {
            Gap = IvpImpactReplay.Real32(inputs, "gap", 0),
            UsesMaterialAxes = IvpImpactReplay.Whole32(inputs, "uses-material-axes", 0) != 0,
        };

        IvpContactRecord record = new()
        {
            Normal = IvpImpactReplay.Vector(inputs, "normal", 0),
            FirstArm = IvpImpactReplay.Vector(inputs, "first-arm", 0),
            SecondArm = IvpImpactReplay.Vector(inputs, "second-arm", 0),
            FirstTurn = IvpImpactReplay.Vector(inputs, "turns", 0),
            SecondTurn = IvpImpactReplay.Vector(inputs, "turns", 3),
            FirstCore = movable[0] != 0 ? first : null,
            SecondCore = movable[1] != 0 ? second : null,
            Impacts = (short)IvpImpactReplay.Whole32(inputs, "impacts", 0),
        };

        point.Record = record;
        point.SetMaterials(manager);
        point.Estimate(environment);

        long estimated = record.Estimated ? 1 : 0;
        long[] estimate = [IvpImpactReplay.Lane(record.PushOut), IvpImpactReplay.Lane(record.PredictedGap)];
        float pushOut = point.PushOut(environment);
        long[] pushed = [IvpImpactReplay.Lane(pushOut), IvpImpactReplay.Lane(record.PushOut)];
        (bool Uses, float Tangent, (float X, float Y, float Z) Axis) axes = IvpImpactSolver.MaterialAxes(environment, point, record);
        IvpRigidBody?[] cores = new IvpRigidBody?[2];

        IvpImpactSolver.Enter(environment, point, cores, pushOut);

        Dictionary<string, long[]> outputs = new(StringComparer.Ordinal)
        {
            ["objects"] = [Which(record.FirstObject, firstObject, secondObject), Which(record.SecondObject, firstObject, secondObject)],
            ["materials"] = [Array.IndexOf(materials, record.FirstMaterial) + 1, Array.IndexOf(materials, record.SecondMaterial) + 1],
            ["elasticity"] = [IvpImpactReplay.Lane(record.Elasticity)],
            ["friction"] = [IvpImpactReplay.Lane(point.Friction)],
            ["estimated"] = [estimated],
            ["estimate"] = estimate,
            ["push-out"] = pushed,
            ["axis-uses"] = [axes.Uses ? 1 : 0],
            ["axis-cone"] = [IvpImpactReplay.Lane(axes.Tangent), .. IvpImpactReplay.Lanes(axes.Axis)],
            ["record-relative"] = IvpImpactReplay.Lanes(record.RelativeVelocity),
            ["counters"] = [environment.Impacts, environment.HeldBack, environment.Frozen],
            ["cores"] = [IvpImpactReplay.Slot(cores[0], first, second), IvpImpactReplay.Slot(cores[1], first, second)],
        };

        IvpImpactReplay.AddCore(outputs, Sides[0], first);
        IvpImpactReplay.AddCore(outputs, Sides[1], second);

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

    private static IvpReplayMaterial Material(IReadOnlyDictionary<string, long[]> inputs, int index) =>
        new(
            IvpImpactReplay.Real64(inputs, "material-friction", index),
            IvpImpactReplay.Real64(inputs, "material-second-friction", index),
            IvpImpactReplay.Whole32(inputs, "material-has-second", index) != 0);

    private static IvpCollisionObject Object(
        IReadOnlyDictionary<string, long[]> inputs, string side, IvpRigidBody core, IvpReplayMaterial material) =>
        new()
        {
            Material = material,
            Core = core,
            FrictionCore = new IvpRigidBody { CoreMatrix = IvpImpactReplay.Matrix(inputs, side + "frame") },
        };

    /// <summary>One triangle whose header carries a material index — all a synapse's material lookup reads of its ledge.</summary>
    private static IvpLedgeSide Side(int materialIndex) =>
        new(
            [(0f, 0f, 0f), (1f, 0f, 0f), (0f, 1f, 0f)],
            new IvpLedgeTopology([(0, 1, 2)], [(0, 0, 0)], [0], [materialIndex]),
            IvpMatrix.FromRotation((0f, 0f, 0f, 1f), (0d, 0d, 0d)),
            (0d, 0d, 0d));

    private static int Which(IvpCollisionObject? candidate, IvpCollisionObject first, IvpCollisionObject second)
    {
        if (ReferenceEquals(candidate, first))
        {
            return 1;
        }

        return ReferenceEquals(candidate, second) ? 2 : 0;
    }

    private static List<IvpReplayField> BuildInputs()
    {
        List<IvpReplayField> fields =
        [
            new("max-velocity", IvpReplayKind.Real32, 1),
            new("max-collisions", IvpReplayKind.Whole32, 1),
            new("max-spin", IvpReplayKind.Real32, 1),
            new("inverse-step", IvpReplayKind.Real64, 1),
            new("step", IvpReplayKind.Real64, 1),
            new("freezes", IvpReplayKind.Whole32, 1),
            new("gap", IvpReplayKind.Real32, 1),
            new("kinds", IvpReplayKind.Whole32, 2),
            new("uses-material-axes", IvpReplayKind.Whole32, 1),
            new("material-indices", IvpReplayKind.Whole32, 2),
            new("normal", IvpReplayKind.Real32, 3),
            new("first-arm", IvpReplayKind.Real32, 3),
            new("second-arm", IvpReplayKind.Real32, 3),
            new("turns", IvpReplayKind.Real32, 6),
            new("movable", IvpReplayKind.Whole32, 2),
            new("impacts", IvpReplayKind.Whole32, 1),
            new("material-friction", IvpReplayKind.Real64, 3),
            new("material-second-friction", IvpReplayKind.Real64, 3),
            new("material-has-second", IvpReplayKind.Whole32, 3),
            new("manager", IvpReplayKind.Real64, 2),
        ];

        foreach (string side in Sides)
        {
            IvpImpactReplay.AddCoreInputs(fields, side);
            fields.Add(new(side + "radius", IvpReplayKind.Real32, 1));
            fields.Add(new(side + "frame", IvpReplayKind.Real64, 9));
        }

        return fields;
    }

    private static List<IvpReplayField> BuildOutputs()
    {
        List<IvpReplayField> fields =
        [
            new("objects", IvpReplayKind.Whole32, 2),
            new("materials", IvpReplayKind.Whole32, 2),
            new("elasticity", IvpReplayKind.Real32, 1),
            new("friction", IvpReplayKind.Real32, 1),
            new("estimated", IvpReplayKind.Whole32, 1),
            new("estimate", IvpReplayKind.Real32, 2),
            new("push-out", IvpReplayKind.Real32, 2),
            new("axis-uses", IvpReplayKind.Whole32, 1),
            new("axis-cone", IvpReplayKind.Real32, 4),
            new("record-relative", IvpReplayKind.Real32, 3),
            new("counters", IvpReplayKind.Whole32, 3),
            new("cores", IvpReplayKind.Whole32, 2),
        ];

        foreach (string side in Sides)
        {
            IvpImpactReplay.AddCoreOutputs(fields, side);
        }

        return fields;
    }
}

/// <summary>A material whose slots give fixed answers, as the probe's fabricated material does.</summary>
/// <param name="FrictionFactor">Slot 1.</param>
/// <param name="SecondFrictionFactor">Slot 2.</param>
/// <param name="HasSecondFriction">The dword at <c>+0xc</c>.</param>
internal sealed record IvpReplayMaterial(double FrictionFactor, double SecondFrictionFactor, bool HasSecondFriction) : IIvpMaterial
{
    /// <summary>Slot 3, which the entry never calls: its manager answers the pair's elasticity itself.</summary>
    public double Elasticity => 0d;
}

/// <summary>A material manager whose slots give fixed answers: one material for any index, one friction, one elasticity.</summary>
internal sealed class IvpReplayMaterials(IIvpMaterial indexed, double friction, double elasticity) : IIvpMaterialManager
{
    public IIvpMaterial MaterialAt(IvpCollisionObject collisionObject, int index) => indexed;

    public double FrictionFactor(IvpContactRecord record) => friction;

    public double Elasticity(IvpContactRecord record) => elasticity;
}
