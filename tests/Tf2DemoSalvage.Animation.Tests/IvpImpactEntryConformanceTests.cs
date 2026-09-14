using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's collision entry — a contact point's materials, its record's estimate, the push-out estimate and the impact solver's
/// entry — replayed against what the shipped binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-impact` probe's `entry-fixture` mode
/// loaded the game's x64 `vphysics.dll`, wrote each case into a fabricated contact point, record, two objects with their triangles
/// and frames, three materials and a material manager whose slots are callbacks, called `FUN_1800908d0`, `FUN_18008db40`,
/// `FUN_18008fca0` and `FUN_18008ed60` in that order — and `FUN_18008fe70` alone on a solver buffer, whose cone tangent a solve
/// can round away — and wrote every field they left to `Data/ivp-impact-entry.txt`. The four `regrouped-cone` cases are the only
/// ones that tell the cone series' product from its regrouping.
/// <see cref="IvpEntryReplay"/> is the one definition of the lanes, shared with the probe.
/// </remarks>
public sealed class IvpImpactEntryConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-impact-entry.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"Enter_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void Enter_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpEntryReplay.Differences(replay.Outputs, IvpEntryReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: cases that all took one side of a branch would pass a port missing the other. A
    /// record past `block[0x48]` is estimated at `1e20f`; a static second core swaps the solver's sides; a ball skips its sweep; a
    /// NaN gap takes both routines' unordered branches; a nonzero material index asks the manager; and the materials' axes run
    /// only when asked for, with an axis friction, on a pair with no `+0x58` core.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachTheBranchesTheLanesShow()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;
        long noEstimate = IvpImpactReplay.Lane(1e20f);

        replays.Count(replay => replay.Outputs["estimate"][1] == noEstimate).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["estimate"][1] != noEstimate).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["movable"][1] == 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["movable"][0] == 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Inputs["kinds"].Contains(3)).ShouldBeGreaterThan(0);
        replays.Count(replay => float.IsNaN(IvpImpactReplay.Real32(replay.Inputs, "gap", 0))).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["materials"].Contains(3)).ShouldBeGreaterThan(0);
        replays.Count(AsksForTheAxes).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["axis-uses"][0] != 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["axis-uses"][0] == 0).ShouldBeGreaterThan(0);
    }

    private static bool AsksForTheAxes(IvpReplayCase replay) =>
        replay.Inputs["uses-material-axes"][0] != 0 &&
        replay.Inputs["first-offset58"][0] == 0 &&
        replay.Inputs["second-offset58"][0] == 0 &&
        replay.Inputs["material-has-second"].Any(flag => flag != 0);

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpEntryReplay.Parse(reader);
    }
}
