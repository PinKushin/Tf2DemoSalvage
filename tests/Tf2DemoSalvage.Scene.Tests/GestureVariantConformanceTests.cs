using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A gesture rolls its variant once, when it starts, and holds it (D233).</summary>
/// <remarks>
/// <code>
/// int iGestureSequence = pPlayer->SelectWeightedSequence( iGestureActivity );
/// if ( iGestureSequence &lt;= 0 ) return;
/// ...
/// m_aGestureSlots[iGestureSlot].m_pAnimLayer->m_nSequence = iGestureSequence;
/// </code>
/// `CMultiPlayerAnimState::AddToGestureSlot` (`multiplayer_animstate.cpp:616-688`) picks by weight once, when the slot
/// is filled, and nothing re-picks while it plays. Ours is stateless per frame, so the roll is seeded on what the
/// gesture carries — the player, the activity, and the moment it started — which makes every frame of it, and a seek
/// into it, land on the same variant.
/// </remarks>
public sealed class GestureVariantConformanceTests
{
    private const string Chest = "ACT_MP_GESTURE_FLINCH_CHEST";
    private const string Head = "ACT_MP_GESTURE_FLINCH_HEAD";
    private const string Wave = "ACT_MP_GESTURE_VARIANT_TEST";
    private const int Entity = 9;

    /// <summary>Four chest flinches at 1-4 and three of another gesture at 5-7, after an idle at 0.</summary>
    private static readonly PropModels.SkinnedModel Model = SyntheticSkinnedModel.WithWeights(
        [("idle", 1), .. Enumerable.Repeat((Chest, 1), 4), .. Enumerable.Repeat((Wave, 1), 3)]);

    [TestCase(GestureSlot.Flinch, Chest, Chest)]
    [TestCase(GestureSlot.Flinch, Head, Chest)]
    [TestCase(GestureSlot.AttackAndReload, Wave, Wave)]
    [TestCase(GestureSlot.Jump, Wave, Wave)]
    public void Instances_AGesture_PlaysTheVariantRolledFromItsStart(GestureSlot slot, string asked, string rolled)
    {
        for (int started = 0; started < 20; started++)
        {
            double start = started * 0.37d;

            LayerAt(slot, asked, start, start + 0.05d).ShouldBe(
                Model.SelectWeightedSequence(rolled, -1, EntityModelSet.ActivityPickSeed(Entity, rolled, BitConverter.DoubleToInt64Bits(start))),
                $"started at {start}");
        }
    }

    /// <summary>Every frame of one gesture, played or sought into, shows the variant it started with.</summary>
    [Test]
    public void Instances_ThroughOneGesture_HoldsItsVariantAndASeekLandsOnIt()
    {
        EntityModelSet played = Loaded();
        int first = Layer(played, GestureSlot.Flinch, Chest, 2d, 2d);

        for (double seconds = 2d; seconds < 3d; seconds += 0.07d)
        {
            Layer(played, GestureSlot.Flinch, Chest, 2d, seconds).ShouldBe(first, $"played at {seconds}");
            LayerAt(GestureSlot.Flinch, Chest, 2d, seconds).ShouldBe(first, $"sought to {seconds}");
        }
    }

    /// <summary>The control: the roll is not one fixed variant, or the tests above would pass on the heaviest-first pick.</summary>
    [Test]
    public void Instances_FlinchesStartedAtDifferentMoments_PickEveryVariant()
    {
        HashSet<int> picked = [.. Enumerable.Range(0, 60).Select(started => LayerAt(GestureSlot.Flinch, Chest, started * 0.1d, started * 0.1d))];

        picked.Order().ShouldBe([1, 2, 3, 4]);
    }

    private static int LayerAt(GestureSlot slot, string activity, double started, double seconds) =>
        Layer(Loaded(), slot, activity, started, seconds);

    private static int Layer(EntityModelSet models, GestureSlot slot, string activity, double started, double seconds)
    {
        List<SceneProp> drawn =
        [
            new(
                Entity,
                "models/player/scout.mdl",
                SceneModelKind.Studio,
                new ScenePose { Gestures = [new SceneGesture(slot, activity, null, false, started)] },
                null),
        ];

        models.Instances(drawn, [], seconds: seconds);
        return models.LayersOf(Entity).ShouldNotBeNull().ShouldHaveSingleItem().Sequence;
    }

    private static EntityModelSet Loaded()
    {
        PropModels.ModelFrames frames = new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            Enumerable.Range(0, 8).ToDictionary(sequence => sequence, _ => (Start: 0, Frames: 1, CyclesPerSecond: 0f)),
            [0],
            [true],
            Skinned: Model);
        EntityModelSet models = new() { Geometry = _ => frames };
        List<SceneProp> drawn = [new(Entity, "models/player/scout.mdl", SceneModelKind.Studio, new ScenePose(), null)];
        models.Add(drawn, _ => frames);
        return models;
    }
}
