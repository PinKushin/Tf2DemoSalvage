using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Animation.Animating;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vphysics.dll</c>'s own runtime-library math, called in process — the oracle for <see cref="IvpMath"/>.
/// </summary>
/// <remarks>
/// **A differential instrument, not a reading.** `docs/findings/51` identifies `FUN_1800d40f0` as `expf`, `FUN_1800d3cf0` as
/// `exp`, `FUN_1800d4f9c` as `asinf` and `FUN_1800d4398` as `atan`, and the first two each carry a plain path and a
/// fused-multiply-add path chosen at run time by `DAT_180136418`. This loads the game's x64 `vphysics.dll`, calls the four at
/// their addresses, and prints every result's bits on each path — the flag written in the loaded image, and the fused path
/// taken only where the library's own start-up chose it, because forcing it on a processor without FMA would fault.
///
/// **`sweep` compares <see cref="IvpMath"/> with the binary** over millions of arguments on each path, and names arguments
/// where the binary's two paths themselves disagree, so a test can be written that only the right path passes.
///
/// **Controls first.** `expf(0)` and `exp(0)` must come back exactly `1`, `asinf(1)` exactly `π/2f` and `atan(1)` exactly
/// the double `π/4`; if any does not, the addresses do not fit this build and nothing after them is printed.
/// </remarks>
public sealed class VphysicsMathProbe : IProbe
{
    /// <summary><c>FUN_1800d40f0</c>, <c>expf</c>.</summary>
    private const long ExpfAddress = 0x1800d40f0;

    /// <summary><c>FUN_1800d3cf0</c>, <c>exp</c>.</summary>
    private const long ExpAddress = 0x1800d3cf0;

    /// <summary><c>FUN_1800d4f9c</c>, <c>asinf</c>.</summary>
    private const long AsinfAddress = 0x1800d4f9c;

    /// <summary><c>FUN_1800d4398</c>, <c>atan</c>.</summary>
    private const long AtanAddress = 0x1800d4398;

    /// <summary><c>FUN_1800d33b0</c>, <c>cos</c>.</summary>
    private const long CosAddress = 0x1800d33b0;

    /// <summary><c>FUN_1800c8020</c>, <c>sin</c>.</summary>
    private const long SinAddress = 0x1800c8020;

    /// <summary><c>FUN_1800cce64</c>, <c>acos</c>.</summary>
    private const long AcosAddress = 0x1800cce64;

    /// <summary><c>DAT_180136418</c>, the fused-path flag <c>__acrt_initialize_fma3</c> writes.</summary>
    private const long FusedFlagAddress = 0x180136418;

    /// <summary><c>FUN_180077a20</c>, the damping applier.</summary>
    private const long DampAddress = 0x180077a20;

    /// <summary>A stand-in core, larger than every offset the damping applier touches.</summary>
    private const int CoreSize = 0x200;

    /// <summary>The double <c>π/4</c>, which <c>atan(1)</c> answers exactly.</summary>
    private const long QuarterPiBits = 0x3FE921FB54442D18;

    /// <summary>The float <c>π/2</c>, which <c>asinf(1)</c> answers exactly.</summary>
    private const int HalfPiBits = 0x3fc90fdb;

    /// <summary>How many arguments each sweep draws per family.</summary>
    private const int SweepCount = 1_000_000;

    /// <summary>The stride through every float's bit pattern — a prime, so it lands in every exponent.</summary>
    private const long BitStride = 9973;

    private static readonly float[] SingleInputs =
        [-0f, -1e-3f, -0.03125f, -0.1f, -0.2f, -0.25f, -0.5f, -0.7071068f, -1f, -2.5f, -10f, 0.3f, 1f, 5f];

    private static readonly double[] DoubleInputs =
        [-1e-9, -1e-3, -0.015, -0.2, -0.25, -0.6, -1d, -1.5, -7.5, 0.5, 3d, -700d];

    private static readonly float[] SineInputs = [1e-5f, 0.1f, 0.3f, 0.4999f, 0.5f, 0.6f, 0.75f, 0.9f, 0.999f, -0.4f];

