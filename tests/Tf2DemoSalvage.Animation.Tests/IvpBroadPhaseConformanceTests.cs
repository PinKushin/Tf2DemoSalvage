using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's broad phase — eight objects refiled and rebuilt over sixteen steps — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-broad-phase` probe's `fixture` mode
/// loaded the game's x64 `vphysics.dll`, ran each case's steps through `FUN_180098880` and `FUN_180096eb0` over fabricated objects and
/// callback tables, and wrote every call they made out and where each node was left to `Data/ivp-broad-phase.txt`.
/// <see cref="IvpBroadPhaseReplay"/> is the one definition of the lanes and of the callbacks.
/// </remarks>
public sealed class IvpBroadPhaseConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-broad-phase.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"BroadPhase_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void BroadPhase_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpBroadPhaseReplay.Differences(replay.Outputs, IvpBroadPhaseReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: across the cases the binary must have asked the filter, made watchers, had a creator
    /// decline, deleted watchers and told creators a node was going — every way the broad phase calls out.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachEveryCallOut()
    {
        HashSet<int> kinds = [];

        foreach (IvpReplayCase replay in Replays.Value)
        {
            for (int step = 0; step < IvpBroadPhaseReplay.StepCount; step++)
            {
                int carried = Math.Min((int)replay.Outputs["event-count"][step], IvpBroadPhaseReplay.CarriedEvents);

                for (int index = 0; index < carried; index++)
                {
                    kinds.Add((int)replay.Outputs["events"][(step * IvpBroadPhaseReplay.CarriedEvents) + index] >> 24);
                }
            }
        }

        kinds.ShouldBe(
            [IvpBroadPhaseReplay.Filtered, IvpBroadPhaseReplay.Declined, IvpBroadPhaseReplay.Created, IvpBroadPhaseReplay.Deleted, IvpBroadPhaseReplay.Removed],
            ignoreOrder: true);
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpBroadPhaseReplay.Parse(reader);
    }
}
