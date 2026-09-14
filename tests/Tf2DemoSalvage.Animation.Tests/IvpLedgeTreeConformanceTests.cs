using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The polygon surface manager's radius query over synthesized ledge trees, replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-ledge-tree` probe's `fixture` mode
/// loaded the game's x64 `vphysics.dll`, handed each case's surface to a manager built on its own table `1800eae60`, asked slot 4
/// (`FUN_18007ada0`) four times, and wrote every lane to `Data/ivp-ledge-tree.txt`. <see cref="IvpLedgeTreeReplay"/> is the one
/// definition of the lanes and of the surface's layout.
/// </remarks>
public sealed class IvpLedgeTreeConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-ledge-tree.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"LedgeTree_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void LedgeTree_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpLedgeTreeReplay.Differences(replay.Outputs, IvpLedgeTreeReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the binary must have found nothing for some query, more ledges than the lanes carry
    /// for another, and something for a query started beneath a hull — so the sphere test, the walk's depth and the ledge path
    /// were all reached.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachEveryWayAQueryEnds()
    {
        (IvpReplayCase Replay, int Query)[] queries =
            [.. Replays.Value.SelectMany(replay => Enumerable.Range(0, IvpLedgeTreeReplay.QueryCount).Select(query => (replay, query)))];

        queries.Count(pair => pair.Replay.Outputs["found"][pair.Query] == 0).ShouldBeGreaterThan(0);
        queries.Count(pair => pair.Replay.Outputs["found"][pair.Query] > IvpLedgeTreeReplay.CarriedLedges).ShouldBeGreaterThan(0);
        queries.Count(pair => pair.Replay.Inputs["query-ledge"][pair.Query] != IvpLedgeTreeReplay.Unused &&
            pair.Replay.Outputs["found"][pair.Query] > 0).ShouldBeGreaterThan(0);
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpLedgeTreeReplay.Parse(reader);
    }
}
