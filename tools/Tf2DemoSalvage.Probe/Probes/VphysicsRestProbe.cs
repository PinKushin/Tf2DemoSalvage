using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s rest test <c>FUN_180077220</c> and its process-wide generator <c>FUN_18007d5c0</c>, called in
/// process on a fabricated core — the oracle for <c>IvpRigidBody.TestRest</c> and <c>IvpRandom</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary is the instrument.** A core is written with its position, both orientations, its spin and radius, and the two
/// anchors the test keeps (`+0x200..0x24c`), beside an environment holding the rest delay at `+0xc8`; the time goes in by value,
/// as an `IVP_Time` is passed. The generator's seed at `180124fe8` is written before each draw and restored afterwards.
///
/// **The control first**: a core sitting on both anchors, not spinning, and still for longer than the delay and four seconds
/// must be at rest (3); moved far from its anchor it must be moving (1).
///
/// **Modes.** With no mode, or `sweep n`, random cases — a fifth of them seeded with NaNs and infinities — are compared lane by
/// lane; `fixture path` writes the cases `IvpRestConformanceTests` reads.
/// </remarks>
public sealed class VphysicsRestProbe : IProbe
{
    private const long RestAddress = 0x180077220;
    private const long DrawAddress = 0x18007d5c0;
    private const long SeedAddress = 0x180124fe8;

    private const int DefaultSweep = 100_000;
    private const int FixtureCases = 400;
    private const ulong FixtureSeed = 77220;
    private const ulong SweepSeed = 20260916;

    private static readonly long[] FloatPoisons = [0x7fc00000, 0xffc00000, 0x7fc00123, 0x7f800001, 0x7f800000, 0xff800000];

    private static readonly long[] DoublePoisons =
    [
        0x7ff8000000000000, unchecked((long)0xfff8000000000000UL), 0x7ff8000000000123, 0x7ff0000000000001,
        0x7ff0000000000000, unchecked((long)0xfff0000000000000UL),
    ];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RestFunction(nint core, long now);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float DrawFunction();

    /// <inheritdoc />
    public string Name => "vphysics-rest";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's rest test FUN_180077220 and generator FUN_18007d5c0 called in process and compared with the port; " +
        "'fixture' writes the conformance suite's cases: vphysics-rest [sweep n | fixture path]";

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
        nint seed = VphysicsLibrary.Address(module, SeedAddress);
        int loaded = Marshal.ReadInt32(seed);

        try
        {
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
        finally
        {
            Marshal.WriteInt32(seed, loaded);
        }
    }

    private static bool Control(TextWriter output, Native native)
    {
        Draws draws = new(SweepSeed);
        Dictionary<string, long[]> resting = RandomCase(draws, poison: false);

        resting["now"] = [IvpImpactReplay.Lane(100d)];
        resting["rest-delay"] = [IvpImpactReplay.Lane(1f)];
        resting["position"] = [IvpImpactReplay.Lane(1.5d), IvpImpactReplay.Lane(-2d), IvpImpactReplay.Lane(3.25d)];
        resting["orientation"] = [0L, 0L, 0L, IvpImpactReplay.Lane(1d)];
        resting["working-orientation"] = [0L, 0L, 0L, IvpImpactReplay.Lane(1d)];
        resting["spin"] = [0L, 0L, 0L];
        resting["anchor-time"] = [IvpImpactReplay.Lane(90d)];
        resting["anchor-orientation"] = [0L, 0L, 0L, IvpImpactReplay.Lane(1f)];
        resting["anchor-position"] = [IvpImpactReplay.Lane(1.5f), IvpImpactReplay.Lane(-2f), IvpImpactReplay.Lane(3.25f)];
        resting["settle-time"] = [IvpImpactReplay.Lane(90d)];
        resting["settle-orientation"] = [0L, 0L, 0L, IvpImpactReplay.Lane(1f)];
        resting["settle-position"] = [IvpImpactReplay.Lane(1.5f), IvpImpactReplay.Lane(-2f), IvpImpactReplay.Lane(3.25f)];

        Dictionary<string, long[]> moving = new(resting, StringComparer.Ordinal)
        {
            ["anchor-position"] = [0L, 0L, 0L],
            ["settle-position"] = [0L, 0L, 0L],
        };

        Dictionary<string, long[]> atRest = native.Run(resting);
        Dictionary<string, long[]> moved = native.Run(moving);
        bool answered = atRest["motion"][0] == 3 && moved["motion"][0] == 1;
        IReadOnlyList<string> differences =
        [
            .. IvpRestReplay.Differences(atRest, IvpRestReplay.Run(resting)),
            .. IvpRestReplay.Differences(moved, IvpRestReplay.Run(moving)),
        ];

        output.WriteLine(
            $"control: the binary answered {atRest["motion"][0]} for a core on its anchors (3 expected) and {moved["motion"][0]} " +
            $"for one moved away (1 expected); the port differs in {differences.Count} lanes");

        foreach (string difference in differences)
        {
            output.WriteLine($"  {difference}");
        }

        return answered;
    }

