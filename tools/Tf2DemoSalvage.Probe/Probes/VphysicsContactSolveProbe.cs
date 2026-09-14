using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s many-contact linear algebra — <c>FUN_1800aa2c0</c>, <c>FUN_1800a4d40</c>,
/// <c>FUN_1800a80a0</c>, <c>FUN_1800a7270</c> and the constraint solver <c>FUN_1800a5e60</c> — called in process on fabricated
/// systems, the oracle for <c>IvpLinearSystem</c> and <c>IvpComplementaritySolver</c> (B369, D172).
/// </summary>
/// <remarks>
/// **The binary is the instrument.** Each system is written at the offsets `docs/findings/51` reads; the solver's arena is a
/// megabyte of unmanaged memory at `+0x10`/`+0x18`, far more than a ten-contact case asks for, so the arena's overflow routine is
/// never reached.
///
/// **The control first**: the identity with right-hand side `(1, 2, −1)` scales to `(0.5, 1, −0.5)` and must be solved to pushes
/// `(0.5, 1, 0)` exactly, by the binary and by the port.
///
/// **Modes.** With no mode, or `sweep n`, random systems are compared lane by lane; `fixture path` writes the cases
/// `IvpComplementaritySolverConformanceTests` reads, drawn from a fixed seed.
/// </remarks>
public sealed class VphysicsContactSolveProbe : IProbe
{
    private const long EquilibrateAddress = 0x1800aa2c0;
    private const long GatherAddress = 0x1800a4d40;
    private const long EliminateAddress = 0x1800a80a0;
    private const long HoldsAddress = 0x1800a7270;
    private const long ComplementarityAddress = 0x1800a5e60;

    private const int DefaultSweep = 20_000;
    private const int FixtureCases = 160;
    private const ulong FixtureSeed = 18005;
    private const ulong SweepSeed = 20260913;
    private const int Contacts = IvpComplementarityReplay.MostContacts;
    private const ulong NearTwinsSeed = 18006;
    private const string RandomGenerator = "random";
    private const string NearTwinsGenerator = "near-twins";
    private const string PoisonedGenerator = "poisoned";
    private const ulong PoisonedSeed = 18007;
    private const string TiesGenerator = "ties";
    private const ulong TiesSeed = 18008;

    /// <summary>Quiet NaNs of both signs, one with a payload, signalling NaNs of both signs, and both infinities.</summary>
    private static readonly long[] Poisons =
    [
        0x7ff8000000000000,
        unchecked((long)0xfff8000000000000UL),
        0x7ff8000000000123,
        0x7ff0000000000001,
        unchecked((long)0xfff4000000000000UL),
        0x7ff0000000000000,
        unchecked((long)0xfff0000000000000UL),
    ];

    /// <summary>
    /// Cases from the sweeps' streams that a port broken on purpose got wrong while the random fixture did not notice, each
    /// found by <c>sweep n generator path</c> against that sabotage.
    /// </summary>
    private static readonly (string Generator, int Index)[] Killers =
    [
        (RandomGenerator, 19459), (PoisonedGenerator, 1746), (PoisonedGenerator, 5232),
        (PoisonedGenerator, 0), (PoisonedGenerator, 364), (PoisonedGenerator, 947),
        (TiesGenerator, 20), (TiesGenerator, 57), (TiesGenerator, 79),
        (RandomGenerator, 83), (TiesGenerator, 11), (PoisonedGenerator, 93),
        (RandomGenerator, 12837), (TiesGenerator, 188), (PoisonedGenerator, 225),
        (RandomGenerator, 4237), (PoisonedGenerator, 11), (PoisonedGenerator, 128), (PoisonedGenerator, 254),
        (PoisonedGenerator, 4098),
    ];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EquilibrateFunction(nint system);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GatherFunction(nint target, nint source, nint active, int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EliminateFunction(nint system);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HoldsFunction(nint system, int row);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ComplementarityFunction(nint solver, nint values, nint rightHandSide, nint result, int size, int warm, nint arena);

    /// <inheritdoc />
    public string Name => "vphysics-contact-solve";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's many-contact scaling, elimination, row test and constraint solver FUN_1800a5e60 called in process and " +
        "compared with IvpLinearSystem and IvpComplementaritySolver; 'sweep' can write the cases that differ, and 'fixture' " +
        "writes the conformance suite's cases: vphysics-contact-solve [sweep n [random|near-twins|poisoned|ties] [path] | fixture path]";

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

        bool sweeping = arguments.Count >= 2 && arguments[0] == "sweep";
        int count = sweeping ? int.Parse(arguments[1], CultureInfo.InvariantCulture) : DefaultSweep;
        string generator = sweeping && arguments.Count >= 3 ? arguments[2] : RandomGenerator;
        string? path = sweeping && arguments.Count >= 4 ? arguments[3] : null;

        if (generator is not (RandomGenerator or NearTwinsGenerator or PoisonedGenerator or TiesGenerator))
        {
            output.WriteLine($"No generator named '{generator}'.");
            return;
        }

        Sweep(output, native, count, generator, path);
    }

