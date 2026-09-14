using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s heap-solve core routines — <c>FUN_1800a9280</c>, <c>FUN_180076710</c>,
/// <c>FUN_180077950</c>, <c>FUN_180076670</c> and <c>FUN_180077e80</c> — called in process on fabricated cores and a record, the
/// oracle for <c>IvpContactRecord.Push</c>, <c>IvpPush</c> and <c>IvpRigidBody.KineticEnergy</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary is the instrument.** Two cores, a record and an environment with its limits are written at the offsets
/// `docs/findings/51` reads; nothing else is reachable from these routines. Every stage starts from freshly written cores.
///
/// **The control first**: a core moving at `(3, 4, 0)` held to a speed of one must come out a unit long, by the binary and by the
/// port alike.
///
/// **Modes.** With no mode, or `sweep n`, random cases — a quarter of them seeded with NaNs of both signs, signalling NaNs and
/// infinities — are compared lane by lane; `fixture path` writes the cases `IvpHeapCoreConformanceTests` reads.
/// </remarks>
public sealed class VphysicsHeapCoreProbe : IProbe
{
    private const long PushAddress = 0x1800a9280;
    private const long LimitAddress = 0x180076710;
    private const long FlushAddress = 0x180077950;
    private const long DropAddress = 0x180076670;
    private const long EnergyAddress = 0x180077e80;

    private const int DefaultSweep = 50_000;
    private const int FixtureCases = 240;
    private const ulong FixtureSeed = 18009;
    private const ulong SweepSeed = 20260914;

    /// <summary>Quiet NaNs of both signs, one with a payload, signalling NaNs of both signs, and both infinities, as floats.</summary>
    private static readonly long[] FloatPoisons =
    [
        0x7fc00000, 0xffc00000, 0x7fc00123, 0x7f800001, 0xffa00000, 0x7f800000, 0xff800000,
    ];

    private static readonly long[] DoublePoisons =
    [
        0x7ff8000000000000, unchecked((long)0xfff8000000000000UL), 0x7ff8000000000123, unchecked((long)0xfff0000000000000UL),
    ];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PushFunction(nint record, double push);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CoreFunction(nint core);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double EnergyFunction(nint core, nint velocity, nint spin);

    /// <inheritdoc />
    public string Name => "vphysics-heap-core";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's heap-solve core routines (the record push FUN_1800a9280, the limits FUN_180076710, the flush and drop, " +
        "the kinetic energy FUN_180077e80) called in process and compared with the port; 'fixture' writes the conformance " +
        "suite's cases: vphysics-heap-core [sweep n | fixture path]";

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

        if (!Control(output, native))
        {
            return;
        }

        if (arguments.Count >= 2 && arguments[0] == "fixture")
        {
            Fixture(output, native, arguments[1]);
            return;
        }