    private static readonly double[] TangentInputs = [1e-3, 0.3, 0.4375, 0.5, 0.6875, 0.8, 1.1875, 1.5, 2.4375, 3d, 10d, -0.7];

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float SingleFunction(float x);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate double DoubleFunction(double x);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DampFunction(nint core, double delta, nint rotation, double speedDamping);

    /// <inheritdoc />
    public string Name => "vphysics-math";

    /// <inheritdoc />
    public string Summary =>
        "vphysics.dll's own expf, exp, asinf, atan, cos, sin and acos, called in process on both runtime paths, as bits; " +
        "'sweep' compares IvpMath with them: vphysics-math [sweep | x ...]";

    /// <inheritdoc />
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!VphysicsLibrary.TryLoad(output, out nint module))
        {
            return;
        }

        Library math = new(
            VphysicsLibrary.Function<SingleFunction>(module, ExpfAddress),
            VphysicsLibrary.Function<DoubleFunction>(module, ExpAddress),
            VphysicsLibrary.Function<SingleFunction>(module, AsinfAddress),
            VphysicsLibrary.Function<DoubleFunction>(module, AtanAddress),
            VphysicsLibrary.Function<DoubleFunction>(module, CosAddress),
            VphysicsLibrary.Function<DoubleFunction>(module, SinAddress),
            VphysicsLibrary.Function<DoubleFunction>(module, AcosAddress),
            VphysicsLibrary.Address(module, FusedFlagAddress));

        int loaded = Marshal.ReadInt32(math.Flag);

        output.WriteLine($"The fused-path flag reads {loaded}; IvpMath.FusedPath is {IvpMath.FusedPath}");

        if (!Controls(output, math))
        {
            return;
        }

        int[] paths = loaded != 0 ? [0, 1] : [0];

        if (arguments.Count > 0 && arguments[0] == "sweep")
        {
            Sweep(output, math, paths);
        }
        else if (arguments.Count > 0 && arguments[0] == "damping")
        {
            Damping(output, module);
        }
        else if (arguments.Count > 2 && arguments[0] == "call")
        {
            // Any double(double) routine at an address, on each argument: how an unnamed runtime routine is identified.
            DoubleFunction routine = VphysicsLibrary.Function<DoubleFunction>(
                module, long.Parse(arguments[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));

            for (int index = 2; index < arguments.Count; index++)
            {
                double x = double.Parse(arguments[index], CultureInfo.InvariantCulture);
                output.WriteLine($"{arguments[1]}({Bits(x)}) -> {Bits(routine(x))}");
            }
        }
        else
        {
            Listing(output, math, paths, arguments);
        }

        Marshal.WriteInt32(math.Flag, loaded);
    }

    /// <summary>
    /// <c>FUN_180077a20(core, dt, rotation, speedDamping)</c> on a zeroed stand-in core: it reads the three rotation factors
    /// through its pointer and writes only the core's angular velocity at <c>+0x130</c> and velocity at <c>+0x140</c>.
    /// </summary>
    private static void Damping(TextWriter output, nint module)
    {
        DampFunction damp = VphysicsLibrary.Function<DampFunction>(module, DampAddress);

        (float Rotation, float Speed, float Step)[] cases =
        [
            (4f, 0f, 1f / 66f), (30f, 30f, 1f / 66f), (16f, 0.1f, 0.015f), (0f, 0f, 0.015f),
            (27.2f, 16.6f, 0.015f), (27.3f, 16.7f, 0.015f), (4f, 4f, 0.015f),

            // Found by search: factors whose expf lane and exp lane damp a spin of 10 to different bits.
            (27.6396427f, 0f, 0.015f), (27.7169571f, 0f, 0.015f),
        ];

        nint core = Marshal.AllocHGlobal(CoreSize);
        nint rotation = Marshal.AllocHGlobal(12);

        try
        {
            foreach ((float factor, float speed, float step) in cases)
            {
                for (int offset = 0; offset < CoreSize; offset++)
                {
                    Marshal.WriteByte(core, offset, 0);
                }

                WriteSingle(core, 0x130, 10f);
                WriteSingle(core, 0x134, 20f);
                WriteSingle(core, 0x138, 30f);
                WriteSingle(core, 0x140, 100f);
                WriteSingle(core, 0x144, 200f);
                WriteSingle(core, 0x148, 300f);
                WriteSingle(rotation, 0, factor);
                WriteSingle(rotation, 4, factor);
                WriteSingle(rotation, 8, factor);

                damp(core, step, rotation, speed);

                output.WriteLine(
                    $"damp rotation {Bits(factor)} speed {Bits(speed)} step {Bits(step)}: " +
                    $"spin 0x{Marshal.ReadInt32(core, 0x130):x8} 0x{Marshal.ReadInt32(core, 0x134):x8} 0x{Marshal.ReadInt32(core, 0x138):x8}; " +
                    $"velocity 0x{Marshal.ReadInt32(core, 0x140):x8} 0x{Marshal.ReadInt32(core, 0x144):x8} 0x{Marshal.ReadInt32(core, 0x148):x8}");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(core);
            Marshal.FreeHGlobal(rotation);
        }
    }

    private static void WriteSingle(nint address, int offset, float value) =>
        Marshal.WriteInt32(address, offset, BitConverter.SingleToInt32Bits(value));

    private static void Listing(TextWriter output, Library math, int[] paths, IReadOnlyList<string> arguments)
    {
        List<double> custom = [];

        foreach (string argument in arguments)
        {
            custom.Add(double.Parse(argument, CultureInfo.InvariantCulture));
        }

        foreach (int path in paths)
        {
            Marshal.WriteInt32(math.Flag, path);
            string label = Label(path);

            foreach (float x in custom.Count > 0 ? custom.ConvertAll(value => (float)value) : [.. SingleInputs])
            {
                output.WriteLine($"expf  {label}  {Bits(x)}  ->  {Bits(math.Expf(x))}");
            }

            foreach (double x in custom.Count > 0 ? custom : [.. DoubleInputs])
            {
                output.WriteLine($"exp   {label}  {Bits(x)}  ->  {Bits(math.Exp(x))}");
                output.WriteLine($"cos   {label}  {Bits(x)}  ->  {Bits(math.Cos(x))}");
                output.WriteLine($"sin   {label}  {Bits(x)}  ->  {Bits(math.Sin(x))}");
                output.WriteLine($"acos  {label}  {Bits(x)}  ->  {Bits(math.Acos(x))}");
            }
        }

        foreach (float x in custom.Count > 0 ? custom.ConvertAll(value => (float)value) : [.. SineInputs])
        {
            output.WriteLine($"asinf  {Bits(x)}  ->  {Bits(math.Asinf(x))}");
        }

        foreach (double x in custom.Count > 0 ? custom : [.. TangentInputs])
        {
            output.WriteLine($"atan  {Bits(x)}  ->  {Bits(math.Atan(x))}");
        }
    }

    private static void Sweep(TextWriter output, Library math, int[] paths)
    {
        float[] singles = SingleArguments();
        double[] doubles = DoubleArguments();
        float[] sines = SineArguments();
        double[] tangents = TangentArguments();
        double[] cosines = CosineArguments();
        double[] circular = CircularArguments();
        double[] arcs = ArcArguments();

        int[][] singleAnswers = new int[paths.Length][];
        long[][] doubleAnswers = new long[paths.Length][];
        long[][] cosineAnswers = new long[paths.Length][];
        long[][] sineAnswers = new long[paths.Length][];

        foreach (int path in paths)
        {
            Marshal.WriteInt32(math.Flag, path);
            bool fused = path != 0;

            singleAnswers[path] = new int[singles.Length];
            Compare(output, $"expf {Label(path)}", singles.Length, index =>
            {
                int engine = BitConverter.SingleToInt32Bits(math.Expf(singles[index]));
                singleAnswers[path][index] = engine;
                int port = BitConverter.SingleToInt32Bits(IvpMath.Expf(singles[index], fused));
                return (engine == port, $"{Bits(singles[index])}: engine 0x{engine:x8}, port 0x{port:x8}");
            });

            doubleAnswers[path] = new long[doubles.Length];
            Compare(output, $"exp {Label(path)}", doubles.Length, index =>
            {
                long engine = BitConverter.DoubleToInt64Bits(math.Exp(doubles[index]));
                doubleAnswers[path][index] = engine;
                long port = BitConverter.DoubleToInt64Bits(IvpMath.Exp(doubles[index], fused));
                return (engine == port, $"{Bits(doubles[index])}: engine 0x{engine:x16}, port 0x{port:x16}");
            });

            Compare(output, $"cos {Label(path)}", cosines.Length, index =>
            {
                long engine = BitConverter.DoubleToInt64Bits(math.Cos(cosines[index]));
                long port = BitConverter.DoubleToInt64Bits(IvpMath.Cos(cosines[index], fused));
                return (engine == port, $"{Bits(cosines[index])}: engine 0x{engine:x16}, port 0x{port:x16}");
            });

            cosineAnswers[path] = new long[circular.Length];
            Compare(output, $"cos, reduced too, {Label(path)}", circular.Length, index =>
            {
                long engine = BitConverter.DoubleToInt64Bits(math.Cos(circular[index]));
                cosineAnswers[path][index] = engine;
                long port = BitConverter.DoubleToInt64Bits(IvpMath.Cos(circular[index], fused));
                return (engine == port, $"{Bits(circular[index])}: engine 0x{engine:x16}, port 0x{port:x16}");
            });

            sineAnswers[path] = new long[circular.Length];
            Compare(output, $"sin {Label(path)}", circular.Length, index =>
            {
                long engine = BitConverter.DoubleToInt64Bits(math.Sin(circular[index]));
                sineAnswers[path][index] = engine;
                long port = BitConverter.DoubleToInt64Bits(IvpMath.Sin(circular[index], fused));
                return (engine == port, $"{Bits(circular[index])}: engine 0x{engine:x16}, port 0x{port:x16}");
            });

            Compare(output, $"acos {Label(path)}", arcs.Length, index =>
            {
                long engine = BitConverter.DoubleToInt64Bits(math.Acos(arcs[index]));
                long port = BitConverter.DoubleToInt64Bits(IvpMath.Acos(arcs[index]));
                return (engine == port, $"{Bits(arcs[index])}: engine 0x{engine:x16}, port 0x{port:x16}");
            });
        }

        if (paths.Length == 2)
        {
            Divergent(output, "expf", singles.Length, index => singleAnswers[0][index] != singleAnswers[1][index],
                index => $"{Bits(singles[index])}: plain 0x{singleAnswers[0][index]:x8}, fused 0x{singleAnswers[1][index]:x8}");
            Divergent(output, "exp", doubles.Length, index => doubleAnswers[0][index] != doubleAnswers[1][index],
                index => $"{Bits(doubles[index])}: plain 0x{doubleAnswers[0][index]:x16}, fused 0x{doubleAnswers[1][index]:x16}");
            Divergent(output, "cos", circular.Length, index => cosineAnswers[0][index] != cosineAnswers[1][index],
                index => $"{Bits(circular[index])}: plain 0x{cosineAnswers[0][index]:x16}, fused 0x{cosineAnswers[1][index]:x16}");
            Divergent(output, "sin", circular.Length, index => sineAnswers[0][index] != sineAnswers[1][index],
                index => $"{Bits(circular[index])}: plain 0x{sineAnswers[0][index]:x16}, fused 0x{sineAnswers[1][index]:x16}");
        }

        Compare(output, "asinf", sines.Length, index =>
        {
            int engine = BitConverter.SingleToInt32Bits(math.Asinf(sines[index]));
            int port = BitConverter.SingleToInt32Bits(IvpMath.Asinf(sines[index]));
            return (engine == port, $"{Bits(sines[index])}: engine 0x{engine:x8}, port 0x{port:x8}");
        });

        Compare(output, "atan", tangents.Length, index =>
        {
            long engine = BitConverter.DoubleToInt64Bits(math.Atan(tangents[index]));
            long port = BitConverter.DoubleToInt64Bits(IvpMath.Atan(tangents[index]));
            return (engine == port, $"{Bits(tangents[index])}: engine 0x{engine:x16}, port 0x{port:x16}");
        });
    }

    private static void Compare(TextWriter output, string label, int count, Func<int, (bool Same, string Detail)> check)
    {
        int differ = 0;
        List<string> first = [];

        for (int index = 0; index < count; index++)
        {
            (bool same, string detail) = check(index);

            if (!same)
            {
                differ++;

                if (first.Count < 5)
                {
                    first.Add(detail);
                }
            }
        }

        output.WriteLine($"{label}: {count} compared, {differ} differ");

        foreach (string detail in first)
        {
            output.WriteLine($"  {detail}");
        }
    }

    private static void Divergent(TextWriter output, string label, int count, Func<int, bool> differs, Func<int, string> detail)
    {
        int found = 0;
        List<string> first = [];

        for (int index = 0; index < count; index++)
        {
            if (differs(index))
            {
                found++;

                if (first.Count < 8)
                {
                    first.Add(detail(index));
                }
            }
        }

        output.WriteLine($"{label}: the binary's two paths disagree on {found} of {count}");

        foreach (string line in first)
        {
            output.WriteLine($"  {line}");
        }
    }

    private static float[] SingleArguments()
    {
        List<float> arguments = [];

        for (long bits = 0; bits <= uint.MaxValue; bits += BitStride)
        {
            arguments.Add(BitConverter.Int32BitsToSingle(unchecked((int)(uint)bits)));
        }

        for (int index = 0; index < SweepCount; index++)
        {
            arguments.Add(-3f + (6f * index / SweepCount));
        }

        return [.. arguments];
    }

    private static double[] DoubleArguments()
    {
        ulong state = 20260913;
        List<double> arguments = [];

        for (int index = 0; index < SweepCount; index++)
        {
            arguments.Add(BitConverter.Int64BitsToDouble(unchecked((long)VphysicsLibrary.SplitMix(ref state))));
            arguments.Add(-750d + (1462d * VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state))));
            arguments.Add(-3d + (6d * index / SweepCount));
        }

        return [.. arguments];
    }

    private static float[] SineArguments()
    {
        List<float> arguments = [];

        for (long bits = 0; bits <= uint.MaxValue; bits += BitStride)
        {
            arguments.Add(BitConverter.Int32BitsToSingle(unchecked((int)(uint)bits)));
        }

        for (int index = 0; index < SweepCount; index++)
        {
            arguments.Add(-1f + (2f * index / SweepCount));
        }

        return [.. arguments];
    }

    private static double[] TangentArguments()
    {
        ulong state = 913;
        List<double> arguments = [];

        for (int index = 0; index < SweepCount; index++)
        {
            arguments.Add(BitConverter.Int64BitsToDouble(unchecked((long)VphysicsLibrary.SplitMix(ref state))));
            arguments.Add(-20d + (40d * index / SweepCount));
        }

        return [.. arguments];
    }

    /// <summary>Under π/4, where the ported <see cref="IvpMath.Cos(double, bool)"/> answers, and across its two small-argument steps.</summary>
    private static double[] CosineArguments()
    {
        ulong state = 33;
        List<double> arguments = [];

        for (int index = 0; index < SweepCount; index++)
        {
            arguments.Add((VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state)) - 0.5) * 1.57);
            arguments.Add(Math.ScaleB(VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state)), -12 - (index % 20)));
        }

        return [.. arguments];
    }

    /// <summary>
    /// Every range <c>sin</c> and <c>cos</c> branch on: the series, both inline reductions, both large reductions, near every
    /// multiple of π/2, random bits (infinities and NaNs among them), and each bound itself.
    /// </summary>
    private static double[] CircularArguments()
    {
        ulong state = 2718;
        List<double> arguments = [0d, -0d, double.PositiveInfinity, double.NegativeInfinity, double.NaN];

        foreach (long bound in new long[] { 0x3e40000000000000, 0x3f20000000000000, 0x3fe921fb54442d18, 0x411e848000000000, 0x417312d000000000 })
        {
            for (long step = -2; step <= 2; step++)
            {
                arguments.Add(BitConverter.Int64BitsToDouble(bound + step));
                arguments.Add(-BitConverter.Int64BitsToDouble(bound + step));
            }
        }

        for (int index = 0; index < SweepCount; index++)
        {
            double unit = VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state)) - 0.5;

            arguments.Add(unit * 16d);
            arguments.Add(unit * 2e6);
            arguments.Add(unit * 6e7);
            arguments.Add(Math.ScaleB(unit, (index % 1000) - 30));
            arguments.Add((Math.PI / 2d * ((index % 4001) - 2000)) + Math.ScaleB(unit, -(index % 60)));
            arguments.Add(BitConverter.Int64BitsToDouble(unchecked((long)VphysicsLibrary.SplitMix(ref state))));
        }

        return [.. arguments];
    }

    /// <summary>Every range <c>acos</c> branches on: tiny, under and over one half, ±1 and their neighbours, beyond one, and random bits.</summary>
    private static double[] ArcArguments()
    {
        ulong state = 1414;
        List<double> arguments = [0d, -0d, 1d, -1d, 0.5, -0.5, double.PositiveInfinity, double.NegativeInfinity, double.NaN];

        for (int index = 0; index < SweepCount; index++)
        {
            double unit = VphysicsLibrary.Unit(VphysicsLibrary.SplitMix(ref state));

            arguments.Add((unit * 2.4) - 1.2);
            arguments.Add(BitConverter.Int64BitsToDouble(unchecked((long)VphysicsLibrary.SplitMix(ref state))));
            arguments.Add(Math.ScaleB(unit - 0.5, -(index % 70)));
            arguments.Add(BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(index % 2 == 0 ? 1d : 0.5) + ((index % 64) - 32)) * (index % 3 == 0 ? -1d : 1d));
        }

        return [.. arguments];
    }

    private static bool Controls(TextWriter output, Library math)
    {
        bool expfOne = BitConverter.SingleToInt32Bits(math.Expf(0f)) == BitConverter.SingleToInt32Bits(1f);
        bool expOne = BitConverter.DoubleToInt64Bits(math.Exp(0d)) == BitConverter.DoubleToInt64Bits(1d);
        bool halfPi = BitConverter.SingleToInt32Bits(math.Asinf(1f)) == HalfPiBits;
        bool quarterPi = BitConverter.DoubleToInt64Bits(math.Atan(1d)) == QuarterPiBits;
        bool sineZero = BitConverter.DoubleToInt64Bits(math.Sin(0d)) == 0;
        bool arcOne = BitConverter.DoubleToInt64Bits(math.Acos(1d)) == 0;

        output.WriteLine(
            $"controls: expf(0) = 1 {expfOne}; exp(0) = 1 {expOne}; asinf(1) = pi/2 {halfPi}; atan(1) = pi/4 {quarterPi}; " +
            $"sin(0) = 0 {sineZero}; acos(1) = 0 {arcOne}");

        if (expfOne && expOne && halfPi && quarterPi && sineZero && arcOne)
        {
            return true;
        }

        output.WriteLine("A control failed: these addresses do not fit this build, so nothing else is reported.");
        return false;
    }

    private static string Label(int path) => path == 0 ? "plain" : "fused";

    private static string Bits(float value) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{BitConverter.SingleToInt32Bits(value):x8} ({value:R})");

    private static string Bits(double value) =>
        string.Create(CultureInfo.InvariantCulture, $"0x{BitConverter.DoubleToInt64Bits(value):x16} ({value:R})");

    /// <summary>The four routines and the path flag, as one loaded image holds them.</summary>
    private sealed record Library(
        SingleFunction Expf,
        DoubleFunction Exp,
        SingleFunction Asinf,
        DoubleFunction Atan,
        DoubleFunction Cos,
        DoubleFunction Sin,
        DoubleFunction Acos,
        nint Flag);
}
