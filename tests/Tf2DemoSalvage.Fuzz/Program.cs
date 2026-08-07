using System;
using SharpFuzz;

namespace Tf2DemoSalvage.Fuzz;

/// <summary>
/// libFuzzer entry point. Run under WSL after instrumenting the assembly with
/// <c>sharpfuzz</c> - see <c>docs/FUZZING.md</c> for the toolchain, and note in particular that
/// a green run proves nothing unless the execution count and corpus size actually grew.
/// </summary>
internal static class Program
{
    private static void Main() => Fuzzer.LibFuzzer.Run(BitReaderFuzzTarget.Consume);
}
