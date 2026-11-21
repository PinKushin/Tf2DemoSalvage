using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

using Tf2DemoSalvage.Core.Primitives;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// The shipped <c>vstdlib.dll</c>'s own RNG called in process, against this project's port of it (B415).
/// </summary>
/// <remarks>
/// **This is the only thing that can tell a good `ran1` from VALVE's `ran1`.** `UniformRandomStreamConformanceTests`
/// pins the port against itself — determinism, range, divergence — and every one of those assertions would still pass on
/// a generator with the wrong multiplier, the wrong warm-up length, or a table filled forwards. A hitscan shot is
/// reconstructed entirely from `m_iSeed` (`docs/findings/57-the-shot-is-a-seed.md`), so a stream that is merely
/// plausible puts every tracer somewhere plausible and wrong.
///
/// `RandomSeed` and `RandomFloat` are exported undecorated, so they are called by name rather than by address — nothing
/// here depends on the image base the findings were read at.
///
/// <code>
///   vstdlib-random              — 2,000 draws across a spread of seeds, port against the binary
///   vstdlib-random &lt;seed&gt; [n]   — n draws on one seed, printing the first disagreement
/// </code>
/// </remarks>
public sealed class VstdlibRandomProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "vstdlib-random";

    /// <inheritdoc/>
    public string Summary =>
        "the shipped vstdlib.dll's RNG against this project's port of it: vstdlib-random [seed [draws]]";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void RandomSeedCall(int seed);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float RandomFloatCall(float minimum, float maximum);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int RandomIntCall(int minimum, int maximum);

    /// <summary>
    /// The ranges `RandomInt` is asked in order on each seed: a one-value range and a reversed one (no draw), the
    /// rejection-heavy `0..2^30` (`0x80000000 % span` is `2^30 - 1`, so nearly half the draws are refused), and the
    /// ranges `CTFTips` and the sound emitter ask.
    /// </summary>
    private static readonly (int Low, int High)[] IntRanges =
    [
        (5, 5), (1, 100), (0, -1), (0, 0x40000000), (1, 9), (0, 3), (0, 0x40000000), (0, int.MaxValue - 1), (-7, 7),
        (int.MinValue, int.MaxValue), (0, 0x40000000), (1, 100),

        // A difference of 2^31, above MAX_RANDOM_RANGE: answered with the low end and no draw, the `ja` at 0x18000fb9d.
        (-1, int.MaxValue), (int.MinValue, 0), (1, 100),
    ];

    /// <summary>The exported global `RandomInt` against <see cref="UniformRandomStream.RandomInt"/>, range by range.</summary>
    private static void CompareRandomInt(TextWriter output, RandomSeedCall seedIt, RandomIntCall intIt)
    {
        int compared = 0;
        int disagreed = 0;

        foreach (int seed in (int[])[0, 1, 2, 7, 1337, 20260920, -424242])
        {
            seedIt(seed);

            UniformRandomStream ported = new();
            ported.SetSeed(seed);

            for (int round = 0; round < 20; round++)
            {
                foreach ((int low, int high) in IntRanges)
                {
                    int engine = intIt(low, high);
                    int mine = ported.RandomInt(low, high);

                    compared++;

                    if (seed == 0 && round == 0)
                    {
                        output.WriteLine($"  seed 0: RandomInt({low}, {high}) = {engine}");
                    }

                    if (engine != mine)
                    {
                        disagreed++;

                        if (disagreed <= 5)
                        {
                            output.WriteLine($"  seed {seed} round {round}: RandomInt({low}, {high}) engine {engine}, port {mine}");
                        }
                    }
                }
            }
        }

        output.WriteLine();
        output.WriteLine(
            disagreed == 0
                ? $"{compared} RandomInt calls agree with the shipped vstdlib.dll."
                : $"{disagreed} of {compared} RandomInt calls DISAGREE.");
    }

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!TryLoad(output, out nint module))
        {
            return;
        }

        RandomSeedCall seedIt = Marshal.GetDelegateForFunctionPointer<RandomSeedCall>(
            NativeLibrary.GetExport(module, "RandomSeed"));
        RandomFloatCall drawIt = Marshal.GetDelegateForFunctionPointer<RandomFloatCall>(
            NativeLibrary.GetExport(module, "RandomFloat"));

        if (arguments.Count > 0 && arguments[0] == "int")
        {
            CompareRandomInt(output, seedIt, Marshal.GetDelegateForFunctionPointer<RandomIntCall>(NativeLibrary.GetExport(module, "RandomInt")));
            return;
        }

        int[] seeds = arguments.Count > 0
            ? [int.Parse(arguments[0], CultureInfo.InvariantCulture)]
            : [0, 1, 2, -1, 7, 1337, 20260920, int.MaxValue, -424242];

        int draws = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : 200;

        int compared = 0;
        int disagreed = 0;

        foreach (int seed in seeds)
        {
            seedIt(seed);

            UniformRandomStream ported = new();
            ported.SetSeed(seed);

            for (int draw = 0; draw < draws; draw++)
            {
                // **The spread's own range**, because that is what FX_FireBullets asks for: two draws of
                // RandomFloat(-0.5, 0.5) per axis. A sweep over [0,1] would miss a sign error in the scaling.
                float engine = drawIt(-0.5f, 0.5f);
                float mine = ported.RandomFloat(-0.5f, 0.5f);

                compared++;

                // A short run prints every draw, so a conformance test can be given the engine's own numbers rather
                // than the port's — the values are only evidence while this probe says the two agree.
                if (draws <= 10)
                {
                    output.WriteLine(
                        $"  seed {seed} draw {draw}: {engine:R}f  (0x{BitConverter.SingleToInt32Bits(engine):x8})");
                }

                if (BitConverter.SingleToInt32Bits(engine) == BitConverter.SingleToInt32Bits(mine))
                {
                    continue;
                }

                disagreed++;

                if (disagreed <= 5)
                {
                    output.WriteLine(
                        $"  seed {seed} draw {draw}: engine {engine:R} (0x{BitConverter.SingleToInt32Bits(engine):x8}), " +
                        $"port {mine:R} (0x{BitConverter.SingleToInt32Bits(mine):x8})");
                }
            }
        }

        output.WriteLine();
        output.WriteLine(
            disagreed == 0
                ? $"{compared} draws over {seeds.Length} seeds agree BIT FOR BIT with the shipped vstdlib.dll."
                : $"{disagreed} of {compared} draws DISAGREE — the port is not Valve's stream.");
    }

    /// <summary>Loads the installed game's x64 <c>vstdlib.dll</c>.</summary>
    /// <remarks>
    /// **The game's `bin/x64` is added to the search path first**, because vstdlib imports `tier0.dll` from beside it;
    /// loading it by full path alone finds the library and then fails on its dependency.
    /// </remarks>
    private static bool TryLoad(TextWriter output, out nint module)
    {
        module = 0;

        if (new MapLocator(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder).FindGameFolder() is not { } folder ||
            Path.GetDirectoryName(folder) is not { } root)
        {
            output.WriteLine("The game folder could not be found.");
            return false;
        }

        string binaries = Path.Combine(root, "bin", "x64");
        string library = Path.Combine(binaries, "vstdlib.dll");

        if (!File.Exists(library))
        {
            output.WriteLine($"No library at {library}.");
            return false;
        }

        NativeLibrary.Load(Path.Combine(binaries, "tier0.dll"));
        module = NativeLibrary.Load(library);

        output.WriteLine($"{library} loaded at 0x{module:x}");
        output.WriteLine();

        return true;
    }
}
