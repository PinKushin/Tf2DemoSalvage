using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's many-contact heap solve — a friction system's contacts sorted by push streak, built into a system of responses and
/// targets, solved, and pushed into the cores — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-heap-solve` probe's `fixture` mode
/// loaded the game's x64 `vphysics.dll`, wrote a friction system and everything it reaches at the offsets `docs/findings/51`
/// reads, called `FUN_1800a9bf0`, and wrote every lane it left to `Data/ivp-heap-solve.txt`. <see cref="IvpHeapSolveReplay"/> is
/// the one definition of the lanes.
/// </remarks>
public sealed class IvpHeapSolveConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-heap-solve.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"HeapSolve_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void HeapSolve_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpHeapSolveReplay.Differences(replay.Outputs, IvpHeapSolveReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the binary must have pushed a contact and left one unpushed, reordered a list,
    /// frozen a crowded heap and solved one it was told not to freeze, and been handed a NaN.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachTheBranchesTheLanesShow()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;

        replays.Count(replay => replay.Outputs["streak"].Contains(-1)).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["normal-push"].Contains(0)).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["order"].Select((place, position) => place != position).Any(moved => moved))
            .ShouldBeGreaterThan(0);
        replays.Single(replay => replay.Label == "crowded-1").Outputs["core-flag-bit0"].ShouldContain(1);
        replays.Single(replay => replay.Label == "crowded-0").Outputs["record-index"].Any(index => index != 0).ShouldBeTrue();
        replays.Count(replay => replay.Inputs["contact-gap"].Any(lane => float.IsNaN(BitConverter.Int32BitsToSingle(unchecked((int)lane)))))
            .ShouldBeGreaterThan(0);
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpHeapSolveReplay.Parse(reader);
    }
}
