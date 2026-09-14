using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's default range manager — an object's range and a pair's — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-range` probe's `fixture` mode loaded
/// the game's x64 `vphysics.dll`, built its range manager with `FUN_1800a0420` and policy 1, called slots `FUN_1800a04e0` and
/// `FUN_1800a0560` on fabricated cores, and wrote every lane to `Data/ivp-range.txt`. <see cref="IvpRangeReplay"/> is the one
/// definition of the lanes.
/// </remarks>
public sealed class IvpRangeConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-range.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"Range_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void Range_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpRangeReplay.Differences(replay.Outputs, IvpRangeReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: a resting unit sphere's range is its radius, since a speed of `1e-20` loses every
    /// clamp to the surface — so a fixture that never reached the binary, or read the wrong field, cannot pass.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_IncludeSpeedsEveryClampDecides()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;

        replays.Count.ShouldBeGreaterThanOrEqualTo(400);
        replays.Count(replay => double.IsNaN(BitConverter.Int64BitsToDouble(replay.Outputs["pair"][0]))).ShouldBeGreaterThan(0);
        replays.Count(replay => BitConverter.Int64BitsToDouble(replay.Outputs["object-first"][0]) is > 1d and < 15d).ShouldBeGreaterThan(0);
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpRangeReplay.Parse(reader);
    }
}
