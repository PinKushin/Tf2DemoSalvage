using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's heap-solve core routines — a push through a record, the limits, the flush and drop of staged changes, and a core's
/// kinetic energy — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-heap-core` probe's `fixture` mode
/// loaded the game's x64 `vphysics.dll`, wrote two cores, a record and an environment at the offsets `docs/findings/51` reads,
/// called `FUN_1800a9280`, `FUN_180076710`, `FUN_180077950`, `FUN_180076670` and `FUN_180077e80` on fresh copies, and wrote
/// every lane they left to `Data/ivp-heap-core.txt`. <see cref="IvpHeapCoreReplay"/> is the one definition of the lanes.
/// </remarks>
public sealed class IvpHeapCoreConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-heap-core.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"HeapCore_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void HeapCore_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpHeapCoreReplay.Differences(replay.Outputs, IvpHeapCoreReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the binary must have held a speed and a staged speed to the limit and left others
    /// alone, pushed cases with each core absent, and been handed a NaN.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachTheBranchesTheLanesShow()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;

        replays.Count(replay => !replay.Outputs["limited-first-velocity"].SequenceEqual(replay.Inputs["first-velocity"]))
            .ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["limited-first-velocity"].SequenceEqual(replay.Inputs["first-velocity"]))
            .ShouldBeGreaterThan(0);
        replays.Count(replay => !replay.Outputs["limited-first-pending-spin"].SequenceEqual(replay.Inputs["first-pending-spin"]))
            .ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["movable"][0] == 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["movable"][1] == 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["first-velocity"].Any(lane => float.IsNaN(BitConverter.Int32BitsToSingle(unchecked((int)lane)))))
            .ShouldBeGreaterThan(0);
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpHeapCoreReplay.Parse(reader);
    }
}
