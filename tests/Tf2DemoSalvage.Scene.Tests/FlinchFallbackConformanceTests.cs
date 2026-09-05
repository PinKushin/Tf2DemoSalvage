using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A flinch the model cannot play falls back to the CHEST flinch (B350).
/// </summary>
/// <remarks>
/// **`PlayFlinchGesture` substitutes before it starts the gesture**
/// (<c>multiplayer_animstate.cpp:371</c>):
///
/// <code>
///   if ( iActivity != ACT_MP_GESTURE_FLINCH_CHEST &amp;&amp;
///        GetBasePlayer()->SelectWeightedSequence( iActivity ) == -1 )
///       RestartGesture( GESTURE_SLOT_FLINCH, ACT_MP_GESTURE_FLINCH_CHEST );
///   else
///       RestartGesture( GESTURE_SLOT_FLINCH, iActivity );
/// </code>
///
/// **This is the exception to the abandonment rule, not an addition to it.** `AddToGestureSlot`
/// drops any gesture whose activity resolves to nothing — `if ( iGestureSequence &lt;= 0 ) return;`
/// (<c>:634</c>) — and this project reproduced that faithfully for every gesture including flinches.
/// But the flinch never reaches that check with an unresolvable activity, because
/// `PlayFlinchGesture` has already swapped it.
///
/// **Measured, and it is most of them.** TF2's class models declare only
/// `ACT_MP_GESTURE_FLINCH_CHEST` in the merged table — no HEAD, LEFTARM, RIGHTARM, LEFTLEG or
/// RIGHTLEG. In `tf2-2026-pub-pov-cheater` the `CTEPlayerAnimEvent` stream fires 55 chest flinches
/// and **69 non-chest ones** (8 head, 36 left arm, 7 right arm, 18 left leg), every one of which
/// was dropped where the engine plays the chest animation.
/// </remarks>
public sealed class FlinchFallbackConformanceTests
{
    /// <summary>The activity every class model does declare.</summary>
    private const string Chest = "ACT_MP_GESTURE_FLINCH_CHEST";

    /// <summary>One the class models do not.</summary>
    private const string Head = "ACT_MP_GESTURE_FLINCH_HEAD";

    [Test]
    public void Instances_ForAFlinchTheModelLacks_PlaysTheChestFlinch()
    {
        EntityModelSet models = Posed(Flinching(Head));

        IReadOnlyList<PoseLayer> layers = models.LayersOf(9).ShouldNotBeNull();

        layers.Count.ShouldBe(1, "the engine substitutes rather than abandoning");

        layers[0].Sequence.ShouldBe(
            ChestSequence, "and what it substitutes is the CHEST flinch");
    }

    /// <remarks>
    /// **The control that says the substitution is not unconditional.** A flinch the model DOES
    /// have must play its own animation — otherwise a fallback that always used chest would satisfy
    /// the test above while throwing away every specific flinch a model does declare.
    /// </remarks>
    [Test]
    public void Instances_ForAFlinchTheModelHas_PlaysThatOne()
    {
        EntityModelSet models = Posed(Flinching(Chest));

        IReadOnlyList<PoseLayer> layers = models.LayersOf(9).ShouldNotBeNull();

        layers.Count.ShouldBe(1);
        layers[0].Sequence.ShouldBe(ChestSequence);
    }

    /// <remarks>
    /// **The other control, and it is the one that keeps the abandonment rule intact.** A
    /// NON-flinch gesture the model cannot play is still dropped — `AddToGestureSlot`'s
    /// `if ( iGestureSequence &lt;= 0 ) return;` is untouched by this, and a fallback applied to
    /// every slot would make a missing reload play a flinch.
    /// </remarks>
    [Test]
    public void Instances_ForANonFlinchGestureTheModelLacks_StillDropsIt()
    {
        EntityModelSet models = Posed(
            Gesturing(GestureSlot.AttackAndReload, "ACT_MP_RELOAD_STAND"));

        models.LayersOf(9).ShouldNotBeNull().ShouldBeEmpty(
            "the engine abandons a gesture whose activity the model does not have");
    }

    /// <remarks>
    /// **A model without even the chest flinch drops the gesture**, because the substitution has
    /// nothing to substitute. The engine reaches `AddToGestureSlot` with `ACT_MP_GESTURE_FLINCH_CHEST`
    /// and abandons on the same `&lt;= 0` as everything else.
    /// </remarks>
    [Test]
    public void Instances_ForAFlinchOnAModelWithNoFlinchAtAll_DropsIt()
    {
        EntityModelSet models = Posed(Flinching(Head), flinches: false);

        models.LayersOf(9).ShouldNotBeNull().ShouldBeEmpty();
    }

    /// <remarks>
    /// **Sequence ZERO is "no answer", which is surprising and load-bearing.**
    /// `if ( iGestureSequence &lt;= 0 ) return;` (<c>multiplayer_animstate.cpp:634</c>) abandons on
    /// zero as well as on -1, even though zero is an ordinary sequence index everywhere else. So a
    /// model whose only chest flinch sits first plays no flinch at all.
    ///
    /// **Added after sabotage**: weakening that guard to `&lt; 0` reddened nothing, because every
    /// other fixture here deliberately puts the activity at index one to avoid the ambiguity. The
    /// boundary needed its own case rather than being designed around.
    /// </remarks>
    [Test]
    public void Instances_ForAFlinchResolvingToSequenceZero_IsAbandoned()
    {
        EntityModelSet models = Posed(Flinching(Chest), chestFirst: true);

        models.LayersOf(9).ShouldNotBeNull().ShouldBeEmpty(
            "the engine treats a gesture sequence of zero as no answer");
    }

    /// <summary>Where the chest flinch sits in the fixture's merged table.</summary>
    /// <remarks>
    /// **Index one, not zero.** `SelectWeightedSequence`'s result is abandoned on `&lt;= 0`, so a
    /// fixture that put the activity first would be indistinguishable from one that failed to
    /// resolve it — the engine treats sequence zero as no answer.
    /// </remarks>
    private const int ChestSequence = 1;

    private static EntityModelSet Posed(
        SceneProp prop, bool flinches = true, bool chestFirst = false)
    {
        PropModels.ModelFrames model = Frames(flinches, chestFirst);
        EntityModelSet models = new() { Geometry = _ => model };

        List<SceneProp> drawn = [prop];

        models.Add(drawn, _ => model);
        models.Instances(drawn, [], seconds: 0.05d);

        return models;
    }

    private static SceneProp Flinching(string activity) =>
        Gesturing(GestureSlot.Flinch, activity);

    private static SceneProp Gesturing(GestureSlot slot, string activity) =>
        new(
            9,
            "models/player/scout.mdl",
            SceneModelKind.Studio,
            new ScenePose
            {
                Sequence = 0,
                Cycle = 0f,
                Gestures = [new SceneGesture(slot, activity, null, false, 0d)],
            },
            null);

    private static PropModels.ModelFrames Frames(bool flinches, bool chestFirst = false)
    {
        PropModels.SkinnedModel model = (flinches, chestFirst) switch
        {
            (_, true) => SyntheticSkinnedModel.With(Chest, "idle", "other"),
            (true, _) => SyntheticSkinnedModel.With("idle", Chest, "other"),
            _ => SyntheticSkinnedModel.With("idle", "other", "another"),
        };

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
            {
                [0] = (0, 1, 0f),
                [1] = (0, 1, 0f),
                [2] = (0, 1, 0f),
            },
            [0],
            [true],
            Skinned: model);
    }
}