    private static void Sweep(TextWriter output, Native native, int count)
    {
        Draws draws = new(SweepSeed);
        int differing = 0;
        int[] answers = new int[4];

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(draws, poison: true);
            Dictionary<string, long[]> binary = native.Run(inputs);
            IReadOnlyList<string> differences = IvpRestReplay.Differences(binary, IvpRestReplay.Run(inputs));

            answers[Math.Clamp((int)binary["motion"][0], 0, 3)]++;

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

        output.WriteLine($"{count} rest cases, {differing} differing; the binary answered 1: {answers[1]}, 2: {answers[2]}, 3: {answers[3]}");
    }

    private static void Fixture(TextWriter output, Native native, string path)
    {
        Draws draws = new(FixtureSeed);

        using StreamWriter writer = File.CreateText(path);

        writer.WriteLine("# Written by the vphysics-rest probe from the shipped vphysics.dll. Do not edit by hand.");

        for (int index = 0; index < FixtureCases; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(draws, poison: true);

            IvpRestReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
        }

        output.WriteLine($"{FixtureCases} cases written to {path}");
    }

    private static Dictionary<string, long[]> RandomCase(Draws draws, bool poison)
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);
        double now = 10d + (draws.Unit() * 100d);
        float delay = draws.Chance(0.1) ? 0f : 0.05f + ((float)draws.Unit() * 2f);
        (double X, double Y, double Z) position = (draws.Signed() * 100d, draws.Signed() * 100d, draws.Signed() * 100d);
        (double X, double Y, double Z, double W) working = draws.Rotation();
        (double X, double Y, double Z, double W) previous = draws.Below(10) switch
        {
            <= 4 => working,
            <= 7 => draws.Near(working, Math.Pow(10d, -2 - draws.Below(5))),
            _ => draws.Rotation(),
        };
        double limit = 2.356194490192345d / Math.Max(delay, 0.05f);
        double spin = limit * new[] { 0d, 0.1d, 0.5d, 0.9d, 1.1d, 3d }[draws.Below(6)] / Math.Sqrt(3d);

        inputs["now"] = [IvpImpactReplay.Lane(now)];
        inputs["rest-delay"] = [IvpImpactReplay.Lane(delay)];
        inputs["radius"] = [IvpImpactReplay.Lane(0.01f + (float)draws.Unit())];
        inputs["position"] = [IvpImpactReplay.Lane(position.X), IvpImpactReplay.Lane(position.Y), IvpImpactReplay.Lane(position.Z)];
        inputs["orientation"] = Doubles(previous);
        inputs["working-orientation"] = Doubles(working);
        inputs["spin"] = [Float(draws.Signed() * spin), Float(draws.Signed() * spin), Float(draws.Signed() * spin)];
        inputs["anchor-time"] = [IvpImpactReplay.Lane(draws.Chance(0.1) ? now : now - (delay * draws.Unit() * 2d))];
        inputs["anchor-orientation"] = Floats(draws.Below(10) <= 4 ? working : draws.Near(working, Math.Pow(10d, -1 - draws.Below(5))));
        inputs["anchor-position"] = Offset(draws, position, 4);
        inputs["settle-time"] = [IvpImpactReplay.Lane(now - (draws.Unit() * 8d))];
        inputs["settle-orientation"] = Floats(draws.Below(10) <= 4 ? previous : draws.Near(previous, Math.Pow(10d, -draws.Below(5))));
        inputs["settle-position"] = Offset(draws, position, 3);
        inputs["seed"] = [draws.Chance(0.1) ? 1 : draws.Whole()];

        if (poison && draws.Chance(0.2))
        {
            Poison(draws, inputs);
        }

        return inputs;
    }

    private static void Poison(Draws draws, Dictionary<string, long[]> inputs)
    {
        List<(string Name, int Lane, IvpReplayKind Kind)> lanes =
        [
            .. IvpRestReplay.Inputs
                .Where(field => field.Kind != IvpReplayKind.Whole32)
                .SelectMany(field => Enumerable.Range(0, field.Count).Select(lane => (field.Name, lane, field.Kind))),
        ];

        int poisons = 1 + draws.Below(3);

        for (int poison = 0; poison < poisons; poison++)
        {
            (string name, int lane, IvpReplayKind kind) = lanes[draws.Below(lanes.Count)];
            long[] choices = kind == IvpReplayKind.Real32 ? FloatPoisons : DoublePoisons;

            inputs[name][lane] = choices[draws.Below(choices.Length)];
        }
    }

    /// <summary>A position narrowed to float and moved by up to <c>10^−k</c> per lane — exactly on it a tenth of the time.</summary>
    private static long[] Offset(Draws draws, (double X, double Y, double Z) position, int spread)
    {
        double size = draws.Chance(0.1) ? 0d : Math.Pow(10d, -draws.Below(spread + 1));

        return
        [
            Float(position.X + (draws.Signed() * size)), Float(position.Y + (draws.Signed() * size)),
            Float(position.Z + (draws.Signed() * size)),
        ];
    }

    private static long Float(double value) => IvpImpactReplay.Lane((float)value);

    private static long[] Doubles((double X, double Y, double Z, double W) rotation) =>
    [
        IvpImpactReplay.Lane(rotation.X), IvpImpactReplay.Lane(rotation.Y), IvpImpactReplay.Lane(rotation.Z), IvpImpactReplay.Lane(rotation.W),
    ];

    private static long[] Floats((double X, double Y, double Z, double W) rotation) =>
        [Float(rotation.X), Float(rotation.Y), Float(rotation.Z), Float(rotation.W)];

    /// <summary>The probe's own random draws, split-mix seeded.</summary>
    private sealed class Draws(ulong seed)
    {
        private ulong _state = seed;

        public int Whole() => unchecked((int)VphysicsLibrary.SplitMix(ref _state));

        public double Unit() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));

        public double Signed() => (Unit() * 2d) - 1d;

        public bool Chance(double probability) => Unit() < probability;

        public int Below(int bound) => (int)(Unit() * bound);

        public (double X, double Y, double Z, double W) Rotation()
        {
            (double x, double y, double z, double w) = (Signed(), Signed(), Signed(), Signed());
            double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

            return length > 1e-3 ? (x / length, y / length, z / length, w / length) : (0d, 0d, 0d, 1d);
        }

        public (double X, double Y, double Z, double W) Near((double X, double Y, double Z, double W) rotation, double spread)
        {
            (double x, double y, double z, double w) = (
                rotation.X + (Signed() * spread), rotation.Y + (Signed() * spread),
                rotation.Z + (Signed() * spread), rotation.W + (Signed() * spread));
            double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

            return (x / length, y / length, z / length, w / length);
        }
    }

    /// <summary>A core and its environment in unmanaged memory at the engine's offsets.</summary>
    private sealed class Native : IDisposable
    {
        private const int CoreSize = 0x270;
        private const int EnvironmentSize = 0x200;

        private readonly RestFunction _rest;
        private readonly DrawFunction _draw;
        private readonly nint _seed;
        private readonly nint _core = Marshal.AllocHGlobal(CoreSize);
        private readonly nint _environment = Marshal.AllocHGlobal(EnvironmentSize);

        public Native(nint module)
        {
            _rest = VphysicsLibrary.Function<RestFunction>(module, RestAddress);
            _draw = VphysicsLibrary.Function<DrawFunction>(module, DrawAddress);
            _seed = VphysicsLibrary.Address(module, SeedAddress);
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Zero(_core, CoreSize);
            Zero(_environment, EnvironmentSize);
            Marshal.WriteInt32(_environment, 0xc8, unchecked((int)inputs["rest-delay"][0]));
            Marshal.WriteIntPtr(_core, 0x10, _environment);
            Marshal.WriteInt32(_core, 0x4, unchecked((int)inputs["radius"][0]));
            WriteFloats(_core, 0x130, inputs["spin"]);
            WriteDoubles(_core, 0x150, inputs["position"]);
            WriteDoubles(_core, 0x180, inputs["orientation"]);
            WriteDoubles(_core, 0x1a0, inputs["working-orientation"]);
            WriteDoubles(_core, 0x200, inputs["anchor-time"]);
            WriteDoubles(_core, 0x208, inputs["settle-time"]);
            WriteFloats(_core, 0x210, inputs["anchor-orientation"]);
            WriteFloats(_core, 0x220, inputs["settle-orientation"]);
            WriteFloats(_core, 0x230, inputs["anchor-position"]);
            WriteFloats(_core, 0x240, inputs["settle-position"]);

            int motion = _rest(_core, inputs["now"][0]);

            Marshal.WriteInt32(_seed, unchecked((int)inputs["seed"][0]));

            float draw = _draw();

            return new Dictionary<string, long[]>(StringComparer.Ordinal)
            {
                ["motion"] = [motion],
                ["anchored-time"] = [Marshal.ReadInt64(_core, 0x200)],
                ["anchored-orientation"] = ReadFloats(_core, 0x210, 4),
                ["anchored-position"] = ReadFloats(_core, 0x230, 3),
                ["settled-time"] = [Marshal.ReadInt64(_core, 0x208)],
                ["settled-orientation"] = ReadFloats(_core, 0x220, 4),
                ["settled-position"] = ReadFloats(_core, 0x240, 3),
                ["draw"] = [IvpImpactReplay.Lane(draw)],
                ["drawn-seed"] = [Marshal.ReadInt32(_seed)],
            };
        }

        public void Dispose()
        {
            Marshal.FreeHGlobal(_core);
            Marshal.FreeHGlobal(_environment);
        }

        private static void WriteDoubles(nint block, int offset, long[] lanes)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                Marshal.WriteInt64(block, offset + (lane * 8), lanes[lane]);
            }
        }

        private static void WriteFloats(nint block, int offset, long[] lanes)
        {
            for (int lane = 0; lane < lanes.Length; lane++)
            {
                Marshal.WriteInt32(block, offset + (lane * 4), unchecked((int)lanes[lane]));
            }
        }

        private static long[] ReadFloats(nint block, int offset, int count) =>
            [.. Enumerable.Range(0, count).Select(lane => (long)(uint)Marshal.ReadInt32(block, offset + (lane * 4)))];

        private static void Zero(nint block, int size)
        {
            for (int offset = 0; offset < size; offset++)
            {
                Marshal.WriteByte(block, offset, 0);
            }
        }
    }
}
