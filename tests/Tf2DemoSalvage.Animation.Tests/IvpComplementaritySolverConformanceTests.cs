using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's many-contact linear algebra — the scaling, the gathered sub-system and its elimination, the row test and the constraint
/// solver — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-contact-solve` probe's `fixture`
/// mode loaded the game's x64 `vphysics.dll`, wrote each system at the offsets `docs/findings/51` reads, called
/// `FUN_1800aa2c0`, `FUN_1800a4d40`, `FUN_1800a80a0`, `FUN_1800a7270` and `FUN_1800a5e60` in `FUN_1800aa5c0`'s order, and wrote
/// every field they left to `Data/ivp-complementarity.txt`. <see cref="IvpComplementarityReplay"/> is the one definition of the
/// lanes, shared with the probe.
/// </remarks>
public sealed class IvpComplementaritySolverConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-complementarity.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"Solve_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void Solve_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpComplementarityReplay.Differences(replay.Outputs, IvpComplementarityReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: cases that all took one side of a branch would pass a port missing the other. The
    /// binary must have solved some systems and given up on others, eliminated some sub-systems and refused others, passed and
    /// failed the row test, let the inverse go stale and eliminated directions from scratch, shuffled, gathered by both of
    /// <c>FUN_1800a4d40</c>'s paths, and been handed NaN.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachTheBranchesTheLanesShow()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;

        replays.Count(replay => replay.Outputs["solved"][0] == 1).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["solved"][0] == 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["gathered-solved"][0] == 1).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["gathered-solved"][0] == 0).ShouldBeGreaterThan(0);
        replays.Count(replay => Holds(replay).Contains(0)).ShouldBeGreaterThan(0);
        replays.Count(replay => Holds(replay).Contains(1)).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["state"][2] != 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["state"][4] > 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["state"][5] > 0).ShouldBeGreaterThan(0);
        replays.Count(replay => Gathered(replay) > 0 && replay.Inputs["active"][Gathered(replay) - 1] == Gathered(replay) - 1)
            .ShouldBeGreaterThan(0);
        replays.Count(replay => Gathered(replay) > 0 && replay.Inputs["active"][Gathered(replay) - 1] != Gathered(replay) - 1)
            .ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["matrix"].Any(lane => double.IsNaN(BitConverter.Int64BitsToDouble(lane))))
            .ShouldBeGreaterThan(0);
    }

    private static long[] Holds(IvpReplayCase replay) => replay.Outputs["holds"].Take((int)replay.Inputs["size"][0]).ToArray();

    private static int Gathered(IvpReplayCase replay) => (int)replay.Inputs["gathered"][0];

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpComplementarityReplay.Parse(reader);
    }
}
