using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's object cache refresh — the object's placement at the current PSI — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-object-cache` probe's `fixture` mode
/// loaded the game's x64 `vphysics.dll`, called `FUN_180080a60` on a fabricated cache, object, core and environment, and wrote every
/// lane to `Data/ivp-object-cache.txt`. <see cref="IvpObjectCacheReplay"/> is the one definition of the lanes.
/// </remarks>
public sealed class IvpObjectCacheConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-object-cache.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"ObjectCache_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void ObjectCache_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpObjectCacheReplay.Differences(replay.Outputs, IvpObjectCacheReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the binary must have copied the core for a case with no time elapsed — its cached
    /// rotation the core's committed orientation, bit for bit — and interpolated for another, and composed an object rotation and an
    /// offset somewhere, so both branches and both compositions were reached; and some matrix must hold a NaN, so the destinations
    /// were in the lanes.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachBothBranchesAndBothCompositions()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;

        replays.Count(replay => replay.Inputs["now"][0] == replay.Inputs["stepped"][0] && replay.Inputs["has-rotation"][0] == 0 &&
            replay.Outputs["cached-rotation"].SequenceEqual(replay.Inputs["orientation"])).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["now"][0] != replay.Inputs["stepped"][0] &&
            !replay.Outputs["cached-rotation"].SequenceEqual(replay.Inputs["orientation"])).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["has-rotation"][0] != 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["has-offset"][0] != 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["matrix"].Any(lane => double.IsNaN(BitConverter.Int64BitsToDouble(lane)))).ShouldBeGreaterThan(0);
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpObjectCacheReplay.Parse(reader);
    }
}
