using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Oracle;

/// <summary>How a replay lane's bits are read.</summary>
public enum IvpReplayKind
{
    /// <summary>A float's 32 bits.</summary>
    Real32,

    /// <summary>A double's 64 bits.</summary>
    Real64,

    /// <summary>A signed 32-bit integer.</summary>
    Whole32,
}

/// <summary>One named group of lanes in a replay case.</summary>
/// <param name="Name">The group's name, as the fixture spells it.</param>
/// <param name="Kind">How each lane is read.</param>
/// <param name="Count">How many lanes the group holds.</param>
public sealed record IvpReplayField(string Name, IvpReplayKind Kind, int Count);

/// <summary>One call the binary was given and what it left, as lanes of bits.</summary>
/// <param name="Label">The case's name.</param>
/// <param name="Inputs">The inputs, by field name.</param>
/// <param name="Outputs">What the binary left, by field name.</param>
public sealed record IvpReplayCase(
    string Label, IReadOnlyDictionary<string, long[]> Inputs, IReadOnlyDictionary<string, long[]> Outputs);

/// <summary>
/// IVP's impact solver as lanes of bits — the format the <c>vphysics-impact</c> probe writes from the shipped
/// <c>vphysics.dll</c> and <c>IvpImpactSolverConformanceTests</c> replays against <see cref="IvpImpactSolver"/> (B369).
/// </summary>
/// <remarks>
/// **One file, compiled into the probe and linked into the test project**, so the probe that asks the binary and the test that
/// replays its answer cannot disagree about what a lane means. The probe writes the inputs into fabricated structs at the
/// offsets `docs/findings/51` reads; <see cref="Run"/> builds the same inputs into this project's objects and reads back the
/// same fields.
/// </remarks>
public static class IvpImpactReplay
{
    /// <summary>The prefixes of the two cores' fields, first then second.</summary>
    private static readonly string[] Sides = ["first-", "second-"];

    /// <summary>What the solver is given: its arguments, the environment, the solver's inputs and both cores.</summary>
    public static IReadOnlyList<IvpReplayField> Inputs { get; } = BuildInputs();

    /// <summary>What the solver writes: its working state, both cores and the environment's counters.</summary>
    public static IReadOnlyList<IvpReplayField> Outputs { get; } = BuildOutputs();

    /// <summary>Runs <see cref="IvpImpactSolver"/> on a case's inputs and reads back every output field.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <returns>The outputs, by field name.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputs"/> is null.</exception>
    public static IReadOnlyDictionary<string, long[]> Run(IReadOnlyDictionary<string, long[]> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        IvpRigidBody first = Core(inputs, Sides[0]);
        IvpRigidBody second = Core(inputs, Sides[1]);

        IvpImpactEnvironment environment = new()
        {
            InverseStep = Real64(inputs, "inverse-step", 0),
            Limits = new IvpAnomalyLimits(
                Real32(inputs, "max-velocity", 0), Whole32(inputs, "max-collisions", 0), Real32(inputs, "max-spin", 0), 0, 0f, 0f),
            Anomalies = new VphysicsAnomalyManager(new FixedAnswer(Whole32(inputs, "freezes", 0) != 0)),
        };

        IvpImpactSolver solver = new()
        {
            First = first,
            Second = second,
            FirstArm = Vector(inputs, "first-arm", 0),
            SecondArm = Vector(inputs, "second-arm", 0),
            Normal = Vector(inputs, "normal", 0),
            Elasticity = Real32(inputs, "elasticity", 0),
            ConeCosine = Real32(inputs, "cone", 0),
            ConeTangent = Real32(inputs, "cone", 1),
            UsesAxis = Whole32(inputs, "uses-axis", 0) != 0,
            AxisTangent = Real32(inputs, "axis-tangent", 0),
            Axis = Vector(inputs, "axis", 0),
        };

        IvpRigidBody?[] cores = new IvpRigidBody?[2];

        solver.Solve(
            environment, cores, Whole32(inputs, "p3", 0) != 0, Whole32(inputs, "p4", 0), Real32(inputs, "p5", 0));

        Dictionary<string, long[]> outputs = new(StringComparer.Ordinal)
        {
            ["separation"] = [Lane(solver.SeparationSpeed)],
            ["virtual-mass"] = [Lane(solver.FirstVirtualMass), Lane(solver.SecondVirtualMass)],
            ["may-hold-back"] = [solver.MayHoldBack ? 1 : 0],
            ["working-velocity"] = Lanes(solver.FirstVelocity, solver.SecondVelocity),
            ["working-spin"] = Lanes(solver.FirstSpin, solver.SecondSpin),
            ["velocity-change"] = Lanes(solver.FirstVelocityChange, solver.SecondVelocityChange),
            ["spin-change"] = Lanes(solver.FirstSpinChange, solver.SecondSpinChange),
            ["relative"] = Lanes(solver.Relative),
            ["push"] = Lanes(solver.Push),
            ["fallback"] = Lanes(solver.Fallback),
            ["record-relative"] = Lanes(solver.RecordRelative),
            ["counters"] = [environment.Impacts, environment.HeldBack, environment.Frozen],
            ["cores"] = [Slot(cores[0], first, second), Slot(cores[1], first, second)],
        };

        AddCore(outputs, Sides[0], first);
        AddCore(outputs, Sides[1], second);

        return outputs;
    }

