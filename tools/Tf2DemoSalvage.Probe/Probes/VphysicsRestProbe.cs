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

        List<(string Label, Dictionary<string, long[]> Inputs)> killers = Killers(draws);

        foreach ((string label, Dictionary<string, long[]> inputs) in killers)
        {
            IvpRestReplay.Write(writer, new IvpReplayCase(label, inputs, native.Run(inputs)));
        }

        output.WriteLine(
            $"{FixtureCases} random and {killers.Count} searched cases written to {path}: " +
            string.Join(", ", killers.GroupBy(killer => killer.Label[..killer.Label.LastIndexOf('-')]).Select(group => $"{group.Key} {group.Count()}")));
    }

    /// <summary>
    /// Cases random draws almost never reach, searched for: an elapsed time a quarter of a float step past the delay, which only
    /// the narrowing refuses; turns straddling the threshold between the dot's two groupings and between which orientation is
    /// narrowed; and spins straddling the limit between the squares' two groupings.
    /// </summary>
    private static List<(string Label, Dictionary<string, long[]> Inputs)> Killers(Draws draws)
    {
        const int Wanted = 4;
        const int Trials = 2_000_000;
        double turnLimit = BitConverter.Int64BitsToDouble(0x3efa36e2d7731900);
        List<(string Label, Dictionary<string, long[]> Inputs)> cases = [];

        for (int found = 0, trial = 0; found < Wanted && trial < Trials; trial++)
        {
            Dictionary<string, long[]> inputs = OnAnchors(draws, draws.Rotation());
            float delay = BitConverter.Int32BitsToSingle(unchecked((int)inputs["rest-delay"][0]));
            double now = BitConverter.Int64BitsToDouble(inputs["now"][0]);
            double anchored = now - (delay + ((MathF.BitIncrement(delay) - (double)delay) / 4d));
            double elapsed = now - anchored;

            if (BitConverter.SingleToInt32Bits((float)elapsed) == BitConverter.SingleToInt32Bits(delay) && elapsed > delay)
            {
                inputs["anchor-time"] = [IvpImpactReplay.Lane(anchored)];
                cases.Add(($"delay-{found++}", inputs));
            }
        }

        for (int found = 0, trial = 0; found < Wanted && trial < Trials; trial++)
        {
            (double X, double Y, double Z, double W) working = draws.Rotation();
            (float X, float Y, float Z, float W) anchor = Narrow(Turned(draws, working, turnLimit, out _));
            float? radius = Straddle(Gap(Dot(anchor, working, regrouped: false)), Gap(Dot(anchor, working, regrouped: true)), turnLimit);

            if (radius is float found32)
            {
                Dictionary<string, long[]> inputs = OnAnchors(draws, working);

                inputs["radius"] = [IvpImpactReplay.Lane(found32)];
                inputs["anchor-orientation"] = Floats((anchor.X, anchor.Y, anchor.Z, anchor.W));
                cases.Add(($"turn-grouping-{found++}", inputs));
            }
        }

        for (int found = 0, trial = 0; found < Wanted && trial < Trials; trial++)
        {
            (double X, double Y, double Z, double W) working = draws.Rotation();
            (double X, double Y, double Z, double W) previous = Turned(draws, working, turnLimit, out _);
            float? radius = Straddle(
                Gap(Dot(Narrow(previous), working, regrouped: false)), Gap(Dot(Narrow(working), previous, regrouped: false)), turnLimit);

            if (radius is float found32)
            {
                Dictionary<string, long[]> inputs = OnAnchors(draws, working);
                float limit = (float)(2.356194490192345d / BitConverter.Int32BitsToSingle(unchecked((int)inputs["rest-delay"][0])));

                inputs["radius"] = [IvpImpactReplay.Lane(found32)];
                inputs["orientation"] = Doubles(previous);
                inputs["spin"] = [IvpImpactReplay.Lane(limit * 2f), IvpImpactReplay.Lane(limit * 2f), IvpImpactReplay.Lane(limit * 2f)];
                cases.Add(($"turn-narrowing-{found++}", inputs));
            }
        }

        for (int found = 0, trial = 0; found < Wanted && trial < Trials; trial++)
        {
            Dictionary<string, long[]> inputs = OnAnchors(draws, draws.Rotation());
            float limit = (float)(2.356194490192345d / BitConverter.Int32BitsToSingle(unchecked((int)inputs["rest-delay"][0])));
            float square = limit * limit;
            (double X, double Y, double Z, double W) direction = draws.Rotation();
            double norm = Math.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y) + (direction.Z * direction.Z));
            double size = Math.Sqrt(square) * (1d + (draws.Signed() * 3e-7)) / norm;
            (float x, float y, float z) = ((float)(direction.X * size), (float)(direction.Y * size), (float)(direction.Z * size));
            float grouped = ((x * x) + (y * y)) + (z * z);
            float regrouped = (x * x) + ((y * y) + (z * z));

            if (grouped > square != regrouped > square)
            {
                inputs["orientation"] = Doubles(draws.Rotation());
                inputs["spin"] = [IvpImpactReplay.Lane(x), IvpImpactReplay.Lane(y), IvpImpactReplay.Lane(z)];
                cases.Add(($"spin-grouping-{found++}", inputs));
            }
        }

        return cases;
    }

    /// <summary>A core sitting exactly on both anchors, still for ten seconds, with a delay from 0.1 to 2 and no spin.</summary>
    private static Dictionary<string, long[]> OnAnchors(Draws draws, (double X, double Y, double Z, double W) working)
    {
        Dictionary<string, long[]> inputs = RandomCase(draws, poison: false);
        double now = BitConverter.Int64BitsToDouble(inputs["now"][0]);
        (float X, float Y, float Z) position = ((float)(draws.Signed() * 100d), (float)(draws.Signed() * 100d), (float)(draws.Signed() * 100d));
        long[] floats = [IvpImpactReplay.Lane(position.X), IvpImpactReplay.Lane(position.Y), IvpImpactReplay.Lane(position.Z)];

        inputs["rest-delay"] = [IvpImpactReplay.Lane(0.1f + ((float)draws.Unit() * 1.9f))];
        inputs["position"] = [IvpImpactReplay.Lane((double)position.X), IvpImpactReplay.Lane((double)position.Y), IvpImpactReplay.Lane((double)position.Z)];
        inputs["orientation"] = Doubles(working);
        inputs["working-orientation"] = Doubles(working);
        inputs["spin"] = [0L, 0L, 0L];
        inputs["anchor-time"] = [IvpImpactReplay.Lane(now - 10d)];
        inputs["anchor-orientation"] = Floats(working);
        inputs["anchor-position"] = floats;
        inputs["settle-time"] = [IvpImpactReplay.Lane(now - 10d)];
        inputs["settle-orientation"] = Floats(working);
        inputs["settle-position"] = [.. floats];

        return inputs;
    }

    /// <summary>A rotation turned from another so its gap sits at the threshold for a radius drawn from 0.05 to 0.95.</summary>
    private static (double X, double Y, double Z, double W) Turned(
        Draws draws, (double X, double Y, double Z, double W) rotation, double limit, out double radius)
    {
        (double X, double Y, double Z, double W) other = draws.Rotation();
        double along = (other.X * rotation.X) + (other.Y * rotation.Y) + (other.Z * rotation.Z) + (other.W * rotation.W);
        (double x, double y, double z, double w) = (
            other.X - (along * rotation.X), other.Y - (along * rotation.Y), other.Z - (along * rotation.Z), other.W - (along * rotation.W));
        double length = Math.Sqrt((x * x) + (y * y) + (z * z) + (w * w));

        radius = 0.05d + (draws.Unit() * 0.9d);

        double angle = Math.Acos(Math.Sqrt(1d - (limit / (2d * radius * radius))));
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle) / length;

        return (
            (cosine * rotation.X) + (sine * x), (cosine * rotation.Y) + (sine * y),
            (cosine * rotation.Z) + (sine * z), (cosine * rotation.W) + (sine * w));
    }

    /// <summary>The rest test's dot, <c>(w + z) + (y + x)</c> as the binary groups it or <c>((w + z) + y) + x</c>.</summary>
    private static double Dot((float X, float Y, float Z, float W) anchor, (double X, double Y, double Z, double W) rotation, bool regrouped)
    {
        double w = anchor.W * rotation.W;
        double z = anchor.Z * rotation.Z;
        double y = anchor.Y * rotation.Y;
        double x = anchor.X * rotation.X;

        return regrouped ? ((w + z) + y) + x : (w + z) + (y + x);
    }

    private static double Gap(double dot) => 1d - (dot * dot);

    /// <summary>A float radius near the one where the two gaps' turns fall either side of the threshold, or null when none does.</summary>
    private static float? Straddle(double first, double second, double limit)
    {
        if (BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second) || first + second <= 0d)
        {
            return null;
        }

        float radius = (float)Math.Sqrt(limit / (first + second));

        for (int step = 0; step < 4; step++)
        {
            radius = MathF.BitDecrement(radius);
        }

        for (int step = 0; step < 8; step++, radius = MathF.BitIncrement(radius))
        {
            if ((first + first) * radius * radius > limit != (second + second) * radius * radius > limit)
            {
                return radius;
            }
        }

        return null;
    }

    private static (float X, float Y, float Z, float W) Narrow((double X, double Y, double Z, double W) rotation) =>
        ((float)rotation.X, (float)rotation.Y, (float)rotation.Z, (float)rotation.W);

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
