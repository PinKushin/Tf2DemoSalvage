using System;
using System.IO;
using System.Security.Cryptography;
using SharpFuzz;
using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Fuzz;

/// <summary>
/// libFuzzer entry point. Run under WSL after instrumenting the assembly with
/// <c>sharpfuzz</c> - see <c>docs/FUZZING.md</c> for the toolchain, and note in particular that
/// a green run proves nothing unless the execution count and corpus size actually grew.
/// </summary>
/// <remarks>
/// libFuzzer drives one target per process, so the target is selected by the
/// <c>TF2FUZZ_TARGET</c> environment variable rather than a command-line argument - libFuzzer
/// owns argv. Each target wants its own corpus directory; sharing one would let inputs shaped
/// for one decoder dominate another's.
/// </remarks>
internal static class Program
{
    private const string TargetVariable = "TF2FUZZ_TARGET";

    /// <summary>
    /// When set, writes a single valid demo to this path and exits instead of fuzzing.
    /// </summary>
    /// <remarks>
    /// A corpus-seeding mode rather than a separate project. The container target starts from
    /// an empty corpus and its input has an 8-byte magic and a 1072-byte fixed header before
    /// anything varies — measured 2026-08-11: 12 million random-mutation executions never moved
    /// coverage past the header check. One valid demo, generated from the same
    /// <see cref="DemoWriter"/> the deterministic property tests already use, gets a run started
    /// on the far side of that wall instead of hoping mutation finds it by chance.
    /// </remarks>
    private const string SeedPathVariable = "TF2FUZZ_SEED_PATH";

    /// <summary>Directory to write a crashing input into. Preservation is off when unset.</summary>
    private const string CrashDirectoryVariable = "TF2FUZZ_CRASH_DIR";

    private static void Main()
    {
        string? seedPath = Environment.GetEnvironmentVariable(SeedPathVariable);

        if (seedPath is not null)
        {
            WriteSeed(seedPath);
            return;
        }

        string target = Environment.GetEnvironmentVariable(TargetVariable) ?? "bitreader";

        switch (target)
        {
            case "bitreader":
                Fuzzer.LibFuzzer.Run(Preserving(BitReaderFuzzTarget.Consume));
                break;

            case "varint":
                Fuzzer.LibFuzzer.Run(Preserving(VarIntFuzzTarget.Consume));
                break;

            case "container":
                Fuzzer.LibFuzzer.Run(Preserving(ContainerFuzzTarget.Consume));
                break;

            case "snappy":
                Fuzzer.LibFuzzer.Run(Preserving(SnappyFuzzTarget.Consume));
                break;

            case "selftest":
                // Deliberately always throws, and exists so the crash-preservation pipeline can
                // be proved end to end without waiting for a real defect to turn up. The same
                // reasoning as the sharpfuzz size check in build/run-measurements.sh: a
                // mechanism that only runs when something goes wrong is a mechanism nobody has
                // ever seen work. Verified in WSL 2026-08-11 - it produced the first
                // crash-<hash>.bin this project has ever saved.
                Fuzzer.LibFuzzer.Run(Preserving(static data =>
                    throw new FuzzPropertyViolationException(
                        $"selftest target: refusing {data.Length} bytes on purpose.")));
                break;

            default:
                // Not ArgumentException: this is environment configuration, not a parameter, and
                // S3928 rightly objects to naming something that is not in the argument list.
                throw new InvalidOperationException(
                    $"Unknown {TargetVariable} '{target}'. Expected one of: bitreader, " +
                    $"varint, container, snappy, selftest.");
        }
    }

    /// <summary>
    /// Wraps a target so the input that broke it is written out before the process dies.
    /// </summary>
    /// <param name="inner">The target being fuzzed.</param>
    /// <returns>The same target, with the offending input preserved on the way past.</returns>
    /// <remarks>
    /// **libFuzzer does not save the reproducer in this setup, and nothing about a run says so.**
    /// On a managed exception SharpFuzz aborts the .NET child, the <c>libfuzzer-dotnet</c> bridge
    /// dies with it, and libFuzzer's own crash handler never runs — so <c>-artifact_prefix</c>
    /// produces no <c>crash-&lt;sha1&gt;</c> file and no "Test unit written to" line, while the
    /// exception itself still prints in full. The run looks healthy and reports the defect; only
    /// the bytes that caused it are missing.
    ///
    /// Recovering them from the corpus does not work either, and that was tried first: libFuzzer
    /// adds only coverage-increasing inputs, and an input that crashes never gets added. Measured
    /// 2026-08-11 — replaying all 26 corpus entries against a target that had just crashed
    /// isolated nothing, because the crash came on the first mutated input after
    /// <c>#27 INITED</c>.
    ///
    /// So the bytes are written here, in the one place that provably holds them. Named by content
    /// hash, matching libFuzzer's own convention, so a repeated finding does not accumulate
    /// duplicate files.
    /// </remarks>
    private static ReadOnlySpanAction Preserving(ReadOnlySpanAction inner) =>
        data =>
        {
            string? directory = Environment.GetEnvironmentVariable(CrashDirectoryVariable);

            if (directory is null)
            {
                inner(data);
                return;
            }

            // The copy has to happen before the call: `data` is a span over a buffer the caller
            // reuses, so reading it after the throw would be reading whatever came next.
            byte[] input = data.ToArray();

            try
            {
                inner(data);
            }
            catch (Exception) when (Save(directory, input))
            {
                // The filter never matches - Save always returns false. Writing the file from an
                // exception FILTER rather than a catch block means the input is preserved without
                // this method altering how the exception propagates: SharpFuzz still sees it
                // exactly as the target threw it, and the stack is not rewound first.
                throw;
            }
        };

    private static bool Save(string directory, byte[] input)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string name = Convert.ToHexStringLower(SHA256.HashData(input))[..16];
            File.WriteAllBytes(Path.Combine(directory, $"crash-{name}.bin"), input);
        }
        catch (IOException)
        {
            // A failure to save must not replace the finding with a different error. The
            // exception being filtered is the interesting one; losing it to a disk problem would
            // trade a decoder bug for an I/O bug.
        }

        return false;
    }

    private static void WriteSeed(string path)
    {
        DemoHeader header = new()
        {
            DemoProtocol = 3,
            NetworkProtocol = 24,
            ServerName = "seed",
            ClientName = "seed",
            MapName = "seed",
            GameDirectory = "tf",
            PlaybackTimeSeconds = 1f,
            PlaybackTicks = 1,
            PlaybackFrames = 1,
            SignonLengthBytes = 0,
        };

        byte[] demo = DemoWriter.Write(
            header, [new DemoCommand(DemoCommandType.Stop, 0, default)]);

        File.WriteAllBytes(path, demo);
    }
}
