using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s rotation routines — <c>FUN_180070d60</c>, <c>FUN_180070c60</c>, <c>FUN_180071060</c>,
/// <c>FUN_180071680</c>, <c>FUN_180070f50</c> and <c>FUN_180099fc0</c> — called in process, the oracle for
/// <c>IvpQuaternion</c> and <c>IvpIntegrator.Rotate</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary is the instrument.** Quaternions go in as four doubles, a spin as three floats, and the rotation step is given
/// a core, an environment holding the phase at `+0x1ac`, and the object chain `core+0x58 → +0x8 → +0x48` that names its axis.
/// Each case sets the runtime path flag `DAT_180136418` the case names, so `sin` answers on that path.
///
/// **The control first**: a quarter turn about Z times itself must be a half turn, and `(0, 0, 0, 1.5)` must normalize to a
/// unit, by the binary and by the port alike.
///
/// **Two inputs never return, in the binary or a faithful port**: `FUN_180070c60` on a squared length of four or more, or on
/// an infinity, or zero. The draws keep the normalized quaternion's squared length between 0.09 and 3.3 and seed it only with
/// NaNs, which it leaves alone.
///
/// **Modes.** With no mode, or `sweep n`, random cases — a quarter of them seeded with NaNs and infinities — are compared lane by
/// lane; `fixture path` writes the cases `IvpRotationConformanceTests` reads.
/// </remarks>
public sealed class VphysicsRotationProbe : IProbe
{
    private const long ProductAddress = 0x180070d60;
    private const long NormaliseAddress = 0x180070c60;
    private const long InterpolateAddress = 0x180071060;
    private const long DeltaAddress = 0x180071680;
    private const long SineDeltaAddress = 0x180070f50;
    private const long RotateAddress = 0x180099fc0;
    private const long FusedFlagAddress = 0x180136418;

    private const int DefaultSweep = 50_000;
    private const int FixtureCases = 400;
    private const ulong FixtureSeed = 70060;
    private const ulong SweepSeed = 20260915;

    private static readonly long[] FloatPoisons = [0x7fc00000, 0xffc00000, 0x7fc00123, 0x7f800001, 0x7f800000, 0xff800000];

    private static readonly long[] DoublePoisons =
    [
        0x7ff8000000000000, unchecked((long)0xfff8000000000000UL), 0x7ff8000000000123, 0x7ff0000000000001,
        0x7ff0000000000000, unchecked((long)0xfff0000000000000UL),
    ];

    private static readonly long[] QuietDoubles = [0x7ff8000000000000, unchecked((long)0xfff8000000000000UL), 0x7ff0000000000001];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ProductFunction(nint output, nint first, nint second);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NormaliseFunction(nint rotation);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void InterpolateFunction(nint output, nint first, nint second, double fraction);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DeltaFunction(nint output, nint spin, double delta);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RotateFunction(nint core, float delta, nint output);

    /// <inheritdoc />
    public string Name => "vphysics-rotation";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's rotation routines (the product FUN_180070d60, the normalization FUN_180070c60, the interpolation " +
        "FUN_180071060, the step rotations FUN_180071680 and FUN_180070f50, a core's rotation step FUN_180099fc0) called in " +
        "process on both sin paths and compared with the port; 'fixture' writes the conformance suite's cases: " +
        "vphysics-rotation [sweep n | fixture path]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        using Native native = new(module);
        nint flag = VphysicsLibrary.Address(module, FusedFlagAddress);
        int loaded = Marshal.ReadInt32(flag);
        bool fusedAvailable = loaded != 0;

        output.WriteLine($"The fused-path flag reads {loaded}; cases draw {(fusedAvailable ? "both paths" : "the plain path only")}");

