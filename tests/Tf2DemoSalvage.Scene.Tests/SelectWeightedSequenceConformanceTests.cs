using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// D233: a player's main sequence is picked the way the engine picks it — at random by weight, once per activity
/// change, then held.
/// </summary>
/// <remarks>
/// <code>
/// int animDesired = SelectWeightedSequence( TranslateActivity( idealActivity ) );
/// if ( GetSequenceActivity( GetSequence() ) == GetSequenceActivity( animDesired ) ) return;
/// </code>
/// `multiplayer_animstate.cpp:1170-1172` rolls every frame and keeps the roll only when the activity changed.
/// The roll is `CActivityToSequenceMapping::SelectWeightedSequence` (`animation.cpp:277-335`): the current
/// sequence is kept when it answers to the activity with a NEGATIVE `actweight` (`:291-297`), otherwise
/// `RandomInt( 0, totalWeight - 1 )` walks the tuples, each weighing `iabs(actweight)` with 0 raised to 1
/// (`studio.cpp:1822-1829`).
///
/// **The one addition is the seed**: the engine's `RandomInt` differs per client run, a viewer that seeks must
/// show the same pick at the same tick, so ours comes from (entity, activity, the tick it began). It changes which
/// random value comes up, never the distribution.
/// </remarks>
[TestFixture]
public sealed class SelectWeightedSequenceConformanceTests
{
    private const string Stand = "ACT_MP_STAND_PRIMARY";
    private const string Run = "ACT_MP_RUN_PRIMARY";

    /// <summary>`animation.cpp:329-335`: the random value walks the cumulative weights; 0 counts as 1.</summary>
    [TestCase(0u, 0)]
    [TestCase(1u, 1)]
    [TestCase(3u, 1)]
    [TestCase(4u, 3)]
    [TestCase(5u, 0)]
    public void SelectWeightedSequence_RandomValue_LandsInItsWeightSlot(uint random, int expected)
    {
        // Weights 1, 3, (other activity), 0 → total 5: slots [0], [1,2,3], [4]; 5 wraps to 0.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithWeights(("ACT_A", 1), ("act_a", -3), ("ACT_B", 9), ("ACT_A", 0));

        model.SelectWeightedSequence("ACT_A", current: -1, random).ShouldBe(expected);
    }

    /// <summary>`animation.cpp:291-297`: the current sequence stays when it answers to the activity with a negative weight.</summary>
    [Test]
    public void SelectWeightedSequence_CurrentOfTheActivityWithNegativeWeight_KeepsIt()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithWeights(("ACT_A", 2), ("ACT_A", -3), ("ACT_B", -1));

        for (uint random = 0; random < 5; random++)
        {
            model.SelectWeightedSequence("ACT_A", current: 1, random).ShouldBe(1);
        }

