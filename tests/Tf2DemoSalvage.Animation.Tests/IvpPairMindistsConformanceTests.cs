using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's pair mindist refresh — two objects' ledges paired over six steps — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-pair-mindists` probe's `fixture` mode
/// loaded the game's x64 `vphysics.dll`, ran each case's refreshes through `FUN_180096680` over synthesized surfaces behind its own
/// polygon manager table, with the constructor's tails detoured to recorders, and wrote the pair and every call out to
/// `Data/ivp-pair-mindists.txt`. <see cref="IvpPairMindistsReplay"/> is the one definition of the lanes and of the callbacks.
/// </remarks>
public sealed class IvpPairMindistsConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-pair-mindists.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"PairMindists_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void PairMindists_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpPairMindistsReplay.Differences(replay.Outputs, IvpPairMindistsReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the binary must have made mindists, deleted some it no longer paired, and kept some
    /// across a step — a pair holding a name it held the step before — so creation, deletion and the kept prefix were all reached.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_MakeKeepAndDeleteMindists()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;
        int last = IvpPairMindistsReplay.StepCount - 1;

        replays.Count(replay => replay.Outputs["created"][last] > 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["deleted"][last] > 0).ShouldBeGreaterThan(0);
        replays.Count(replay => Enumerable.Range(1, last).Any(step =>
            replay.Outputs["pair-count"][step] > 0 && replay.Outputs["pair-count"][step - 1] > 0 &&
            replay.Outputs["created"][step] == replay.Outputs["created"][step - 1])).ShouldBeGreaterThan(0);
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpPairMindistsReplay.Parse(reader);
    }
}
