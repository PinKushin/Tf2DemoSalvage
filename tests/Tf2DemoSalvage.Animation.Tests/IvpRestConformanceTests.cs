using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// A core's rest test and vphysics' generator, replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-rest` probe's `fixture` mode loaded
/// the game's x64 `vphysics.dll`, wrote a core at the offsets `docs/findings/51` reads, called `FUN_180077220` and then
/// `FUN_18007d5c0` with the case's seed, and wrote every lane they left to `Data/ivp-rest.txt`. <see cref="IvpRestReplay"/> is the
/// one definition of the lanes.
/// </remarks>
public sealed class IvpRestConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-rest.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"Rest_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void Rest_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpRestReplay.Differences(replay.Outputs, IvpRestReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the binary must have answered each of moving, still and resting, moved the wider
    /// anchor in some cases and not in others, and been handed a NaN — and the fixture must carry the searched cases a sabotage
    /// round needed: an elapsed time only the float narrowing holds under the delay, turns straddling the threshold between the
    /// dot's groupings and between which orientation is narrowed, and spins straddling the limit between the squares' groupings.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachTheBranchesTheLanesShow()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;

        foreach (long motion in new long[] { 1, 2, 3 })
        {
            replays.Count(replay => replay.Outputs["motion"][0] == motion).ShouldBeGreaterThan(0);
        }

        foreach (string searched in new[] { "delay-", "turn-grouping-", "turn-narrowing-", "spin-grouping-" })
        {
            replays.Count(replay => replay.Label.StartsWith(searched, StringComparison.Ordinal)).ShouldBeGreaterThan(0, searched);
        }

        replays.Count(replay => replay.Outputs["settled-time"][0] != replay.Inputs["settle-time"][0]).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["anchored-time"][0] != replay.Inputs["anchor-time"][0] &&
                                replay.Outputs["settled-time"][0] == replay.Inputs["settle-time"][0]).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["position"].Any(lane => double.IsNaN(BitConverter.Int64BitsToDouble(lane))) ||
                                replay.Inputs["spin"].Any(lane => float.IsNaN(BitConverter.Int32BitsToSingle(unchecked((int)lane)))))
            .ShouldBeGreaterThan(0);
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpRestReplay.Parse(reader);
    }
}
