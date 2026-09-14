using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's pair watcher — made, refreshed five times by its hull records, and ended — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-pair-watcher` probe's `fixture` mode loaded
/// the game's x64 `vphysics.dll`, made each case's watcher through the default creator's own table, told its records their hulls passed,
/// ended it through its own destructor or the creator's removal notice, and wrote what it left to `Data/ivp-pair-watcher.txt`.
/// <see cref="IvpPairWatcherReplay"/> is the one definition of the lanes.
/// </remarks>
public sealed class IvpPairWatcherConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-pair-watcher.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"PairWatcher_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void PairWatcher_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpPairWatcherReplay.Differences(replay.Outputs, IvpPairWatcherReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the binary must have made mindists and deleted some across refreshes, counted every refresh,
    /// registered the watcher behind other collisions on both nodes at once, deleted two or more other collisions in one removal notice,
    /// and ended cases each of the three ways — so creation, a refresh's deletions, the watcher's two indices and every ending were reached.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_MakeDeleteAndEndEveryWay()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;
        int last = IvpPairWatcherReplay.StepCount - 1;

        replays.Count(replay => replay.Outputs["created"][last] > 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["deleted"][last] > 0).ShouldBeGreaterThan(0);
        replays.ShouldAllBe(replay => replay.Outputs["refreshes"][last] == IvpPairWatcherReplay.StepCount);
        replays.Count(replay => replay.Outputs["watcher-index"][0] != replay.Outputs["watcher-index"][1]).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["end-event-count"][0] >= 2).ShouldBeGreaterThan(0);

        foreach (int ending in new[] { IvpPairWatcherReplay.EndDeleted, IvpPairWatcherReplay.EndFirstRemoved, IvpPairWatcherReplay.EndSecondRemoved })
        {
            replays.Count(replay => replay.Inputs["ending"][0] == ending).ShouldBeGreaterThan(0);
        }
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpPairWatcherReplay.Parse(reader);
    }
}
