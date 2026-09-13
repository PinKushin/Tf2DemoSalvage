using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's impact solver — <c>FUN_18008e290</c> and every routine under it — replayed against what the shipped binary left
/// (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-impact` probe loaded the game's x64
/// `vphysics.dll`, wrote each case's inputs into fabricated solver, core and environment structs at the offsets
/// `docs/findings/51` reads — with the image's own anomaly-manager table, and the game's freeze answer and the object's shadow
/// query as callbacks — called `FUN_18008e290`, and wrote every field it left to `Data/ivp-impact-solver.txt`.
///
/// **Each case compares every lane**: the separating speed, both virtual masses, the hold-back flag, each side's working
/// velocity and spin, the last push's changes, the relative velocity, the push and its fallback, the record's copy of the
/// relative velocity, both cores' flags, counts, velocities and pending velocities, the environment's three counters and
/// which cores took the impact. <see cref="IvpImpactReplay"/> is the one definition of the lanes, shared with the probe.
/// </remarks>
public sealed class IvpImpactSolverConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-impact-solver.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"Solve_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void Solve_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpImpactReplay.Differences(replay.Outputs, IvpImpactReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: cases that all took one branch would pass a port missing the other. The approaching
    /// branch is the one that leaves the push at the reversed normal; the separating branch never writes it. A held-back core
    /// counts at `env+0xa4`, a frozen pair at `env+0xac`.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_ReachTheBranchesTheLanesShow()
    {
        IReadOnlyList<IvpReplayCase> replays = Replays.Value;

        replays.Count(Approaching).ShouldBeGreaterThan(0);
        replays.Count(replay => !Approaching(replay)).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["counters"][1] > 0).ShouldBeGreaterThan(0);
        replays.Count(replay => replay.Outputs["counters"][2] > 0).ShouldBeGreaterThan(0);
    }

    private static bool Approaching(IvpReplayCase replay)
    {
        long[] normal = replay.Inputs["normal"];

        return replay.Outputs["push"].SequenceEqual(normal.Select(lane => lane ^ 0x80000000L));
    }

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpImpactReplay.Parse(reader);
    }
}