    private static bool Control(TextWriter output, Native native)
    {
        Dictionary<string, long[]> inputs = Empty();

        inputs["size"][0] = 3;
        inputs["matrix"][0] = IvpImpactReplay.Lane(1d);
        inputs["matrix"][4] = IvpImpactReplay.Lane(1d);
        inputs["matrix"][8] = IvpImpactReplay.Lane(1d);
        inputs["rhs"][0] = IvpImpactReplay.Lane(1d);
        inputs["rhs"][1] = IvpImpactReplay.Lane(2d);
        inputs["rhs"][2] = IvpImpactReplay.Lane(-1d);

        Dictionary<string, long[]> binary = native.Run(inputs);
        long[] expected = [IvpImpactReplay.Lane(0.5d), IvpImpactReplay.Lane(1d), 0L];
        bool held = binary["solved"][0] == 1 &&
            binary["result"][0] == expected[0] && binary["result"][1] == expected[1] && binary["result"][2] == expected[2];
        IReadOnlyList<string> differences = IvpComplementarityReplay.Differences(binary, IvpComplementarityReplay.Run(inputs));

        output.WriteLine($"control: the binary {(held ? "solved" : "DID NOT solve")} the identity; the port differs in {differences.Count} lanes");

        foreach (string difference in differences)
        {
            output.WriteLine($"  {difference}");
        }

        return held;
    }

    /// <summary>
    /// Compares a generator's stream with the binary. With <paramref name="path"/>, every differing case is also written there
    /// under the label <c>generator-index</c> — which, run against a port broken on purpose, is how the inputs in
    /// <see cref="Killers"/> were found.
    /// </summary>
    private static void Sweep(TextWriter output, Native native, int count, string generator, string? path)
    {
        ulong state = Seed(generator);
        int differing = 0;
        int solved = 0;
        using StreamWriter? writer = path is null ? null : File.CreateText(path);

        for (int index = 0; index < count; index++)
        {
            Dictionary<string, long[]> inputs = Draw(generator, ref state);
            Dictionary<string, long[]> binary = native.Run(inputs);
            IReadOnlyList<string> differences = IvpComplementarityReplay.Differences(binary, IvpComplementarityReplay.Run(inputs));

            solved += (int)binary["solved"][0];

            if (differences.Count == 0)
            {
                continue;
            }

            differing++;

            if (differing <= 5)
            {
                output.WriteLine($"{generator} {index}: {differences.Count} lanes differ, first {differences[0]}");
            }

            writer?.WriteLine();
            IvpComplementarityReplay.Write(
                writer ?? TextWriter.Null, new IvpReplayCase($"{generator}-{index.ToString(CultureInfo.InvariantCulture)}", inputs, binary));
        }

        output.WriteLine($"{count} {generator} systems, {solved} solved by the binary, {differing} differing");
    }

    private static ulong Seed(string generator) => generator switch
    {
        NearTwinsGenerator => NearTwinsSeed,
        PoisonedGenerator => PoisonedSeed,
        TiesGenerator => TiesSeed,
        _ => SweepSeed,
    };

    private static Dictionary<string, long[]> Draw(string generator, ref ulong state) => generator switch
    {
        NearTwinsGenerator => NearTwins(ref state),
        PoisonedGenerator => Poisoned(ref state),
        TiesGenerator => Ties(ref state),
        _ => RandomCase(ref state),
    };

