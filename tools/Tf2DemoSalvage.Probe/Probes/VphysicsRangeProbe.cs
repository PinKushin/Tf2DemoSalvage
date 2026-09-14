using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s default range manager — built by its own constructor <c>FUN_1800a0420</c> with the policy
/// vphysics passes, then asked through slot 2 (<c>FUN_1800a04e0</c>) and slot 1 (<c>FUN_1800a0560</c>) — called in process, the
/// oracle for <c>IvpRangeManager</c> (B369, D172).
/// </summary>
/// <remarks>
/// **Only the fields the two slots read are fabricated**: the environment's <c>+0x108</c>, each object's core at <c>+0xe8</c>,
/// and each core's <c>+0x4</c>, <c>+0x1dc</c> and <c>+0x254</c>. A tenth of the fields are drawn from NaNs with two payloads,
/// infinities, negative zero and the largest float, so the lanes see which operand each instruction keeps.
///
/// **Modes.** With no mode, or `sweep n`, random cases are compared lane by lane; `fixture path` writes the cases
/// `IvpRangeConformanceTests` reads.
/// </remarks>
public sealed class VphysicsRangeProbe : IProbe
{
    private const long ConstructAddress = 0x1800a0420;
    private const long ObjectAddress = 0x1800a04e0;
    private const long PairAddress = 0x1800a0560;

    private const int DefaultSweep = 100_000;
    private const int FixtureCases = 400;
    private const ulong FixtureSeed = 104200;
    private const ulong SweepSeed = 20260914;

    private static readonly float[] Specials =
        [float.NaN, BitConverter.Int32BitsToSingle(unchecked((int)0xffc00001)), float.PositiveInfinity, float.NegativeInfinity, -0f, float.MaxValue];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ConstructFunction(nint manager, nint environment, int policy);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double ObjectFunction(nint manager, nint body);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PairFunction(nint manager, nint first, nint second, nint firstRange, nint secondRange);

