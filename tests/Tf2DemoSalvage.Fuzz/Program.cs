using System;
using System.IO;
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
                Fuzzer.LibFuzzer.Run(BitReaderFuzzTarget.Consume);
                break;

            case "varint":
                Fuzzer.LibFuzzer.Run(VarIntFuzzTarget.Consume);
                break;

            case "container":
                Fuzzer.LibFuzzer.Run(ContainerFuzzTarget.Consume);
                break;

            case "snappy":
                Fuzzer.LibFuzzer.Run(SnappyFuzzTarget.Consume);
                break;

            default:
                // Not ArgumentException: this is environment configuration, not a parameter, and
                // S3928 rightly objects to naming something that is not in the argument list.
                throw new InvalidOperationException(
                    $"Unknown {TargetVariable} '{target}'. Expected one of: bitreader, " +
                    $"varint, container, snappy.");
        }
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