    /// <summary>
    /// A heap whose contacts are interchangeable — <c>a·I + b·11ᵀ</c> over small whole or halved numbers, groups of equal
    /// right-hand sides — so steps tie within the solver's epsilon and which contact leaves is decided by its tie rule.
    /// </summary>
    private static Dictionary<string, long[]> Ties(ref ulong state)
    {
        int size = 2 + Below(ref state, Contacts - 1);
        double diagonal = (1 + Below(ref state, 4)) * 0.5;
        double shared = (Below(ref state, 5) - 1) * 0.5;
        int groups = 1 + Below(ref state, 3);
        double[] values = new double[size * size];
        double[] rhs = new double[size];
        double[] levels = new double[groups];

        for (int group = 0; group < groups; group++)
        {
            levels[group] = (1 + Below(ref state, 4)) * (Chance(ref state, 0.2) ? -0.5 : 0.5);
        }

        for (int i = 0; i < size; i++)
        {
            rhs[i] = levels[Below(ref state, groups)];

            for (int j = 0; j < size; j++)
            {
                values[i * size + j] = i == j ? diagonal + shared : shared;
            }
        }

        return System(size, Below(ref state, size + 1), [], values, rhs);
    }

    /// <summary>
    /// A random system with one to four of its values or right-hand side replaced by a NaN — quiet or signalling, either sign,
    /// with a payload or none — or an infinity, so NaNs meet NaNs of the other sign in every sum and product the solve makes.
    /// </summary>
    private static Dictionary<string, long[]> Poisoned(ref ulong state)
    {
        Dictionary<string, long[]> inputs = RandomCase(ref state);
        int size = (int)inputs["size"][0];
        int poisons = 1 + Below(ref state, 4);

        for (int poison = 0; poison < poisons; poison++)
        {
            long bits = Poisons[Below(ref state, Poisons.Length)];

            if (Chance(ref state, 0.7))
            {
                inputs["matrix"][Below(ref state, size * size)] = bits;
            }
            else
            {
                inputs["rhs"][Below(ref state, size)] = bits;
            }
        }

        return inputs;
    }

    private static void Fixture(TextWriter output, Native native, string path)
    {
        ulong state = FixtureSeed;

        using StreamWriter writer = File.CreateText(path);

        writer.WriteLine("# Written by the vphysics-contact-solve probe from the shipped vphysics.dll. Do not edit by hand.");

        for (int index = 0; index < FixtureCases; index++)
        {
            Dictionary<string, long[]> inputs = RandomCase(ref state);

            IvpComplementarityReplay.Write(writer, new IvpReplayCase(
                index.ToString(CultureInfo.InvariantCulture), inputs, native.Run(inputs)));
        }

        List<(string Label, Dictionary<string, long[]> Inputs)> targeted = Targeted();

        foreach ((string label, Dictionary<string, long[]> inputs) in targeted)
        {
            IvpComplementarityReplay.Write(writer, new IvpReplayCase(label, inputs, native.Run(inputs)));
        }

        foreach ((string generator, int wanted) in Killers)
        {
            ulong stream = Seed(generator);
            Dictionary<string, long[]> inputs = Draw(generator, ref stream);

            for (int index = 0; index < wanted; index++)
            {
                inputs = Draw(generator, ref stream);
            }

            IvpComplementarityReplay.Write(writer, new IvpReplayCase(
                $"{generator}-{wanted.ToString(CultureInfo.InvariantCulture)}", inputs, native.Run(inputs)));
        }

        output.WriteLine($"{FixtureCases} random, {targeted.Count} targeted and {Killers.Length} sabotage-found cases written to {path}");
    }

