using System;

using BenchmarkDotNet.Running;

namespace Tf2DemoSalvage.Benchmarks;

/// <summary>
/// Entry point for the benchmark suite.
/// </summary>
/// <remarks>
/// **Not part of the gate, and deliberately.** `build/gate.sh` runs tests, which either pass or
/// fail; a benchmark produces a number and no verdict, so putting one behind a threshold turns
/// ordinary machine noise into a red build. The numbers are read, not asserted.
///
/// **Run it locally and quietly:**
///
/// <code>
/// dotnet run -c Release --project benchmarks/Tf2DemoSalvage.Benchmarks
/// </code>
///
/// Never on the Oracle measurement boxes — `CLAUDE.md` excludes benchmarks from them by name,
/// because shared cloud vCPUs cannot give a stable time and a benchmark's whole value is stability.
/// Mutation and fuzzing go there instead: they count survivors and crashes, which a noisy neighbour
/// does not change.
///
/// **Consider taking the machine lock for a serious run.** `run-exclusive.ps1` currently guards the
/// UI suites and Stryker; a benchmark is not on that list, but another agent building in the
/// background is exactly the kind of neighbour that makes a time meaningless. Not added
/// unilaterally — it changes the shared-machine protocol, so it is the owner's call.
/// </remarks>
public static class Program
{
    /// <summary>Runs every benchmark in this assembly.</summary>
    /// <param name="args">Passed through to BenchmarkDotNet — <c>--filter</c>, <c>--list</c>, etc.</param>
    public static void Main(string[] args) =>
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}
