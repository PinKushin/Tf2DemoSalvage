using System;
using SharpFuzz;

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

    private static void Main()
    {
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
}
