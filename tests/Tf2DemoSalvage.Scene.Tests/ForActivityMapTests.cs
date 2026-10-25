using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// B584: <see cref="PropModels.SkinnedModel.ForActivity"/> answers from a map built once, and must
/// answer exactly what the per-call scan it replaced answered.
/// </summary>
/// <remarks>
/// The oracle <see cref="Scan"/> is the engine's tuple weight (`studio.cpp:1822-1829`: abs, 0 becomes
/// 1) under our deterministic pick: highest weight, strictly greater so the first of a tie wins, an
/// empty activity name never chosen, names compared ignoring case. The engine rolls
/// <c>RandomInt</c> over those weights (`animation.cpp:308-335`); we take the heaviest instead so a
/// demo replays the same way twice, which is what the engine's own <c>SelectHeaviestSequence</c>
/// (`animation.cpp:413-437`) does apart from its weight-0 rule.
/// </remarks>
[TestFixture]
public sealed class ForActivityMapTests
{
    private static readonly string[] Names = ["ACT_MP_RUN", "act_mp_run", "ACT_MP_STAND_IDLE", "ACT_DIERAGDOLL", "", "Act_Mp_Run_Melee"];

    [Test]
    public void ForActivity_OnRandomModels_EqualsTheOldScanForEveryName()
    {
        // A fixed linear congruential sequence rather than Random: the same 200 models every run.
        uint state = 584;
        int Next(int below)
        {
            state = (state * 1664525u) + 1013904223u;
            return (int)(state >> 8) % below;
        }

        int compared = 0;

        for (int model = 0; model < 200; model++)
        {
            (string Activity, int Weight)[] sequences =
            [
                .. Enumerable.Range(0, Next(40))
                    .Select(_ => (Names[Next(Names.Length)], Next(6) - 2)),
            ];
            PropModels.SkinnedModel built = Build(sequences);

            foreach (string name in Names.Append("ACT_NOT_ON_THIS_MODEL"))
            {
                built.ForActivity(name).ShouldBe(Scan(sequences, name), $"model {model}, '{name}'");
                compared++;
            }
        }

        compared.ShouldBe(1400);
    }

    [Test]
    public void ForActivity_TieOnHighestWeight_PicksTheFirst()
    {
        PropModels.SkinnedModel built = Build([("ACT_A", 1), ("act_a", 3), ("ACT_A", 3), ("ACT_A", 2)]);

        built.ForActivity("Act_A").ShouldBe(1);
    }

    /// <summary>
    /// Conformance: `studio.cpp:1822-1829` stores each tuple's weight as <c>iabs(actweight)</c> and
    /// turns 0 into 1 ("We can't have weights of 0"), and `animation.cpp:329-335` walks those tuples,
    /// so a sequence of weight 0 or below IS selectable. Only an empty activity name is not one
    /// (`animation.cpp:146`, `studio.cpp:1723`).
    /// </summary>
    [Test]
    public void ForActivity_OnlyWeightZeroOrNegativeCandidates_PicksTheHeaviestByAbsoluteWeight()
    {
        Build([("ACT_A", 0), ("ACT_A", -1)]).ForActivity("ACT_A").ShouldBe(0);
        Build([("ACT_A", 0), ("ACT_A", -3), ("ACT_A", 2)]).ForActivity("ACT_A").ShouldBe(1);
        Build([("", 5)]).ForActivity("").ShouldBe(-1);
    }

    [Test]
    public void ForActivity_AfterAWithCopyReplacesTheTables_AnswersFromTheNewTables()
    {
        PropModels.SkinnedModel first = Build([("ACT_A", 1), ("ACT_B", 1)]);
        first.ForActivity("ACT_B").ShouldBe(1);

        PropModels.SkinnedModel other = Build([("ACT_B", 1)]);
        PropModels.SkinnedModel copy = first with { Sequences = other.Sequences, Groups = other.Groups };

        copy.ForActivity("ACT_B").ShouldBe(0);
    }

    private static int Scan((string Activity, int Weight)[] sequences, string activity)
    {
        int best = -1;
        int bestWeight = 0;

        for (int sequence = 0; sequence < sequences.Length; sequence++)
        {
            (string candidate, int weight) = sequences[sequence];

            if (candidate.Length == 0 || !string.Equals(candidate, activity, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            weight = Math.Max(1, Math.Abs(weight));

            if (weight > bestWeight)
            {
                bestWeight = weight;
                best = sequence;
            }
        }

        return best;
    }

    private static PropModels.SkinnedModel Build((string Activity, int Weight)[] named)
    {
        List<StudioSequence> sequences =
        [
            .. named.Select((sequence, index) => new StudioSequence(
                Animation: index,
                Flags: 0,
                Label: $"seq{index}",
                Blend: null,
                Activity: sequence.Activity,
                ActivityWeight: sequence.Weight)),
        ];

        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups = [(0, sequences)];

        return new PropModels.SkinnedModel(
            Bones: [],
            Models: [[]],
            Sequences: StudioSequenceTable.Merge(groups),
            Groups: groups,
            PoseParameters: [],
            MasterPose: []);
    }
}