    /// <summary>Every lane where two readings of the same case differ, named.</summary>
    /// <param name="expected">What the binary left.</param>
    /// <param name="actual">What the port left.</param>
    /// <returns>One line per differing lane; empty when the two agree.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IReadOnlyList<string> Differences(
        IReadOnlyDictionary<string, long[]> expected, IReadOnlyDictionary<string, long[]> actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        List<string> differences = [];

        foreach (IvpReplayField field in Outputs)
        {
            long[] engine = expected[field.Name];
            long[] port = actual[field.Name];

            for (int lane = 0; lane < field.Count; lane++)
            {
                if (engine[lane] != port[lane])
                {
                    differences.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{field.Name}[{lane}]: binary {Text(field.Kind, engine[lane])}, port {Text(field.Kind, port[lane])}"));
                }
            }
        }

        return differences;
    }

    /// <summary>Writes one case in the fixture's form.</summary>
    /// <param name="writer">Where to write.</param>
    /// <param name="replay">The case.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static void Write(TextWriter writer, IvpReplayCase replay)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(replay);

        writer.WriteLine($"case {replay.Label}");

        foreach (IvpReplayField field in Inputs)
        {
            WriteField(writer, field, replay.Inputs[field.Name]);
        }

        foreach (IvpReplayField field in Outputs)
        {
            WriteField(writer, field, replay.Outputs[field.Name]);
        }
    }

    /// <summary>Reads every case a fixture holds.</summary>
    /// <param name="reader">The fixture.</param>
    /// <returns>The cases, in file order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
    /// <exception cref="InvalidDataException">A line names no known field, holds the wrong number of lanes, or a case is incomplete.</exception>
    /// <remarks>Blank lines and lines starting with <c>#</c> are ignored.</remarks>
    public static IReadOnlyList<IvpReplayCase> Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        Dictionary<string, IvpReplayField> known = Inputs.Concat(Outputs).ToDictionary(field => field.Name, StringComparer.Ordinal);
        List<IvpReplayCase> cases = [];
        string? label = null;
        Dictionary<string, long[]> lanes = new(StringComparer.Ordinal);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (tokens[0] == "case")
            {
                Finish(cases, label, lanes);
                label = tokens[1];
                lanes = new Dictionary<string, long[]>(StringComparer.Ordinal);
                continue;
            }

            if (!known.TryGetValue(tokens[0], out IvpReplayField? named) || tokens.Length != named.Count + 1)
            {
                throw new InvalidDataException($"The replay line '{line}' names no known field or holds the wrong number of lanes.");
            }

            long[] values = new long[named.Count];

            for (int lane = 0; lane < named.Count; lane++)
            {
                values[lane] = Parse(named.Kind, tokens[lane + 1]);
            }

            lanes.Add(named.Name, values);
        }

        Finish(cases, label, lanes);

        return cases;
    }

    /// <summary>A float's bits as a lane.</summary>
    /// <param name="value">The float.</param>
    /// <returns>Its 32 bits, unsigned.</returns>
    public static long Lane(float value) => (uint)BitConverter.SingleToInt32Bits(value);

    /// <summary>A double's bits as a lane.</summary>
    /// <param name="value">The double.</param>
    /// <returns>Its 64 bits.</returns>
    public static long Lane(double value) => BitConverter.DoubleToInt64Bits(value);

    /// <summary>The flags word a core's fields make, as the binary's <c>core+0x0</c> holds it.</summary>
    /// <param name="core">The core.</param>
    /// <returns>Bit <c>0x2</c>, <c>0x10</c>, <c>0x20</c> and bits 6–7.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="core"/> is null.</exception>
    public static int Flags(IvpRigidBody core)
    {
        ArgumentNullException.ThrowIfNull(core);

        return (core.Immovable ? 0x2 : 0) | (core.SkipsGravity ? 0x10 : 0) | (core.UsesAlternateGravity ? 0x20 : 0) |
               ((core.CollisionFreeze & 3) << 6);
    }

    /// <summary>Builds one side's core from a case's inputs.</summary>
    /// <param name="inputs">The inputs, by field name.</param>
    /// <param name="side"><c>"first-"</c> or <c>"second-"</c>.</param>
    /// <returns>The core, its flags word split into this project's fields.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static IvpRigidBody Core(IReadOnlyDictionary<string, long[]> inputs, string side)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(side);

        int flags = Whole32(inputs, side + "flags", 0);
        long[] matrix = inputs[side + "matrix"];

        return new IvpRigidBody
        {
            Immovable = (flags & 0x2) != 0,
            SkipsGravity = (flags & 0x10) != 0,
            UsesAlternateGravity = (flags & 0x20) != 0,
            CollisionFreeze = (flags >> 6) & 3,
            Collisions = (short)Whole32(inputs, side + "collisions", 0),
            Offset08 = Real32(inputs, side + "offset08", 0),
            HasOffset58 = Whole32(inputs, side + "offset58", 0) != 0,
            InverseInertia = Vector(inputs, side + "inverse-inertia", 0),
            InverseMass = Real32(inputs, side + "inverse-mass", 0),
            Velocity = Vector(inputs, side + "velocity", 0),
            AngularVelocity = Vector(inputs, side + "spin", 0),
            PendingVelocity = Vector(inputs, side + "pending-velocity", 0),
            PendingAngularVelocity = Vector(inputs, side + "pending-spin", 0),
            CoreMatrix = new IvpMatrix(
                BitConverter.Int64BitsToDouble(matrix[0]), BitConverter.Int64BitsToDouble(matrix[1]), BitConverter.Int64BitsToDouble(matrix[2]),
                BitConverter.Int64BitsToDouble(matrix[3]), BitConverter.Int64BitsToDouble(matrix[4]), BitConverter.Int64BitsToDouble(matrix[5]),
                BitConverter.Int64BitsToDouble(matrix[6]), BitConverter.Int64BitsToDouble(matrix[7]), BitConverter.Int64BitsToDouble(matrix[8]),
                (0d, 0d, 0d)),
        };
    }

    private static List<IvpReplayField> BuildInputs()
    {
        List<IvpReplayField> fields =
        [
            new("p3", IvpReplayKind.Whole32, 1),
            new("p4", IvpReplayKind.Whole32, 1),
            new("p5", IvpReplayKind.Real32, 1),
            new("max-velocity", IvpReplayKind.Real32, 1),
            new("max-collisions", IvpReplayKind.Whole32, 1),
            new("max-spin", IvpReplayKind.Real32, 1),
            new("inverse-step", IvpReplayKind.Real64, 1),
            new("freezes", IvpReplayKind.Whole32, 1),
            new("elasticity", IvpReplayKind.Real32, 1),
            new("cone", IvpReplayKind.Real32, 2),
            new("uses-axis", IvpReplayKind.Whole32, 1),
            new("axis-tangent", IvpReplayKind.Real32, 1),
            new("axis", IvpReplayKind.Real32, 3),
            new("normal", IvpReplayKind.Real32, 3),
            new("first-arm", IvpReplayKind.Real32, 3),
            new("second-arm", IvpReplayKind.Real32, 3),
        ];

        foreach (string side in Sides)
        {
            fields.Add(new(side + "flags", IvpReplayKind.Whole32, 1));
            fields.Add(new(side + "collisions", IvpReplayKind.Whole32, 1));
            fields.Add(new(side + "offset08", IvpReplayKind.Real32, 1));
            fields.Add(new(side + "offset58", IvpReplayKind.Whole32, 1));
            fields.Add(new(side + "inverse-inertia", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "inverse-mass", IvpReplayKind.Real32, 1));
            fields.Add(new(side + "velocity", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "spin", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "pending-velocity", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "pending-spin", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "matrix", IvpReplayKind.Real64, 9));
        }

        return fields;
    }

    private static List<IvpReplayField> BuildOutputs()
    {
        List<IvpReplayField> fields =
        [
            new("separation", IvpReplayKind.Real32, 1),
            new("virtual-mass", IvpReplayKind.Real64, 2),
            new("may-hold-back", IvpReplayKind.Whole32, 1),
            new("working-velocity", IvpReplayKind.Real32, 6),
            new("working-spin", IvpReplayKind.Real32, 6),
            new("velocity-change", IvpReplayKind.Real32, 6),
            new("spin-change", IvpReplayKind.Real32, 6),
            new("relative", IvpReplayKind.Real32, 3),
            new("push", IvpReplayKind.Real32, 3),
            new("fallback", IvpReplayKind.Real32, 3),
            new("record-relative", IvpReplayKind.Real32, 3),
            new("counters", IvpReplayKind.Whole32, 3),
            new("cores", IvpReplayKind.Whole32, 2),
        ];

        foreach (string side in Sides)
        {
            fields.Add(new(side + "flags-after", IvpReplayKind.Whole32, 1));
            fields.Add(new(side + "collisions-after", IvpReplayKind.Whole32, 1));
            fields.Add(new(side + "velocity-after", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "spin-after", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "pending-velocity-after", IvpReplayKind.Real32, 3));
            fields.Add(new(side + "pending-spin-after", IvpReplayKind.Real32, 3));
        }

        return fields;
    }

    private static void AddCore(Dictionary<string, long[]> outputs, string side, IvpRigidBody core)
    {
        outputs[side + "flags-after"] = [Flags(core)];
        outputs[side + "collisions-after"] = [core.Collisions];
        outputs[side + "velocity-after"] = Lanes(core.Velocity);
        outputs[side + "spin-after"] = Lanes(core.AngularVelocity);
        outputs[side + "pending-velocity-after"] = Lanes(core.PendingVelocity);
        outputs[side + "pending-spin-after"] = Lanes(core.PendingAngularVelocity);
    }

    /// <summary>Which core a slot holds: zero for none, one for the first, two for the second.</summary>
    private static int Slot(IvpRigidBody? core, IvpRigidBody first, IvpRigidBody second)
    {
        if (core is null)
        {
            return 0;
        }

        if (ReferenceEquals(core, first))
        {
            return 1;
        }

        return ReferenceEquals(core, second) ? 2 : 9;
    }

    private static long[] Lanes((float X, float Y, float Z) vector) => [Lane(vector.X), Lane(vector.Y), Lane(vector.Z)];

    private static long[] Lanes((float X, float Y, float Z) first, (float X, float Y, float Z) second) =>
        [Lane(first.X), Lane(first.Y), Lane(first.Z), Lane(second.X), Lane(second.Y), Lane(second.Z)];

    private static float Real32(IReadOnlyDictionary<string, long[]> inputs, string name, int lane) =>
        BitConverter.Int32BitsToSingle(unchecked((int)(uint)inputs[name][lane]));

    private static double Real64(IReadOnlyDictionary<string, long[]> inputs, string name, int lane) =>
        BitConverter.Int64BitsToDouble(inputs[name][lane]);

    private static int Whole32(IReadOnlyDictionary<string, long[]> inputs, string name, int lane) =>
        unchecked((int)inputs[name][lane]);

    private static (float X, float Y, float Z) Vector(IReadOnlyDictionary<string, long[]> inputs, string name, int lane) =>
        (Real32(inputs, name, lane), Real32(inputs, name, lane + 1), Real32(inputs, name, lane + 2));

    private static void WriteField(TextWriter writer, IvpReplayField field, long[] values)
    {
        StringBuilder line = new(field.Name);

        foreach (long value in values)
        {
            line.Append(' ').Append(Hex(field.Kind, value));
        }

        writer.WriteLine(line.ToString());
    }

    private static string Hex(IvpReplayKind kind, long value) =>
        kind == IvpReplayKind.Real64
            ? value.ToString("x16", CultureInfo.InvariantCulture)
            : unchecked((uint)value).ToString("x8", CultureInfo.InvariantCulture);

    private static long Parse(IvpReplayKind kind, string token) =>
        kind switch
        {
            IvpReplayKind.Real64 => long.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            IvpReplayKind.Real32 => uint.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            _ => unchecked((int)uint.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
        };

    private static string Text(IvpReplayKind kind, long value) =>
        kind switch
        {
            IvpReplayKind.Real32 => string.Create(
                CultureInfo.InvariantCulture, $"0x{Hex(kind, value)} ({BitConverter.Int32BitsToSingle(unchecked((int)(uint)value)):R})"),
            IvpReplayKind.Real64 => string.Create(
                CultureInfo.InvariantCulture, $"0x{Hex(kind, value)} ({BitConverter.Int64BitsToDouble(value):R})"),
            _ => value.ToString(CultureInfo.InvariantCulture),
        };

    private static void Finish(List<IvpReplayCase> cases, string? label, Dictionary<string, long[]> lanes)
    {
        if (label is null)
        {
            return;
        }

        IvpReplayField? missing = Inputs.Concat(Outputs).FirstOrDefault(field => !lanes.ContainsKey(field.Name));

        if (missing is not null)
        {
            throw new InvalidDataException($"The replay case '{label}' has no '{missing.Name}'.");
        }

        cases.Add(new IvpReplayCase(label, lanes, lanes));
    }

    /// <summary>A game solver that gives one answer, as the replay's <c>freezes</c> lane says.</summary>
    private sealed class FixedAnswer(bool answer) : IPhysicsCollisionSolver
    {
        public bool ShouldFreezeObject(IvpRigidBody body) => answer;
    }
}