        Sweep(output, native, arguments.Count >= 2 && arguments[0] == "sweep"
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : DefaultSweep);
    }

    private static bool Control(TextWriter output, Native native)
    {
        ulong state = SweepSeed;
        Dictionary<string, long[]> inputs = RandomCase(ref state, poison: false);

        inputs["max-velocity"][0] = IvpImpactReplay.Lane(1f);
        inputs["first-velocity"] = [IvpImpactReplay.Lane(3f), IvpImpactReplay.Lane(4f), IvpImpactReplay.Lane(0f)];

        Dictionary<string, long[]> binary = native.Run(inputs);
        long[] held = binary["limited-first-velocity"];
        double x = BitConverter.Int32BitsToSingle(unchecked((int)held[0]));
        double y = BitConverter.Int32BitsToSingle(unchecked((int)held[1]));
        bool unit = Math.Abs(Math.Sqrt((x * x) + (y * y)) - 1d) < 1e-6;
        IReadOnlyList<string> differences = IvpHeapCoreReplay.Differences(binary, IvpHeapCoreReplay.Run(inputs));

        output.WriteLine($"control: the binary {(unit ? "held" : "DID NOT hold")} (3, 4, 0) to a unit speed; the port differs in {differences.Count} lanes");

        foreach (string difference in differences)
        {
            output.WriteLine($"  {difference}");
        }

        return unit;
    }

    private static void Sweep(TextWriter output, Native native, int count)
    {
        ulong state = SweepSeed;
        int differing = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(ref state, poison: true);
            Dictionary<string, long[]> binary = native.Run(inputs);
            IReadOnlyList<string> differences = IvpHeapCoreReplay.Differences(binary, IvpHeapCoreReplay.Run(inputs));

            if (differences.Count == 0)
            {
                continue;
            }

            differing++;

            if (differing <= 5)
            {
                output.WriteLine($"case {index}: {differences.Count} lanes differ, first {differences[0]}");
            }
        }

        output.WriteLine($"{count} heap-core cases, {differing} differing");
    }

    private static void Fixture(TextWriter output, Native native, string path)
    {
        ulong state = FixtureSeed;

        using StreamWriter writer = File.CreateText(path);

        writer.WriteLine("# Written by the vphysics-heap-core probe from the shipped vphysics.dll. Do not edit by hand.");

        for (int index = 0; index < FixtureCases; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(ref state, poison: true);

            IvpHeapCoreReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
        }

        output.WriteLine($"{FixtureCases} cases written to {path}");
    }

    private static Dictionary<string, long[]> RandomCase(ref ulong state, bool poison)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpHeapCoreReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        inputs["inverse-step"][0] = IvpImpactReplay.Lane(Chance(ref state, 0.6) ? 66d : 10d + (Unit(ref state) * 190d));
        inputs["max-velocity"][0] = IvpImpactReplay.Lane(Limit(ref state, 100f));
        inputs["max-spin"][0] = IvpImpactReplay.Lane(Limit(ref state, 0.5f));
        inputs["push"][0] = IvpImpactReplay.Lane(Chance(ref state, 0.1) ? 0d : Signed(ref state) * Magnitude(ref state));
        inputs["movable"][0] = Chance(ref state, 0.8) ? 1 : 0;
        inputs["movable"][1] = Chance(ref state, 0.6) ? 1 : 0;
        Floats(inputs["normal"], 0, Direction(ref state));
        FillFloats(ref state, inputs["turns"]);
        FillFloats(ref state, inputs["energy-velocity"]);
        FillFloats(ref state, inputs["energy-spin"]);

        foreach (string side in IvpHeapCoreReplay.SideNames)
        {
            float mass = 0.1f + ((float)Unit(ref state) * 100f);

            inputs[side + "mass"][0] = IvpImpactReplay.Lane(mass);
            inputs[side + "inverse-mass"][0] = IvpImpactReplay.Lane(Chance(ref state, 0.8) ? 1f / mass : (float)Unit(ref state));

            for (int lane = 0; lane < 3; lane++)
            {
                float inertia = 0.001f + ((float)Unit(ref state) * 10f);

                inputs[side + "inertia"][lane] = IvpImpactReplay.Lane(inertia);
                inputs[side + "inverse-inertia"][lane] = IvpImpactReplay.Lane(1f / inertia);
            }

            foreach (string vector in IvpHeapCoreReplay.VectorNames)
            {
                if (vector.StartsWith("pending", StringComparison.Ordinal) && Chance(ref state, 0.3))
                {
                    continue;
                }

                FillFloats(ref state, inputs[side + vector]);
            }
        }

        if (poison && Chance(ref state, 0.25))
        {
            Poison(ref state, inputs);
        }

        return inputs;
    }

    private static void Poison(ref ulong state, Dictionary<string, long[]> inputs)
    {
        List<(string Name, int Lane)> floats = [];

        foreach (IvpReplayField field in IvpHeapCoreReplay.Inputs)
        {
            for (int lane = 0; field.Kind == IvpReplayKind.Real32 && lane < field.Count; lane++)
            {
                floats.Add((field.Name, lane));
            }
        }

        int poisons = 1 + Below(ref state, 4);

        for (int poison = 0; poison < poisons; poison++)
        {
            (string name, int lane) = floats[Below(ref state, floats.Count)];

            inputs[name][lane] = FloatPoisons[Below(ref state, FloatPoisons.Length)];
        }

        if (Chance(ref state, 0.2))
        {
            inputs["push"][0] = DoublePoisons[Below(ref state, DoublePoisons.Length)];
        }
    }

    private static float Limit(ref ulong state, float typical)
    {
        int shape = Below(ref state, 10);

        if (shape == 0)
        {
            return 0f;
        }

        if (shape == 1)
        {
            return -typical;
        }

        return typical * (float)(0.01 + (Unit(ref state) * 2d));
    }

    private static void FillFloats(ref ulong state, long[] lanes)
    {
        double magnitude = Magnitude(ref state);

        for (int lane = 0; lane < lanes.Length; lane++)
        {
            lanes[lane] = IvpImpactReplay.Lane((float)(Signed(ref state) * magnitude));
        }
    }

    private static void Floats(long[] lanes, int start, (double X, double Y, double Z) vector)
    {
        lanes[start] = IvpImpactReplay.Lane((float)vector.X);
        lanes[start + 1] = IvpImpactReplay.Lane((float)vector.Y);
        lanes[start + 2] = IvpImpactReplay.Lane((float)vector.Z);
    }

    private static (double X, double Y, double Z) Direction(ref ulong state)
    {
        double x = Signed(ref state);
        double y = Signed(ref state);
        double z = Signed(ref state);
        double length = Math.Sqrt((x * x) + (y * y) + (z * z));

        return length > 1e-6 ? (x / length, y / length, z / length) : (0d, 0d, 1d);
    }

    private static double Magnitude(ref ulong state) => Math.Pow(10d, Below(ref state, 7) - 3);

    private static int Below(ref ulong state, int bound) => (int)(VphysicsLibrary.SplitMix(ref state) % (ulong)bound);

    private static bool Chance(ref ulong state, double probability) => Unit(ref state) < probability;

    private static double Unit(ref ulong state) => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state));

    private static double Signed(ref ulong state) => (Unit(ref state) * 2d) - 1d;

    /// <summary>Two cores, a record, the environment and its limits, in unmanaged memory at the engine's offsets.</summary>
    private sealed class Native : IDisposable
    {
        private const int CoreSize = 0x270;
        private const int RecordSize = 0x110;
        private const int EnvironmentSize = 0x200;
        private const int LimitsSize = 0x40;
        private const int VectorSize = 12;

        private readonly List<nint> _blocks = [];
        private readonly PushFunction _push;
        private readonly CoreFunction _limit;
        private readonly CoreFunction _flush;
        private readonly CoreFunction _drop;
        private readonly EnergyFunction _energy;
        private readonly nint _first;
        private readonly nint _second;
        private readonly nint _record;
        private readonly nint _environment;
        private readonly nint _limits;
        private readonly nint _velocity;
        private readonly nint _spin;

        public Native(nint module)
        {
            _push = VphysicsLibrary.Function<PushFunction>(module, PushAddress);
            _limit = VphysicsLibrary.Function<CoreFunction>(module, LimitAddress);
            _flush = VphysicsLibrary.Function<CoreFunction>(module, FlushAddress);
            _drop = VphysicsLibrary.Function<CoreFunction>(module, DropAddress);
            _energy = VphysicsLibrary.Function<EnergyFunction>(module, EnergyAddress);

            _first = Allocate(CoreSize);
            _second = Allocate(CoreSize);
            _record = Allocate(RecordSize);
            _environment = Allocate(EnvironmentSize);
            _limits = Allocate(LimitsSize);
            _velocity = Allocate(VectorSize);
            _spin = Allocate(VectorSize);
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Dictionary<string, long[]> outputs = new(StringComparer.Ordinal);
            IReadOnlyList<string> stages = IvpHeapCoreReplay.StageNames;

            Zero(_environment, EnvironmentSize);
            Zero(_limits, LimitsSize);
            Marshal.WriteIntPtr(_environment, 0x48, _limits);
            Marshal.WriteInt64(_environment, 0x110, inputs["inverse-step"][0]);
            Marshal.WriteInt32(_limits, 0xc, unchecked((int)inputs["max-velocity"][0]));
            Marshal.WriteInt32(_limits, 0x14, unchecked((int)inputs["max-spin"][0]));

            WriteCores(inputs);
            Zero(_record, RecordSize);
            WriteLanes(_record, 0x20, inputs["normal"], 0, 3);
            WriteLanes(_record, 0xf0, inputs["turns"], 0, 3);
            WriteLanes(_record, 0x100, inputs["turns"], 3, 3);
            Marshal.WriteIntPtr(_record, 0x98, inputs["movable"][0] != 0 ? _first : 0);
            Marshal.WriteIntPtr(_record, 0xa0, inputs["movable"][1] != 0 ? _second : 0);
            _push(_record, BitConverter.Int64BitsToDouble(inputs["push"][0]));
            ReadCores(outputs, stages[0]);

            WriteCores(inputs);
            _limit(_first);
            _limit(_second);
            ReadCores(outputs, stages[1]);

            WriteCores(inputs);
            _flush(_first);
            _flush(_second);
            ReadCores(outputs, stages[2]);

            WriteCores(inputs);
            _drop(_first);
            _drop(_second);
            ReadCores(outputs, stages[3]);

            WriteCores(inputs);
            WriteLanes(_velocity, 0, inputs["energy-velocity"], 0, 3);
            WriteLanes(_spin, 0, inputs["energy-spin"], 0, 3);
            outputs["energy"] =
            [
                BitConverter.DoubleToInt64Bits(_energy(_first, _velocity, _spin)),
                BitConverter.DoubleToInt64Bits(_energy(_second, _velocity, _spin)),
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

        private void WriteCores(Dictionary<string, long[]> inputs)
        {
            IReadOnlyList<string> sides = IvpHeapCoreReplay.SideNames;

            WriteCore(_first, inputs, sides[0]);
            WriteCore(_second, inputs, sides[1]);
        }

        private void WriteCore(nint core, Dictionary<string, long[]> inputs, string side)
        {
            Zero(core, CoreSize);
            Marshal.WriteIntPtr(core, 0x10, _environment);
            WriteLanes(core, 0x20, inputs[side + "inertia"], 0, 3);
            WriteLanes(core, 0x2c, inputs[side + "mass"], 0, 1);
            WriteLanes(core, 0x40, inputs[side + "inverse-inertia"], 0, 3);
            WriteLanes(core, 0x4c, inputs[side + "inverse-mass"], 0, 1);
            WriteLanes(core, 0x110, inputs[side + "pending-spin"], 0, 3);
            WriteLanes(core, 0x120, inputs[side + "pending-velocity"], 0, 3);
            WriteLanes(core, 0x130, inputs[side + "spin"], 0, 3);
            WriteLanes(core, 0x140, inputs[side + "velocity"], 0, 3);
        }

        private void ReadCores(Dictionary<string, long[]> outputs, string stage)
        {
            IReadOnlyList<string> sides = IvpHeapCoreReplay.SideNames;

            foreach ((string side, nint core) in new[] { (sides[0], _first), (sides[1], _second) })
            {
                outputs[stage + side + "velocity"] = ReadLanes(core, 0x140);
                outputs[stage + side + "spin"] = ReadLanes(core, 0x130);
                outputs[stage + side + "pending-velocity"] = ReadLanes(core, 0x120);
                outputs[stage + side + "pending-spin"] = ReadLanes(core, 0x110);
            }
        }

        private static void WriteLanes(nint block, int offset, long[] lanes, int start, int count)
        {
            for (int lane = 0; lane < count; lane++)
            {
                Marshal.WriteInt32(block, offset + (lane * 4), unchecked((int)lanes[start + lane]));
            }
        }

        private static long[] ReadLanes(nint block, int offset) =>
        [
            (uint)Marshal.ReadInt32(block, offset), (uint)Marshal.ReadInt32(block, offset + 4), (uint)Marshal.ReadInt32(block, offset + 8),
        ];

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