    /// <summary>
    /// The inputs a random draw almost never reaches, each named by the rule it pins: a vanished pivot whose residual falls
    /// either side of <c>1000·eps</c>; a row test within <c>(double)1e-5f</c>'s last digits; a symmetric heap whose active
    /// contacts tie on the step; and an identity warm-started whole, with two NaNs of different sign and payload in its first
    /// row, so the inverse's four-wide back substitution meets a NaN product against a NaN sum where the binary's operand
    /// order decides which survives.
    /// </summary>
    private static List<(string Label, Dictionary<string, long[]> Inputs)> Targeted()
    {
        List<(string Label, Dictionary<string, long[]> Inputs)> cases = [];

        foreach (double delta in new[] { 3e-8, 3e-7, 3e-6, 3e-5, 3e-4 })
        {
            foreach (int warm in new[] { 0, 2 })
            {
                cases.Add(($"leftover-{cases.Count}", System(2, warm, [0, 1], [1d, 1d, 1d, 1d], [1d, 1d + delta])));
            }
        }

        foreach (double share in new[] { 9.9999999e-6, 9.9999997e-6, 1e-5 })
        {
            foreach (double right in new[] { 0.25, 0.5, 0.75 })
            {
                cases.Add(($"slack-{cases.Count}", System(2, 0, [0], [1d, 0d, right - right * share, 1d], [1d, right])));
            }
        }

        foreach (int size in new[] { 2, 3, 4, 6 })
        {
            foreach ((double diagonal, double shared) in new[] { (1d, 1d), (2d, 0.5d) })
            {
                double[] values = new double[size * size];
                double[] rhs = new double[size];

                for (int i = 0; i < size; i++)
                {
                    rhs[i] = 1d;

                    for (int j = 0; j < size; j++)
                    {
                        values[i * size + j] = i == j ? diagonal + shared : shared;
                    }
                }

                cases.Add(($"symmetric-{cases.Count}", System(size, 0, [], values, rhs)));
            }
        }

        foreach ((int size, int later, int earlier, long laterBits, long earlierBits) in new[]
        {
            (10, 8, 5, 0x7ff8000000000008L, unchecked((long)0xfff8000000000005UL)),
            (10, 8, 5, unchecked((long)0xfff8000000000008UL), 0x7ff8000000000005L),
            (9, 7, 4, 0x7ff8000000000007L, unchecked((long)0xfff8000000000004UL)),
        })
        {
            double[] values = new double[size * size];
            double[] rhs = new double[size];

            for (int i = 0; i < size; i++)
            {
                values[i * size + i] = 1d;
                rhs[i] = 1d;
            }

            values[later] = BitConverter.Int64BitsToDouble(laterBits);
            values[earlier] = BitConverter.Int64BitsToDouble(earlierBits);
            cases.Add(($"nan-slot-{cases.Count}", System(size, size, [], values, rhs)));
        }

        return cases;
    }

    private static Dictionary<string, long[]> System(int size, int warm, int[] active, double[] values, double[] rhs)
    {
        Dictionary<string, long[]> inputs = Empty();

        inputs["size"][0] = size;
        inputs["warm"][0] = warm;
        inputs["gathered"][0] = active.Length;

        for (int i = 0; i < active.Length; i++)
        {
            inputs["active"][i] = active[i];
        }

        for (int i = 0; i < values.Length; i++)
        {
            inputs["matrix"][i] = IvpImpactReplay.Lane(values[i]);
        }

        for (int i = 0; i < rhs.Length; i++)
        {
            inputs["rhs"][i] = IvpImpactReplay.Lane(rhs[i]);
        }

        return inputs;
    }

