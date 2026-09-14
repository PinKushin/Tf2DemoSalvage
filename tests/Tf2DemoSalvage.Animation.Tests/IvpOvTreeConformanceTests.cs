using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's OV tree — sequences of inserts and removals over sixteen nodes — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-ov-tree` probe's `fixture` mode
/// loaded the game's x64 `vphysics.dll`, built a tree with its constructor `FUN_18009d820` and nodes with `FUN_18009d7b0`, ran each
/// case's steps through `FUN_18009ecb0` and `FUN_18009efc0`, and wrote every lane to `Data/ivp-ov-tree.txt`.
/// <see cref="IvpOvTreeReplay"/> is the one definition of the lanes.
/// </remarks>
public sealed class IvpOvTreeConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-ov-tree.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"OvTree_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void OvTree_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpOvTreeReplay.Differences(replay.Outputs, IvpOvTreeReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the binary must have found overlapping nodes, filed a node with the outer radius,
    /// emptied the tree with a removal, and filed nodes at levels at least eight apart — so growth, descent and the path between
    /// were all run.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachTheBranchesTheLanesShow()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;
        long emptyTree = IvpOvTreeReplay.Digest([-1]);

        replays.Count(replay => replay.Outputs["found"].Any(lane => lane > 2)).ShouldBeGreaterThan(0);
        replays.Count(replay => Enumerable.Range(0, IvpOvTreeReplay.StepCount).Any(step =>
                replay.Inputs["outer"][step] != replay.Inputs["radius"][step] && replay.Outputs["result"][step] == replay.Inputs["outer"][step]))
            .ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["tree-digest"].Any(lane => lane == emptyTree)).ShouldBeGreaterThan(0);
        replays.Count(replay =>
            {
                int[] levels = [.. Enumerable.Range(0, IvpOvTreeReplay.StepCount).Select(step => (int)replay.Outputs["key"][(step * 5) + 3])];
                return levels.Max() - levels.Min() >= 8;
            })
            .ShouldBeGreaterThan(0);
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpOvTreeReplay.Parse(reader);
    }
}