        // The controls: a positive current rolls, and a negative current of ANOTHER activity rolls.
        model.SelectWeightedSequence("ACT_A", current: 0, 2u).ShouldBe(1);
        model.SelectWeightedSequence("ACT_A", current: 2, 0u).ShouldBe(0);
    }

    [Test]
    public void SelectWeightedSequence_ActivityAbsent_IsMinusOne() =>
        SyntheticSkinnedModel.WithWeights(("ACT_A", 1)).SelectWeightedSequence("ACT_B", -1, 0u).ShouldBe(-1);

    /// <summary>Many picks through the seed converge to the weights — the seed must not bias the roll.</summary>
    [Test]
    public void SelectWeightedSequence_ThroughTheSeed_ConvergesToTheWeights()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithWeights(("ACT_A", 1), ("ACT_A", 3), ("ACT_A", 6));
        int[] counts = new int[3];
        const int Picks = 30000;

        for (int tick = 0; tick < Picks; tick++)
        {
            counts[model.SelectWeightedSequence("ACT_A", -1, EntityModelSet.ActivityPickSeed(7, "ACT_A", tick))]++;
        }

        ((double)counts[0] / Picks).ShouldBe(0.1, 0.01);
        ((double)counts[1] / Picks).ShouldBe(0.3, 0.01);
        ((double)counts[2] / Picks).ShouldBe(0.6, 0.01);
    }

    [Test]
    public void ActivityPickSeed_SameInputs_SameValueAndNamesCompareIgnoringCase()
    {
        EntityModelSet.ActivityPickSeed(3, "ACT_A", 100).ShouldBe(EntityModelSet.ActivityPickSeed(3, "act_a", 100));
        EntityModelSet.ActivityPickSeed(3, "ACT_A", 100).ShouldNotBe(EntityModelSet.ActivityPickSeed(3, "ACT_A", 101));
        EntityModelSet.ActivityPickSeed(3, "ACT_A", 100).ShouldNotBe(EntityModelSet.ActivityPickSeed(4, "ACT_A", 100));
        EntityModelSet.ActivityPickSeed(3, "ACT_A", 100).ShouldNotBe(EntityModelSet.ActivityPickSeed(3, "ACT_B", 100));
    }

    /// <summary>`:1171-1172`: while the activity is unchanged the pick holds, whatever the roll would say.</summary>
    [Test]
    public void UpdateClientSideAnimations_ActivityUnchanged_HoldsThePick()
    {
        EntityModelSet models = Loaded();
        int first = Play(models, 0, speed: 0f);

        for (int tick = 1; tick < 300; tick++)
        {
            Play(models, tick, speed: 0f).ShouldBe(first, $"tick {tick}");
        }
    }

    /// <summary>A new activity rolls again; coming back to stand rolls a fresh stand, keyed on the tick it began.</summary>
    [Test]
    public void UpdateClientSideAnimations_ActivityChanges_RollsAgainKeyedOnTheTickItBegan()
    {
        HashSet<int> stands = [];

        for (int began = 0; began < 40; began++)
        {
            EntityModelSet models = Loaded();
            Play(models, 0, speed: 250f).ShouldBe(8, "the only run");
            int stand = Play(models, began + 1, speed: 0f);

            stand.ShouldBe(StandAt(began + 1), "the same pick a fresh set makes at that tick");
            stands.Add(stand);
        }

        stands.Count.ShouldBeGreaterThan(1, "eight equal variants over forty ticks must not all be one");
    }

    /// <summary>A replay of the same ticks shows the same picks.</summary>
    [Test]
    public void UpdateClientSideAnimations_SameDemoReplayed_SamePicks()
    {
        static int[] Replay(EntityModelSet models) =>
            [.. Enumerable.Range(0, 200).Select(tick => Play(models, tick, speed: tick / 20 % 2 == 0 ? 0f : 250f))];

        Replay(Loaded()).ShouldBe(Replay(Loaded()));
    }

    [Test]
    public void UpdateClientSideAnimations_HeldPath_AllocatesNothing()
    {
        EntityModelSet models = Loaded();
        List<SceneProp> drawn = [Player(0f)];
        models.CurrentTick = 0;
        models.UpdateClientSideAnimations(drawn);
        models.CurrentTick = 1;
        models.UpdateClientSideAnimations(drawn);

        long before = GC.GetAllocatedBytesForCurrentThread();
        models.CurrentTick = 2;
        models.UpdateClientSideAnimations(drawn);
        (GC.GetAllocatedBytesForCurrentThread() - before).ShouldBe(0);
    }

    /// <summary>The cached delegate answers for its own model after a `with` copy, and the record still hashes.</summary>
    [Test]
    public void Declares_AfterAWithCopy_AnswersForTheCopyAndHashes()
    {
        PropModels.SkinnedModel first = SyntheticSkinnedModel.WithWeights(("ACT_A", 1));
        first.Declares("ACT_A").ShouldBeTrue();

        PropModels.SkinnedModel other = SyntheticSkinnedModel.WithWeights(("ACT_B", 1));
        PropModels.SkinnedModel copy = first with { Sequences = other.Sequences, Groups = other.Groups };

        copy.Declares("ACT_A").ShouldBeFalse();
        copy.Declares("ACT_B").ShouldBeTrue();
        first.GetHashCode().ShouldBe(first.GetHashCode());
        first.Equals(copy).ShouldBeFalse();
    }

    private static int StandAt(int tick) => Play(Loaded(), tick, speed: 0f);

    private static int Play(EntityModelSet models, int tick, float speed)
    {
        models.CurrentTick = tick;
        List<SceneProp> drawn = [Player(speed)];
        models.UpdateClientSideAnimations(drawn);
        return drawn[0].Pose.Sequence;
    }

    private static EntityModelSet Loaded()
    {
        EntityModelSet models = new();
        models.Add([Player(0f)], Variants);
        return models;
    }

    /// <summary>Eight equal stand variants, as an idle with several authored takes, and one run.</summary>
    private static PropModels.ModelFrames? Variants(string path) =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: SyntheticSkinnedModel.WithWeights(
                [.. Enumerable.Repeat((Stand, 1), 8), (Run, 1)]));

    private static SceneProp Player(float speed) =>
        new(
            3,
            "models/player/soldier.mdl",
            SceneModelKind.Studio,
            new ScenePose { Scale = 1f, Sequence = 99, Speed = speed });
}