    /// <summary>
    /// Contacts in nearly identical pairs — a Gram row and the same row moved by <c>1e-5</c> to <c>1e-8</c> — each pushing on its
    /// own, so an active block that holds both of a pair is too nearly singular for the running inverse. Exact twins do not do
    /// it: the second always settles, its residual already zero.
    /// </summary>
    private static Dictionary<string, long[]> NearTwins(ref ulong state)
    {
        int pairs = 2 + Below(ref state, Contacts / 2 - 1);
        int size = pairs * 2;
        int lanes = pairs + Below(ref state, pairs + 1);
        double apart = Math.Pow(10d, -5 - Below(ref state, 4));
        double[][] rows = new double[size][];
        double[] rhs = new double[size];

        for (int pair = 0; pair < pairs; pair++)
        {
            double[] row = new double[lanes];
            double[] twin = new double[lanes];

            for (int k = 0; k < lanes; k++)
            {
                row[k] = Signed(ref state);
                twin[k] = row[k] + Signed(ref state) * apart;
            }

            rows[pair * 2] = row;
            rows[pair * 2 + 1] = twin;
            rhs[pair * 2] = 0.5 + VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state)) * 0.5;
            rhs[pair * 2 + 1] = 0.5 + VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state)) * 0.5;
        }

        double[] values = new double[size * size];

        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                double sum = 0d;

                for (int k = 0; k < lanes; k++)
                {
                    sum += rows[i][k] * rows[j][k];
                }

                values[i * size + j] = sum;
            }
        }

        return System(size, Below(ref state, size + 1), [], values, rhs);
    }

    private static Dictionary<string, long[]> Empty()
    {
        Dictionary<string, long[]> inputs = new(StringComparer.Ordinal);

        foreach (IvpReplayField field in IvpComplementarityReplay.Inputs)
        {
            inputs[field.Name] = new long[field.Count];
        }

        return inputs;
    }

    /// <summary>
    /// A system shaped like a heap's — a Gram matrix of short random rows, often rank-deficient — or a general, an integer-valued,
    /// a nearly vanishing or a poisoned one, with a random warm count and gathered set.
    /// </summary>
    private static Dictionary<string, long[]> RandomCase(ref ulong state)
    {
        Dictionary<string, long[]> inputs = Empty();
        int size = 1 + Below(ref state, Contacts);
        double[] values = new double[size * size];
        double[] rhs = new double[size];
        double magnitude = Math.Pow(10d, Below(ref state, 9) - 4);

        switch (Below(ref state, 6))
        {
            case 3:
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = Signed(ref state);
                }

                break;

            case 4:
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = Below(ref state, 4) - 1;
                }

                break;

            default:
                Gram(ref state, values, size);
                break;
        }

        if (Chance(ref state, 0.15))
        {
            magnitude = 1e-12;
        }

        for (int i = 0; i < values.Length; i++)
        {
            values[i] *= magnitude;
        }

        int sign = Below(ref state, 3);

        for (int i = 0; i < size; i++)
        {
            double draw = Signed(ref state);

            if (sign == 1)
            {
                draw = Math.Abs(draw);
            }
            else if (sign == 2)
            {
                draw = -Math.Abs(draw);
            }

            rhs[i] = draw * magnitude;
        }

        if (Chance(ref state, 0.03))
        {
            values[Below(ref state, values.Length)] = double.NaN;
        }

        if (Chance(ref state, 0.03))
        {
            rhs[Below(ref state, size)] = Chance(ref state, 0.5) ? double.NaN : double.PositiveInfinity;
        }

        inputs["size"][0] = size;
        inputs["warm"][0] = Below(ref state, size + 1);

        for (int i = 0; i < values.Length; i++)
        {
            inputs["matrix"][i] = IvpImpactReplay.Lane(values[i]);
        }

        for (int i = 0; i < size; i++)
        {
            inputs["rhs"][i] = IvpImpactReplay.Lane(rhs[i]);
        }

        int gathered = Below(ref state, size + 1);
        List<int> indices = [];

        for (int i = 0; i < size; i++)
        {
            indices.Add(i);
        }

        if (!Chance(ref state, 0.4))
        {
            for (int i = size - 1; i > 0; i--)
            {
                int j = Below(ref state, i + 1);

                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            indices.RemoveRange(gathered, size - gathered);
            indices.Sort();
        }

        inputs["gathered"][0] = gathered;

        for (int i = 0; i < gathered; i++)
        {
            inputs["active"][i] = indices[i];
        }

        return inputs;
    }

    /// <summary>Rows of up to six random lanes, some repeated, and their dot products — with a little on the diagonal, or none.</summary>
    private static void Gram(ref ulong state, double[] values, int size)
    {
        int lanes = 1 + Below(ref state, 6);
        double[][] rows = new double[size][];

        for (int i = 0; i < size; i++)
        {
            rows[i] = new double[lanes];

            if (i > 0 && Chance(ref state, 0.15))
            {
                Array.Copy(rows[Below(ref state, i)], rows[i], lanes);
                continue;
            }

            for (int k = 0; k < lanes; k++)
            {
                rows[i][k] = Signed(ref state);
            }
        }

        double jitter = Chance(ref state, 0.5) ? 0d : VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state)) * 0.01;

        for (int i = 0; i < size; i++)
        {
            for (int j = 0; j < size; j++)
            {
                double sum = 0d;

                for (int k = 0; k < lanes; k++)
                {
                    sum += rows[i][k] * rows[j][k];
                }

                values[i * size + j] = i == j ? sum + jitter : sum;
            }
        }
    }

    private static int Below(ref ulong state, int bound) => (int)(VphysicsLibrary.SplitMix(ref state) % (ulong)bound);

    private static bool Chance(ref ulong state, double probability) => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state)) < probability;

    private static double Signed(ref ulong state) => VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state)) * 2d - 1d;

    /// <summary>The systems, the sub-system, the solver and its arena, in unmanaged memory at the engine's offsets.</summary>
    private sealed class Native : IDisposable
    {
        private const int SystemSize = 0x30;
        private const int SolverSize = 0x200;
        private const int ArenaSize = 0x28;
        private const int ArenaBytes = 1 << 20;
        private const int Lanes = Contacts * Contacts;

        private readonly List<nint> _blocks = [];
        private readonly EquilibrateFunction _equilibrate;
        private readonly GatherFunction _gather;
        private readonly EliminateFunction _eliminate;
        private readonly HoldsFunction _holds;
        private readonly ComplementarityFunction _complementarity;

        private readonly nint _system;
        private readonly nint _values;
        private readonly nint _rhs;
        private readonly nint _result;
        private readonly nint _gathered;
        private readonly nint _gatheredValues;
        private readonly nint _gatheredRhs;
        private readonly nint _gatheredResult;
        private readonly nint _active;
        private readonly nint _solver;
        private readonly nint _arena;
        private readonly nint _arenaBuffer;
        private readonly nint _pushes;

        public Native(nint module)
        {
            _equilibrate = VphysicsLibrary.Function<EquilibrateFunction>(module, EquilibrateAddress);
            _gather = VphysicsLibrary.Function<GatherFunction>(module, GatherAddress);
            _eliminate = VphysicsLibrary.Function<EliminateFunction>(module, EliminateAddress);
            _holds = VphysicsLibrary.Function<HoldsFunction>(module, HoldsAddress);
            _complementarity = VphysicsLibrary.Function<ComplementarityFunction>(module, ComplementarityAddress);

            _system = Allocate(SystemSize);
            _values = Allocate(Lanes * 8);
            _rhs = Allocate(Contacts * 8);
            _result = Allocate(Contacts * 8);
            _gathered = Allocate(SystemSize);
            _gatheredValues = Allocate(Lanes * 8);
            _gatheredRhs = Allocate(Contacts * 8);
            _gatheredResult = Allocate(Contacts * 8);
            _active = Allocate(Contacts * 4);
            _solver = Allocate(SolverSize);
            _arena = Allocate(ArenaSize);
            _arenaBuffer = Allocate(ArenaBytes);
            _pushes = Allocate(Contacts * 8);
        }

        public Dictionary<string, long[]> Run(Dictionary<string, long[]> inputs)
        {
            int size = IvpImpactReplay.Whole32(inputs, "size", 0);
            int warm = IvpImpactReplay.Whole32(inputs, "warm", 0);
            int gathered = IvpImpactReplay.Whole32(inputs, "gathered", 0);

            Zero(_system, SystemSize);
            Zero(_values, Lanes * 8);
            Zero(_rhs, Contacts * 8);
            Zero(_result, Contacts * 8);
            Zero(_gathered, SystemSize);
            Zero(_gatheredValues, Lanes * 8);
            Zero(_gatheredRhs, Contacts * 8);
            Zero(_gatheredResult, Contacts * 8);
            Zero(_active, Contacts * 4);
            Zero(_solver, SolverSize);
            Zero(_arena, ArenaSize);
            Zero(_pushes, Contacts * 8);

            Header(_system, size, _values, _rhs, _result);

            for (int i = 0; i < size * size; i++)
            {
                Marshal.WriteInt64(_values, i * 8, inputs["matrix"][i]);
            }

            for (int i = 0; i < size; i++)
            {
                Marshal.WriteInt64(_rhs, i * 8, inputs["rhs"][i]);
            }

            _equilibrate(_system);

            long scale = Marshal.ReadInt64(_system, 0x28);
            long[] equilibrated = Read64(_values, Lanes);
            long[] equilibratedRight = Read64(_rhs, Contacts);

            Header(_gathered, gathered, _gatheredValues, _gatheredRhs, _gatheredResult);

            for (int i = 0; i < gathered; i++)
            {
                Marshal.WriteInt32(_active, i * 4, (int)inputs["active"][i]);
            }

            _gather(_gathered, _system, _active, gathered);

            int gatheredSolved = _eliminate(_gathered);

            for (int i = 0; i < gathered; i++)
            {
                Marshal.WriteInt64(_result, (int)inputs["active"][i] * 8, Marshal.ReadInt64(_gatheredResult, i * 8));
            }

            long[] holds = new long[Contacts];

            for (int i = 0; i < size; i++)
            {
                holds[i] = _holds(_system, i);
            }

            Marshal.WriteIntPtr(_arena, 0x10, _arenaBuffer);
            Marshal.WriteIntPtr(_arena, 0x18, _arenaBuffer + ArenaBytes);

            int solved = _complementarity(_solver, _values, _rhs, _pushes, size, warm, _arena);
            nint residual = Marshal.ReadIntPtr(_solver, 0x48);
            nint order = Marshal.ReadIntPtr(_solver, 0x68);
            long[] residualLanes = new long[Contacts];
            long[] orderLanes = new long[Contacts];

            for (int i = 0; i < size; i++)
            {
                residualLanes[i] = Marshal.ReadInt64(residual, i * 8);
                orderLanes[i] = Marshal.ReadInt32(order, i * 4);
            }

            return new Dictionary<string, long[]>(StringComparer.Ordinal)
            {
                ["scale"] = [scale],
                ["equilibrated"] = equilibrated,
                ["equilibrated-rhs"] = equilibratedRight,
                ["gathered-solved"] = [gatheredSolved],
                ["gathered-values"] = Read64(_gatheredValues, Lanes),
                ["gathered-rhs"] = Read64(_gatheredRhs, Contacts),
                ["gathered-result"] = Read64(_gatheredResult, Contacts),
                ["holds"] = holds,
                ["solved"] = [solved],
                ["result"] = Read64(_pushes, Contacts),
                ["residual"] = residualLanes,
                ["order"] = orderLanes,
                ["state"] =
                [
                    Marshal.ReadInt32(_solver, 0x80), Marshal.ReadInt32(_solver, 0x88), Marshal.ReadInt32(_solver, 0xa4),
                    Marshal.ReadInt32(_solver, 0xf4), Marshal.ReadInt32(_solver, 0x90), Marshal.ReadInt32(_solver, 0x94),
                    Marshal.ReadInt32(_solver, 0x98), Marshal.ReadInt32(_solver, 0x9c), Marshal.ReadInt32(_solver, 0xa0),
                ],
            };
        }

        public void Dispose()
        {
            foreach (nint block in _blocks)
            {
                Marshal.FreeHGlobal(block);
            }

            _blocks.Clear();
        }

        /// <summary><c>FUN_1800a4600</c>'s fields, then the counts and the three arrays.</summary>
        private static void Header(nint system, int size, nint values, nint rhs, nint result)
        {
            Marshal.WriteInt64(system, 0, BitConverter.DoubleToInt64Bits(1e-9));
            Marshal.WriteInt32(system, 0x8, size);
            Marshal.WriteInt32(system, 0xc, size);
            Marshal.WriteIntPtr(system, 0x10, values);
            Marshal.WriteIntPtr(system, 0x18, rhs);
            Marshal.WriteIntPtr(system, 0x20, result);
        }

        private static long[] Read64(nint block, int count)
        {
            long[] lanes = new long[count];

            for (int i = 0; i < count; i++)
            {
                lanes[i] = Marshal.ReadInt64(block, i * 8);
            }

            return lanes;
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
