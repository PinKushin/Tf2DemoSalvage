using System.Collections.Generic;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A gesture the demo raised reaches the skeleton as a layer.
/// </summary>
/// <remarks>
/// **Three layers of this were built before anything drew, and each was invisible on its own**
/// (B282). The decode reads the temp entities, the timeline fills gesture slots, the scene resolves
/// an activity to a sequence, and the pose accumulates it — and the whole chain reported success
/// with a green suite while producing nothing, because the resolution step asked the wrong
/// question. `SkinnedModel.Find` matches a sequence LABEL; the engine resolves a gesture through
/// `SelectWeightedSequence( iGestureActivity )`, which matches the ACTIVITY. No sequence is
/// labelled `ACT_MP_GESTURE_FLINCH_CHEST`, so every gesture on every model resolved to −1.
///
/// **Measured, and only by looking at the output**: three gestures reaching a real player's drawn
/// prop and zero layers on the skeleton. Nothing below this level could see it — the feed's tests
/// were green, the pose-layer conformance tests were green, and the two were connected by a lookup
/// that always failed.
///
/// So these assert on <see cref="EntityModelSet.LayersOf"/>: what the skeleton was HANDED, carried
/// rather than recomputed (B243).
/// </remarks>
public sealed class GestureLayerWiringTests
{
    [Test]
    public void Instances_ForAPlayerWithAFreshGesture_HandsTheSkeletonALayer()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [Reloading(startedSeconds: 0d)];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 0.05d);

        models.LayersOf(4).ShouldNotBeNull().Count.ShouldBe(
            1,
            "a gesture resolves through SelectWeightedSequence on the ACTIVITY, and a fresh one " +
            "must reach the skeleton as a layer");
    }

    /// <remarks>
    /// **The control that separates a working resolution from a permanently empty one.** A gesture
    /// naming an activity this model does not have is abandoned by the engine —
    /// <c>if ( iGestureSequence &lt;= 0 ) return;</c> (<c>multiplayer_animstate.cpp:634</c>) — rather
    /// than substituted. Without this, a lookup that returned the same sequence for everything
    /// would pass the test above.
    /// </remarks>
    [Test]
    public void Instances_ForAGestureThisModelCannotPlay_HandsNoLayer()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn =
        [
            Reloading(startedSeconds: 0d) with
            {
                Pose = Reloading(0d).Pose with
                {
                    Gestures =
                    [
                        new SceneGesture(
                            GestureSlot.AttackAndReload,
                            "ACT_MP_NOTHING_LIKE_THIS",
                            null,
                            AutoKill: true,
                            0d),
                    ],
                },
            },
        ];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 0.05d);

        models.LayersOf(4).ShouldNotBeNull().ShouldBeEmpty(
            "the engine abandons a gesture whose activity the model does not have");
    }

    /// <remarks>
    /// **An auto-killing gesture past its end is gone, not held.**
    /// <c>UpdateGestureLayer</c>: <c>if ( flCycle &gt; 1.0f ) { if ( m_bAutoKill )
    /// ResetGestureSlot( … ); }</c> (<c>multiplayer_animstate.cpp:1294</c>). This is the common
    /// case in a real recording — a slot holds the LAST event ever raised for that player, so most
    /// of the time it is minutes old and must draw nothing.
    /// </remarks>
    [Test]
    public void Instances_ForAnExpiredAutoKillGesture_HandsNoLayer()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [Reloading(startedSeconds: 0d)];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 300d);

        models.LayersOf(4).ShouldNotBeNull().ShouldBeEmpty(
            "an auto-killing gesture whose cycle has passed one is reset, not clamped");
    }

    /// <remarks>
    /// **A gesture that does NOT auto-kill holds its last frame instead of vanishing**, which is
    /// what the <c>_BEGIN</c> gestures are for: a stun or a sniper's pre-fire stays up until
    /// something ends it. Same input, opposite flag, opposite answer — which is what makes the
    /// test above a statement about the flag rather than about time.
    /// </remarks>
    [Test]
    public void Instances_ForAnExpiredHoldingGesture_StillHandsALayer()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn =
        [
            Reloading(startedSeconds: 0d) with
            {
                Pose = Reloading(0d).Pose with
                {
                    Gestures =
                    [
                        new SceneGesture(
                            GestureSlot.AttackAndReload,
                            "ACT_MP_RELOAD_STAND",
                            null,
                            AutoKill: false,
                            0d),
                    ],
                },
            },
        ];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 300d);

        models.LayersOf(4).ShouldNotBeNull().Count.ShouldBe(
            1, "without auto-kill the cycle clamps to one and the gesture holds its last frame");
    }

    /// <summary>A player prop carrying one fresh reload gesture.</summary>
    private static SceneProp Reloading(double startedSeconds) =>
        new(
            4,
            "models/player/scout.mdl",
            ScenePropTrack.Classify("models/player/scout.mdl"),
            new ScenePose
            {
                Sequence = 0,
                Speed = 320f,
                Flags = PlayerActivityState.OnGround,
                Slot = "PRIMARY",
                Gestures =
                [
                    new SceneGesture(
                        GestureSlot.AttackAndReload,
                        "ACT_MP_RELOAD_STAND",
                        null,
                        AutoKill: true,
                        startedSeconds),
                ],
            },
            null,
            ClientSideAnimated: true);

    /// <summary>A model carrying the reload activity plus a run to be the base sequence.</summary>
    /// <remarks>
    /// **Sequence 0 must not be the gesture**, because <c>LayersFor</c> abandons anything resolving
    /// to zero or below, exactly as the engine does. `SyntheticSkinnedModel.With` numbers its
    /// sequences in the order given, so the reload is second.
    /// </remarks>
    private static PropModels.ModelFrames Frames()
    {
        // **The model declares the REWRITTEN name, as a real one does** (B284). A gesture names
        // `ACT_MP_RELOAD_STAND` and the weapon in hand rewrites it through Valve's own
        // `acttable_t` — `WeaponActivityTable` here — so what a scout's model actually declares is
        // `ACT_MP_RELOAD_STAND_PRIMARY`. A fixture carrying the generic name would pass against
        // code that skipped the rewrite, which is the defect this whole area was.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With(
            "ACT_MP_RUN_PRIMARY", "ACT_MP_RELOAD_STAND_PRIMARY", "ACT_MP_STAND_PRIMARY");

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
            },
            [0],
            [true],

            // **Real studio bytes, because a gesture's whole lifetime is arithmetic on its rate.**
            // With the default empty bytes every sequence reports one frame at zero cycles a
            // second, so a gesture's cycle never leaves zero and an expiry test cannot fail.
            Skinned: model with { Models = [AnimatedStudioBytes.OneSecondLoop(animations: 3)] });
    }
}
