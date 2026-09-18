using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Probe.Oracle;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// IVP's larger mindist — built by the pair refresh, frozen, collided, told its hull passed, and deleted — replayed against what the shipped
/// binary left (B369, D172).
/// </summary>
/// <remarks>
/// **Every expected lane was written by Valve's binary, not by this project.** The `vphysics-recursive-mindist` probe's `fixture` mode loaded
/// the game's x64 `vphysics.dll`, built each case's pair through <c>IvpPairMindists::Refresh</c>, called the larger mindists' own slots 7, 8
/// and 1 and their destructors, and wrote what they left to `Data/ivp-recursive-mindist.txt`. <see cref="IvpRecursiveMindistReplay"/> is the
/// one definition of the lanes.
/// </remarks>
public sealed class IvpRecursiveMindistReplayConformanceTests
{
    private static readonly string FixturePath = Path.Combine(AppContext.BaseDirectory, "Data", "ivp-recursive-mindist.txt");

    private static readonly Lazy<IReadOnlyList<IvpReplayCase>> Replays = new(Load);

    /// <summary>One test per case the binary was given, named by its label.</summary>
    /// <returns>The cases' labels.</returns>
    public static IEnumerable<TestCaseData> Cases() =>
        Replays.Value.Select(replay => new TestCaseData(replay.Label).SetName($"RecursiveMindist_Case{replay.Label}_LeavesTheBinarysBits"));

    [TestCaseSource(nameof(Cases))]
    public void RecursiveMindist_ACaseTheBinaryWasGiven_LeavesItsBits(string label)
    {
        IvpReplayCase replay = Replays.Value.Single(candidate => candidate.Label == label);

        IvpRecursiveMindistReplay.Differences(replay.Outputs, IvpRecursiveMindistReplay.Run(replay.Inputs)).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The control on the fixture itself**: the binary must have opened larger mindists through both slot 7 and slot 8, collided on real
    /// geometry, sent a pair invalid past the limit, closed an opened pair and refreshed one when told its hull passed, and held mindists
    /// beneath an opened one — so every branch the port claims was reached by a case the binary answered.
    /// </remarks>
    [Test]
    public void Fixture_TheCasesTheBinaryWasGiven_OpenCloseRefreshAndInvalidateEveryWay()
    {
        List<(int Action, long Flags, long Members, long[] Events)> acted = [];

        foreach (IvpReplayCase replay in Replays.Value)
        {
            for (int step = 1; step < IvpRecursiveMindistReplay.StepCount; step++)
            {
                if (replay.Outputs["chosen"][step] >= 0)
                {
                    long[] events = replay.Outputs["events"].Skip(step * 24).Take((int)Math.Min(replay.Outputs["event-count"][step], 24)).ToArray();

                    acted.Add(((int)replay.Inputs["action"][step], replay.Outputs["chosen-flags"][step], replay.Outputs["member-count"][step], events));
                }
            }
        }

        acted.Count(a => a.Action == IvpRecursiveMindistReplay.ActionFreeze && State(a.Flags) == IvpMindistHull.RecursiveState).ShouldBeGreaterThan(0);
        acted.Count(a => a.Action == IvpRecursiveMindistReplay.ActionFreeze && State(a.Flags) == IvpMindistHull.InvalidState).ShouldBeGreaterThan(0);
        acted.Count(a => a.Action == IvpRecursiveMindistReplay.ActionCollide && State(a.Flags) == IvpMindistHull.RecursiveState).ShouldBeGreaterThan(0);
        acted.Count(a => a.Action == IvpRecursiveMindistReplay.ActionCollide && a.Events.Any(e => e >> 16 == IvpRecursiveMindistReplay.Collided)).ShouldBeGreaterThan(0);
        acted.Count(a => a.Action == IvpRecursiveMindistReplay.ActionHullPassed && State(a.Flags) == IvpMindistHull.ExactState).ShouldBeGreaterThan(0);
        acted.Count(a => a.Action == IvpRecursiveMindistReplay.ActionHullPassed && State(a.Flags) == IvpMindistHull.RecursiveState).ShouldBeGreaterThan(0);
        acted.Count(a => a.Members > 0).ShouldBeGreaterThan(0);
    }

    private static int State(long flags) => (int)flags & IvpMindistHull.StateMask;

    private static IReadOnlyList<IvpReplayCase> Load()
    {
        using StreamReader reader = File.OpenText(FixturePath);

        return IvpRecursiveMindistReplay.Parse(reader);
    }
}