        try
        {
            if (!Control(output, native))
            {
                return;
            }

            if (arguments.Count >= 2 && arguments[0] == "fixture")
            {
                Fixture(output, native, arguments[1], fusedAvailable);
                return;
            }

            Sweep(output, native, fusedAvailable, arguments.Count >= 2 && arguments[0] == "sweep"
                ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
                : DefaultSweep);
        }
        finally
        {
            Marshal.WriteInt32(flag, loaded);
        }
    }

    private static bool Control(TextWriter output, Native native)
    {
        ulong state = SweepSeed;
        Dictionary<string, long[]> inputs = RandomCase(ref state, poison: false, fusedAvailable: false);
        double half = Math.Sqrt(0.5d);

        inputs["first"] = [0L, 0L, IvpImpactReplay.Lane(half), IvpImpactReplay.Lane(half)];
        inputs["second"] = [0L, 0L, IvpImpactReplay.Lane(half), IvpImpactReplay.Lane(half)];

        Dictionary<string, long[]> binary = native.Run(inputs);
        double z = BitConverter.Int64BitsToDouble(binary["product"][2]);
        double w = BitConverter.Int64BitsToDouble(binary["product"][3]);
        bool halfTurn = Math.Abs(z - 1d) < 1e-12 && Math.Abs(w) < 1e-12;

        IReadOnlyList<string> differences = IvpRotationReplay.Differences(binary, IvpRotationReplay.Run(inputs));
        Dictionary<string, long[]> offUnit = new(inputs, StringComparer.Ordinal);

        offUnit["first"] = [0L, 0L, 0L, IvpImpactReplay.Lane(1.5d)];
        binary = native.Run(offUnit);
        bool unit = Math.Abs(BitConverter.Int64BitsToDouble(binary["normalised"][3]) - 1d) < 1e-9;
        differences = [.. differences, .. IvpRotationReplay.Differences(binary, IvpRotationReplay.Run(offUnit))];

        output.WriteLine(
            $"control: the binary {(halfTurn ? "made" : "DID NOT make")} a half turn of two quarter turns and " +
            $"{(unit ? "normalized" : "DID NOT normalize")} (0, 0, 0, 1.5); the port differs in {differences.Count} lanes");

        foreach (string difference in differences)
        {
            output.WriteLine($"  {difference}");
        }

        return halfTurn && unit;
    }

    private static void Sweep(TextWriter output, Native native, bool fusedAvailable, int count)
    {
        ulong state = SweepSeed;
        int differing = 0;
        Dictionary<string, int> byField = new(StringComparer.Ordinal);

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(ref state, poison: true, fusedAvailable);
            Dictionary<string, long[]> binary = native.Run(inputs);
            IReadOnlyList<string> differences = IvpRotationReplay.Differences(binary, IvpRotationReplay.Run(inputs));

            if (differences.Count == 0)
            {
                continue;
            }

            differing++;

            foreach (string field in differences.Select(difference => difference.Split(' ')[0]).Distinct(StringComparer.Ordinal))
            {
                byField[field] = byField.GetValueOrDefault(field) + 1;
            }

            if (differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} rotation cases, {differing} differing");

        foreach ((string field, int cases) in byField.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"  {field}: {cases}");
        }
    }

    private static void Fixture(TextWriter output, Native native, string path, bool fusedAvailable)
    {
        ulong state = FixtureSeed;

        using StreamWriter writer = File.CreateText(path);

        writer.WriteLine("# Written by the vphysics-rotation probe from the shipped vphysics.dll. Do not edit by hand.");

        for (int index = 0; index < FixtureCases; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(ref state, poison: true, fusedAvailable);

            IvpRotationReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
        }

        List<(string Label, Dictionary<string, long[]> Inputs)> pairs = NaNPairs(ref state, fusedAvailable);
        List<(string Label, Dictionary<string, long[]> Inputs)> killers = Killers(ref state, fusedAvailable);

        foreach ((string label, Dictionary<string, long[]> inputs) in pairs.Concat(killers))
        {
            IvpRotationReplay.Write(writer, new IvpReplayCase(label, inputs, native.Run(inputs)));
        }

        output.WriteLine(
            $"{FixtureCases} random, {pairs.Count} NaN-pair and {killers.Count} searched cases " +
            $"({killers.Count(killer => killer.Label.StartsWith("substeps-", StringComparison.Ordinal))} sub-step counts) written to {path}");
    }

    /// <summary>
    /// Cases random draws almost never reach, searched for: a spin whose sub-step count moves when its squares are summed in
    /// double rather than float, and a dot whose arc cosine <c>Math.Acos</c> answers a bit differently from vphysics'.
    /// </summary>
    private static List<(string Label, Dictionary<string, long[]> Inputs)> Killers(ref ulong state, bool fusedAvailable)
    {
        const int Wanted = 8;
        const int Trials = 4_000_000;
        List<(string Label, Dictionary<string, long[]> Inputs)> cases = [];
        int counts = 0;
        int arcs = 0;

        for (int trial = 0; trial < Trials && counts < Wanted; trial++)
        {
            float step = Chance(ref state, 0.5) ? 0.015f : 0.001f + ((float)Unit(ref state) * 0.3f);
            double length = (2 + Below(ref state, 40)) / (12d * step) * (1d + (Signed(ref state) * 1e-6));
            (double X, double Y, double Z, double W) direction = UnitQuaternion(ref state);
            double norm = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
            (float X, float Y, float Z) spin = (
                (float)(direction.X / norm * length), (float)(direction.Y / norm * length), (float)(direction.Z / norm * length));

            if (IvpIntegrator.SubSteps(spin, step) == DoubleSumSubSteps(spin, step))
            {
                continue;
            }

            Dictionary<string, long[]> inputs = RandomCase(ref state, poison: false, fusedAvailable);

            inputs["spin"] = [IvpImpactReplay.Lane(spin.X), IvpImpactReplay.Lane(spin.Y), IvpImpactReplay.Lane(spin.Z)];
            inputs["step"][0] = IvpImpactReplay.Lane(step);
            inputs["flags"][0] = 0;
            inputs["phase"][0] = 0;
            cases.Add(($"substeps-{counts++}", inputs));
        }

        for (int trial = 0; trial < Trials && arcs < Wanted; trial++)
        {
            double dot = Unit(ref state) * 0.998d;

            if (BitConverter.DoubleToInt64Bits(IvpMath.Acos(dot)) == BitConverter.DoubleToInt64Bits(Math.Acos(dot)))
            {
                continue;
            }

            Dictionary<string, long[]> inputs = RandomCase(ref state, poison: false, fusedAvailable);

            inputs["first"] = [0L, 0L, 0L, IvpImpactReplay.Lane(1d)];
            inputs["second"] = [IvpImpactReplay.Lane(Math.Sqrt(1d - (dot * dot))), 0L, 0L, IvpImpactReplay.Lane(dot)];
            cases.Add(($"acos-{arcs++}", inputs));
        }

        return cases;
    }

    /// <summary>The sub-step count with the spin's squares summed in double — the variant <c>Killers</c> searches against.</summary>
    private static int DoubleSumSubSteps((float X, float Y, float Z) spin, float step)
    {
        double square = (((double)spin.Y * spin.Y) + ((double)spin.X * spin.X)) + ((double)spin.Z * spin.Z);
        double turn = square * step * step;

        return turn > 1d / 36d ? (int)Math.Sqrt(turn * 144d) + 1 : 1;
    }

    /// <summary>
    /// For every pair of input lanes an addition or multiplication meets, a case with exactly those two set to NaNs of opposite
    /// sign and different payload — on the stepped route and on the one-axis route — the only inputs that tell which operand
    /// the binary makes the destination.
    /// </summary>
    private static List<(string Label, Dictionary<string, long[]> Inputs)> NaNPairs(ref ulong state, bool fusedAvailable)
    {
        List<(string First, int FirstLane, string Second, int SecondLane)> pairs = [];

        for (int first = 0; first < 4; first++)
        {
            pairs.Add(("first", first, "fraction", 0));
            pairs.Add(("second", first, "fraction", 0));

            for (int second = 0; second < 4; second++)
            {
                pairs.Add(("first", first, "second", second));
            }
        }

        for (int first = 0; first < 3; first++)
        {
            pairs.Add(("spin", first, "delta", 0));
            pairs.Add(("spin", first, "step", 0));

            for (int second = 0; second < 3; second++)
            {
                pairs.Add(("inertia", first, "inverse-inertia", second));

                if (second != first)
                {
                    pairs.Add(("spin", first, "spin", second));
                    pairs.Add(("inertia", first, "inertia", second));
                }
            }
        }

        List<(string Label, Dictionary<string, long[]> Inputs)> cases = [];

        foreach ((string first, int firstLane, string second, int secondLane) in pairs)
        {
            foreach (bool oneAxis in new[] { false, true })
            {
                Dictionary<string, long[]> inputs = RandomCase(ref state, poison: false, fusedAvailable);

                inputs["flags"][0] = oneAxis ? 0x8 : 0;
                inputs["phase"][0] = 0;
                inputs["offset58"][0] = oneAxis ? 1 : 0;
                inputs["offset08"][0] = 0;
                SetNaN(inputs, first, firstLane, 0x7fc00001, 0x7ff8000000000001);
                SetNaN(inputs, second, secondLane, 0xffc00002, unchecked((long)0xfff8000000000002UL));
                cases.Add(($"nan-{first}{firstLane}-{second}{secondLane}-{(oneAxis ? "axis" : "stepped")}", inputs));
            }
        }

        return cases;
    }

    private static void SetNaN(Dictionary<string, long[]> inputs, string field, int lane, long floatBits, long doubleBits) =>
        inputs[field][lane] = IvpRotationReplay.Inputs.Single(candidate => candidate.Name == field).Kind == IvpReplayKind.Real64
            ? doubleBits
            : floatBits;

    private static Dictionary<string, long[]> RandomCase(ref ulong state, bool poison, bool fusedAvailable)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpRotationReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        (double X, double Y, double Z, double W) first = UnitQuaternion(ref state);

        if (Chance(ref state, 0.3))
        {
            first = Scale(first, 0.3d + (Unit(ref state) * 1.5d));
        }
        else if (Chance(ref state, 0.2))
        {
            first = Scale(first, 1d + (Signed(ref state) * 1e-7));
        }

        (double X, double Y, double Z, double W) second = Below(ref state, 10) switch
        {
            0 or 1 => Near(ref state, first, 1e-3),
            2 => Scale(Near(ref state, first, 1e-3), -1d),
            3 => first,
            _ => UnitQuaternion(ref state),
        };

        inputs["fused"][0] = fusedAvailable && Chance(ref state, 0.5) ? 1 : 0;
        inputs["first"] = QuaternionLanes(first);
        inputs["second"] = QuaternionLanes(second);
        inputs["fraction"][0] = IvpImpactReplay.Lane(Below(ref state, 10) switch
        {
            0 => 0d,
            1 => 1d,
            2 => Signed(ref state) * 3d,
            _ => Unit(ref state),
        });

        double magnitude = Math.Pow(10d, Below(ref state, 6) - 3);

        for (int lane = 0; lane < 3; lane++)
        {
            inputs["spin"][lane] = IvpImpactReplay.Lane(Chance(ref state, 0.1) ? 0f : (float)(Signed(ref state) * magnitude));

            float inertia = 0.001f + ((float)Unit(ref state) * 10f);

            inputs["inertia"][lane] = IvpImpactReplay.Lane(inertia);
            inputs["inverse-inertia"][lane] = IvpImpactReplay.Lane(Chance(ref state, 0.8) ? 1f / inertia : (float)Unit(ref state));
        }

        float step = Chance(ref state, 0.6) ? 0.015f : 0.001f + ((float)Unit(ref state) * 0.3f);

        inputs["step"][0] = IvpImpactReplay.Lane(step);
        inputs["delta"][0] = IvpImpactReplay.Lane(Chance(ref state, 0.5) ? step : Unit(ref state) * 0.5d);
        inputs["flags"][0] = (Below(ref state, 256) & ~0x8) | (Chance(ref state, 0.2) ? 0x8 : 0);
        inputs["phase"][0] = Chance(ref state, 0.2) ? 5 : Below(ref state, 5);
        inputs["offset58"][0] = Chance(ref state, 0.6) ? 1 : 0;
        inputs["offset08"][0] = IvpImpactReplay.Lane(Below(ref state, 10) switch
        {
            <= 4 => 0f,
            5 => -0f,
            6 => float.NaN,
            _ => (float)Signed(ref state),
        });
        inputs["axis"][0] = Below(ref state, 3);

        if (poison && Chance(ref state, 0.25))
        {
            Poison(ref state, inputs);
        }

        return inputs;
    }

    /// <summary>One to three lanes set to NaNs or infinities; the normalized quaternion only to NaNs, which it leaves alone.</summary>
    private static void Poison(ref ulong state, Dictionary<string, long[]> inputs)
    {
        List<(string Name, int Lane, IvpReplayKind Kind)> lanes = [];

        foreach (IvpReplayField field in IvpRotationReplay.Inputs.Where(field => field.Kind != IvpReplayKind.Whole32))
        {
            for (int lane = 0; lane < field.Count; lane++)
            {
                lanes.Add((field.Name, lane, field.Kind));
            }
        }

        int poisons = 1 + Below(ref state, 3);

        for (int poison = 0; poison < poisons; poison++)
        {
            (string name, int lane, IvpReplayKind kind) = lanes[Below(ref state, lanes.Count)];

            long[] choices = DoublePoisons;

            if (kind == IvpReplayKind.Real32)
            {
                choices = FloatPoisons;
            }
            else if (name == "first")
            {
                choices = QuietDoubles;
            }

            inputs[name][lane] = choices[Below(ref state, choices.Length)];
        }
    }

    private static (double X, double Y, double Z, double W) UnitQuaternion(ref ulong state)
    {
        double x = Signed(ref state);
        double y = Signed(ref state);
        double z = Signed(ref state);
        double w = Signed(ref state);
        double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

        return length > 1e-3 ? (x / length, y / length, z / length, w / length) : (0d, 0d, 0d, 1d);
    }

    private static (double X, double Y, double Z, double W) Near(ref ulong state, (double X, double Y, double Z, double W) rotation, double spread)
    {
        (double X, double Y, double Z, double W) moved = (
            rotation.X + (Signed(ref state) * spread), rotation.Y + (Signed(ref state) * spread),
            rotation.Z + (Signed(ref state) * spread), rotation.W + (Signed(ref state) * spread));
        double length = Math.Sqrt((moved.X * moved.X) + (moved.Y * moved.Y) + (moved.Z * moved.Z) + (moved.W * moved.W));

        return Scale(moved, 1d / length);
    }

    private static (double X, double Y, double Z, double W) Scale((double X, double Y, double Z, double W) rotation, double factor) =>
        (rotation.X * factor, rotation.Y * factor, rotation.Z * factor, rotation.W * factor);

    private static long[] QuaternionLanes((double X, double Y, double Z, double W) rotation) =>
    [
        IvpImpactReplay.Lane(rotation.X), IvpImpactReplay.Lane(rotation.Y), IvpImpactReplay.Lane(rotation.Z), IvpImpactReplay.Lane(rotation.W),
    ];

    private static int Below(ref ulong state, int bound) => (int)(VphysicsLibrary.SplitMix(ref state) % (ulong)bound);

    private static bool Chance(ref ulong state, double probability) => Unit(ref state) < probability;

    private static double Unit(ref ulong state) => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state));

    private static double Signed(ref ulong state) => (Unit(ref state) * 2d) - 1d;

    /// <summary>The quaternions, a spin, a core, its environment and the object chain, in unmanaged memory at the engine's offsets.</summary>
    private sealed class Native : IDisposable
    {
        private const int QuaternionSize = 32;
        private const int SpinSize = 16;
        private const int CoreSize = 0x270;
        private const int EnvironmentSize = 0x200;
        private const int ObjectSize = 0x80;

        private readonly List<nint> _blocks = [];
        private readonly ProductFunction _product;
        private readonly NormaliseFunction _normalise;
        private readonly InterpolateFunction _interpolate;
        private readonly DeltaFunction _delta;
        private readonly DeltaFunction _sineDelta;
        private readonly RotateFunction _rotate;
        private readonly nint _flag;
        private readonly nint _first;
        private readonly nint _second;
        private readonly nint _output;
        private readonly nint _spin;
        private readonly nint _core;
        private readonly nint _environment;
        private readonly nint _object;
        private readonly nint _inner;

        public Native(nint module)
        {
            _product = VphysicsLibrary.Function<ProductFunction>(module, ProductAddress);
            _normalise = VphysicsLibrary.Function<NormaliseFunction>(module, NormaliseAddress);
            _interpolate = VphysicsLibrary.Function<InterpolateFunction>(module, InterpolateAddress);
            _delta = VphysicsLibrary.Function<DeltaFunction>(module, DeltaAddress);
            _sineDelta = VphysicsLibrary.Function<DeltaFunction>(module, SineDeltaAddress);
            _rotate = VphysicsLibrary.Function<RotateFunction>(module, RotateAddress);
            _flag = VphysicsLibrary.Address(module, FusedFlagAddress);

            _first = Allocate(QuaternionSize);
            _second = Allocate(QuaternionSize);
            _output = Allocate(QuaternionSize);
            _spin = Allocate(SpinSize);
            _core = Allocate(CoreSize);
            _environment = Allocate(EnvironmentSize);
            _object = Allocate(ObjectSize);
            _inner = Allocate(ObjectSize);
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Dictionary<string, long[]> outputs = new(StringComparer.Ordinal);
            double fraction = BitConverter.Int64BitsToDouble(inputs["fraction"][0]);
            double delta = BitConverter.Int64BitsToDouble(inputs["delta"][0]);

            Marshal.WriteInt32(_flag, unchecked((int)inputs["fused"][0]));

            WriteDoubles(_first, inputs["first"]);
            WriteDoubles(_second, inputs["second"]);
            Zero(_output, QuaternionSize);
            _product(_output, _first, _second);
            outputs["product"] = ReadDoubles(_output);

            _normalise(_first);
            outputs["normalised"] = ReadDoubles(_first);

            WriteDoubles(_first, inputs["first"]);
            Zero(_output, QuaternionSize);
            _interpolate(_output, _first, _second, fraction);
            outputs["interpolated"] = ReadDoubles(_output);

            Zero(_spin, SpinSize);
            WriteFloats(_spin, 0, inputs["spin"]);
            Zero(_output, QuaternionSize);
            _delta(_output, _spin, delta);
            outputs["delta-turn"] = ReadDoubles(_output);

            Zero(_output, QuaternionSize);
            _sineDelta(_output, _spin, delta);
            outputs["sine-turn"] = ReadDoubles(_output);

            WriteCore(inputs);
            Zero(_output, QuaternionSize);
            _rotate(_core, BitConverter.Int32BitsToSingle(unchecked((int)inputs["step"][0])), _output);
            outputs["turn"] = ReadDoubles(_output);
            outputs["turned-spin"] =
            [
                (uint)Marshal.ReadInt32(_core, 0x130), (uint)Marshal.ReadInt32(_core, 0x134), (uint)Marshal.ReadInt32(_core, 0x138),
            ];

            return outputs;
        }

        public void Dispose()
        {
            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }

            _blocks.Clear();
        }

        private void WriteCore(Dictionary<string, long[]> inputs)
        {
            Zero(_core, CoreSize);
            Zero(_environment, EnvironmentSize);
            Zero(_object, ObjectSize);
            Zero(_inner, ObjectSize);

            Marshal.WriteByte(_core, 0, unchecked((byte)inputs["flags"][0]));
            Marshal.WriteInt32(_core, 0x8, unchecked((int)inputs["offset08"][0]));
            Marshal.WriteIntPtr(_core, 0x10, _environment);
            WriteFloats(_core, 0x20, inputs["inertia"]);
            WriteFloats(_core, 0x40, inputs["inverse-inertia"]);
            WriteFloats(_core, 0x130, inputs["spin"]);
            Marshal.WriteIntPtr(_core, 0x58, inputs["offset58"][0] != 0 ? _object : 0);
            Marshal.WriteIntPtr(_object, 0x8, _inner);
            Marshal.WriteInt32(_inner, 0x48, unchecked((int)inputs["axis"][0]));
            Marshal.WriteInt32(_environment, 0x1ac, unchecked((int)inputs["phase"][0]));
        }

        private static void WriteDoubles(nint block, long[] lanes)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                Marshal.WriteInt64(block, lane * 8, lanes[lane]);
            }
        }

        private static long[] ReadDoubles(nint block) =>
            [Marshal.ReadInt64(block, 0), Marshal.ReadInt64(block, 8), Marshal.ReadInt64(block, 16), Marshal.ReadInt64(block, 24)];

        private static void WriteFloats(nint block, int offset, long[] lanes)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                Marshal.WriteInt32(block, offset + (lane * 4), unchecked((int)lanes[lane]));
            }
        }

        private static void Zero(nint block, int size)
        {
            for (int offset = 0; offset < size; offset++)
            {
                Marshal.WriteByte(block, offset, 0);
            }
        }

        private nint Allocate(int size)
        {
            nint block = Marshal.AllocHGlobal(size);

            _blocks.Add(block);
            Zero(block, size);

            return block;
        }
    }
}