    /// <inheritdoc />
    public string Name => "vphysics-range";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's default range manager (slots FUN_1800a04e0 and FUN_1800a0560 of FUN_1800a0420's object) called in process " +
        "and compared with the port; 'fixture' writes the conformance suite's cases: vphysics-range [sweep n | fixture path]";

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

        if (arguments.Count >= 2 && arguments[0] == "fixture")
        {
            Draws draws = new(FixtureSeed);

            using StreamWriter writer = File.CreateText(path: arguments[1]);

            writer.WriteLine("# Written by the vphysics-range probe from the shipped vphysics.dll. Do not edit by hand.");

            for (int index = 0; index < FixtureCases; index++)
            {
                Dictionary<string, long[]> inputs = RandomCase(draws);

                IvpRangeReplay.Write(writer, new IvpReplayCase(index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
            }

            output.WriteLine($"{FixtureCases} cases written to {arguments[1]}");
            return;
        }

        Sweep(output, native, arguments.Count >= 2 && arguments[0] == "sweep"
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : DefaultSweep);
    }

    private static void Sweep(TextWriter output, Native native, int count)
    {
        Dictionary<string, long[]> control = Case(1d / 66d, (1f, 0f, 0f), (1f, 0f, 0f));
        Dictionary<string, long[]> controlOutputs = native.Run(control);

        // A resting unit sphere: MINSD(1e-20, 5) = 1e-20, MAXSD 0.5, less 1e-20/66, against 0.06·1e-20 + 1 — the surface wins.
        output.WriteLine(
            $"control: a resting unit sphere's range {BitConverter.Int64BitsToDouble(controlOutputs["object-first"][0]):R} (1 expected), " +
            $"the pair's {BitConverter.Int64BitsToDouble(controlOutputs["pair"][0]):R} and {BitConverter.Int64BitsToDouble(controlOutputs["pair"][1]):R}");

        Draws draws = new(SweepSeed);
        int differing = 0;

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(draws);
            IReadOnlyList<string> differences = IvpRangeReplay.Differences(native.Run(inputs), IvpRangeReplay.Run(inputs));

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

        output.WriteLine($"{count} range cases, {differing} differing");
    }

    private static Dictionary<string, long[]> RandomCase(Draws draws)
    {
        double step = draws.Chance(0.7) ? 1d / 66d : draws.Unit() * 0.1;

        return Case(
            draws.Chance(0.05) ? draws.Special() : step,
            (draws.Field(draws.Power(-2, 2)), draws.Speed(), draws.Speed()),
            (draws.Field(draws.Power(-2, 2)), draws.Speed(), draws.Speed()));
    }

    private static Dictionary<string, long[]> Case(double step, (float, float, float) first, (float, float, float) second) =>
        new(StringComparer.Ordinal)
        {
            ["step"] = [IvpImpactReplay.Lane(step)],
            ["first"] = [IvpImpactReplay.Lane(first.Item1), IvpImpactReplay.Lane(first.Item2), IvpImpactReplay.Lane(first.Item3)],
            ["second"] = [IvpImpactReplay.Lane(second.Item1), IvpImpactReplay.Lane(second.Item2), IvpImpactReplay.Lane(second.Item3)],
        };

    /// <summary>The probe's own random draws, split-mix seeded.</summary>
    private sealed class Draws(ulong seed)
    {
        private ulong _state = seed;

        public double Unit() => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref _state));

        public bool Chance(double probability) => Unit() < probability;

        public float Power(int least, int most) => (float)Math.Pow(10d, least + (Unit() * (most - least)));

        public float Special() => Specials[(int)(Unit() * Specials.Length)];

        public float Field(float value) => Chance(0.1) ? Special() : value;

        public float Speed() => Field(Chance(0.3) ? 0f : Power(-3, 3));
    }

    /// <summary>The binary's range manager, an environment, two objects and their cores, in unmanaged memory.</summary>
    private sealed class Native : IDisposable
    {
        private const int ManagerSize = 0x68;
        private const int EnvironmentSize = 0x200;
        private const int ObjectSize = 0x100;
        private const int CoreSize = 0x300;

        private readonly ObjectFunction _object;
        private readonly PairFunction _pair;
        private readonly nint _manager = Marshal.AllocHGlobal(ManagerSize);
        private readonly nint _environment = Marshal.AllocHGlobal(EnvironmentSize);
        private readonly nint _first = Marshal.AllocHGlobal(ObjectSize);
        private readonly nint _second = Marshal.AllocHGlobal(ObjectSize);
        private readonly nint _firstCore = Marshal.AllocHGlobal(CoreSize);
        private readonly nint _secondCore = Marshal.AllocHGlobal(CoreSize);
        private readonly nint _ranges = Marshal.AllocHGlobal(16);

        public Native(nint module)
        {
            _object = VphysicsLibrary.Function<ObjectFunction>(module, ObjectAddress);
            _pair = VphysicsLibrary.Function<PairFunction>(module, PairAddress);

            foreach ((nint block, int size) in new[] { (_environment, EnvironmentSize), (_first, ObjectSize), (_second, ObjectSize), (_firstCore, CoreSize), (_secondCore, CoreSize) })
            {
                for (int offset = 0; offset < size; offset++)
                {
                    Marshal.WriteByte(block, offset, 0);
                }
            }

            Marshal.WriteIntPtr(_first, 0xe8, _firstCore);
            Marshal.WriteIntPtr(_second, 0xe8, _secondCore);

            // vphysics' own construction passes the environment and policy 1 (FUN_180080d90 at 18008114f).
            VphysicsLibrary.Function<ConstructFunction>(module, ConstructAddress)(_manager, _environment, 1);
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            Marshal.WriteInt64(_environment, 0x108, inputs["step"][0]);
            Write(_firstCore, inputs["first"]);
            Write(_secondCore, inputs["second"]);

            long objectFirst = BitConverter.DoubleToInt64Bits(_object(_manager, _first));
            long objectSecond = BitConverter.DoubleToInt64Bits(_object(_manager, _second));

            _pair(_manager, _first, _second, _ranges, _ranges + 8);

            return new Dictionary<string, long[]>(StringComparer.Ordinal)
            {
                ["object-first"] = [objectFirst],
                ["object-second"] = [objectSecond],
                ["pair"] = [Marshal.ReadInt64(_ranges), Marshal.ReadInt64(_ranges, 8)],
            };
        }

        public void Dispose()
        {
            foreach (nint block in new[] { _manager, _environment, _first, _second, _firstCore, _secondCore, _ranges })
            {
                Marshal.FreeHGlobal(block);
            }
        }

        private static void Write(nint core, long[] lanes)
        {
            Marshal.WriteInt32(core, 0x4, unchecked((int)lanes[0]));
            Marshal.WriteInt32(core, 0x1dc, unchecked((int)lanes[1]));
            Marshal.WriteInt32(core, 0x254, unchecked((int)lanes[2]));
        }
    }
}
