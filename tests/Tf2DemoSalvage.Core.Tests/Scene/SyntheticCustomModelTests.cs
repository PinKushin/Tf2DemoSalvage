using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A player's <c>m_iszCustomModel</c>, carried by an authored demo to the scene (B763).</summary>
/// <remarks>
/// `CTFPlayerClassShared::GetModelName` returns it in place of the class's model whenever it is not empty
/// (`tf_playerclass_shared.cpp:136`). A Mann vs. Machine robot is a player with one. The corpus has no MvM match, so the
/// specimen is authored: the real container, schema, entity decode and timeline, with the value put there by the test.
/// An empty string, the wire's own default, is no custom model: the control.
/// </remarks>
public sealed class SyntheticCustomModelTests
{
    [TestCase("models/bots/soldier/bot_soldier.mdl", "models/bots/soldier/bot_soldier.mdl", TestName = "Build_ACustomModelSent_ReachesTheScenePlayer")]
    [TestCase("", null, TestName = "Build_AnEmptyCustomModel_IsNone")]
    public void Build_ACustomModel_ReachesTheScenePlayer(string sent, string? expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround) { CustomModel = sent }));

        timeline.Frames.Single(frame => frame.Tick == 100).Players.Single().CustomModel.ShouldBe(expected);
    }

    /// <remarks>
    /// **A custom model's offset and its visibility to its own player reach the scene** (B765): `GetRenderOrigin` adds
    /// `m_vecCustomModelOffset` (`c_tf_player.cpp:10697-10703`), and `ShouldDraw` hides a local player whose custom model
    /// is not `m_bCustomModelVisibleToSelf` (`:8008-8012`). Only the recorder is hidden from himself: `IsLocalPlayer()` is
    /// the test's first half. Another slot is the control.
    /// </remarks>
    [TestCase(0, true, TestName = "Build_TheRecordersCustomModelNotVisibleToSelf_IsHiddenFromHim")]
    [TestCase(4, false, TestName = "Build_AnotherPlayersCustomModelNotVisibleToSelf_IsNot")]
    public void Build_ACustomModelsOffsetAndVisibility_ReachTheScenePlayer(byte recorderSlot, bool hidden)
    {
        ScenePlayer player = Single(new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround)
        {
            CustomModel = Robot,
            CustomModelOffset = (1f, -2f, 30f),
            CustomModelVisibleToSelf = false,
            RecorderSlot = recorderSlot,
        });

        (player.CustomModelOffset, player.CustomModelHiddenFromSelf).ShouldBe(((1f, -2f, 30f), hidden));
    }

    /// <remarks>
    /// **A custom model without the class's animations is turned by `CTFPlayerAnimState::Update`'s early branch**
    /// (`tf_playeranimstate.cpp:340-366`) rather than by the feet: `m_angRender` is the set rotation whole, or the eye yaw
    /// with no pitch or roll, or, when it does not rotate, left as it was: zero from the start, as `m_angRender.Init()`
    /// makes it (`multiplayer_animstate.cpp:56`). A rotation's yaw past 180 is the same facing normalised.
    /// </remarks>
    [TestCase(true, true, 90f, 10f, 90f, 5f, TestName = "Build_ARobotWithASetRotation_IsDrawnAtIt")]
    [TestCase(true, true, 270f, 10f, -90f, 5f, TestName = "Build_ARobotWithASetRotationPast180_IsDrawnAtItNormalised")]
    [TestCase(true, false, 90f, 0f, 45f, 0f, TestName = "Build_ARobotThatRotatesWithNoneSet_IsDrawnAtItsEyeYaw")]
    [TestCase(false, true, 90f, 0f, 0f, 0f, TestName = "Build_ARobotThatNeverRotated_IsDrawnAtZero")]
    public void Build_ACustomModelsRenderAngles_AreTheAnimStates(
        bool rotates, bool set, float setYaw, float pitch, float yaw, float roll)
    {
        ScenePlayer player = Single(new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround)
        {
            CustomModel = Robot,
            CustomModelRotates = rotates,
            CustomModelRotationSet = set,
            CustomModelRotation = (10f, setYaw, 5f),
            EyeYaw = 45f,
        });

        // The eye yaw travels in 12 bits over 360 degrees, so it arrives within a tenth.
        player.RenderPitch.ShouldBe(pitch, 0.1f);
        player.Yaw.ShouldBe(yaw, 0.1f);
        player.RenderRoll.ShouldBe(roll, 0.1f);
    }

    /// <remarks>
    /// **A custom model that stops rotating keeps the angles it was last given** (`m_angRender` is only written inside
    /// `if ( CustomModelRotates() )`). A tick at eye yaw 45 that rotates, then one at 120 that does not: 45 both times.
    /// </remarks>
    [Test]
    public void Build_ARobotThatStopsRotating_KeepsItsLastAngles()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround) { CustomModel = Robot, EyeYaw = 45f },
            new SyntheticPlayer.GestureSnapshot(101, 0f, PlayerActivityState.OnGround)
            {
                CustomModel = Robot, EyeYaw = 120f, CustomModelRotates = false,
            }));

        timeline.Frames.Single(frame => frame.Tick == 101).Players.Single().Yaw.ShouldBe(45f, 0.5f);
    }

    /// <remarks>
    /// **The class's path writes only the yaw** (`multiplayer_animstate.cpp:1765`), so a set rotation's pitch and roll
    /// outlive the custom branch: a robot rotated at (10, 90, 5) that takes the class's animations a tick later keeps
    /// pitch 10 and roll 5. The control is a robot on the class's animations from the start: zero.
    /// </remarks>
    [TestCase(false, 10f, 5f, TestName = "Build_ARobotThatTakesTheClasssAnimations_KeepsItsRotationsPitchAndRoll")]
    [TestCase(true, 0f, 0f, TestName = "Build_ARobotOnTheClasssAnimationsThroughout_HasNone")]
    public void Build_TheClasssPath_LeavesThePitchAndRollAsTheyStood(bool classAnimationsFirst, float pitch, float roll)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround)
            {
                CustomModel = Robot, UsesClassAnimations = classAnimationsFirst, CustomModelRotationSet = true,
                CustomModelRotation = (10f, 90f, 5f),
            },
            new SyntheticPlayer.GestureSnapshot(101, 0f, PlayerActivityState.OnGround)
            {
                CustomModel = Robot, UsesClassAnimations = true, CustomModelRotationSet = true,
                CustomModelRotation = (10f, 90f, 5f),
            }));

        ScenePlayer player = timeline.Frames.Single(frame => frame.Tick == 101).Players.Single();
        (player.RenderPitch, player.RenderRoll).ShouldBe((pitch, roll));
    }

    /// <remarks>
    /// **A new player in a reused slot starts from a new anim state**, whose `m_angRender` is zero
    /// (`multiplayer_animstate.cpp:56`). The slot's first occupant is rotated to 170; the next, a new serial that does not
    /// rotate, is drawn at zero. The same serial is the control: he keeps 170.
    /// </remarks>
    [TestCase(2, 0f, TestName = "Build_ANewPlayerInAReusedSlotWhoDoesNotRotate_IsDrawnAtZero")]
    [TestCase(1, 170f, TestName = "Build_TheSamePlayerWhoStopsRotating_KeepsHisAngles")]
    public void Build_AReusedSlot_StartsItsAnglesAfresh(int secondSerial, float yaw)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround)
            {
                CustomModel = Robot, CustomModelRotationSet = true, CustomModelRotation = (0f, 170f, 0f),
            },
            new SyntheticPlayer.GestureSnapshot(101, 0f, PlayerActivityState.OnGround)
            {
                CustomModel = Robot, CustomModelRotates = false, Serial = secondSerial,
            }));

        timeline.Frames.Single(frame => frame.Tick == 101).Players.Single().Yaw.ShouldBe(yaw, 0.5f);
    }

    /// <remarks>
    /// **The per-frame read keeps the anim state's angles** rather than turning the feet toward the eyes, since the
    /// custom-model branch returns before the feet are advanced (`tf_playeranimstate.cpp:362-366`). A rotation of 170
    /// against eyes at 45 is far past any feet tolerance, so a feet turn would move it. The eye-yaw branch follows the
    /// drawn eyes.
    /// </remarks>
    [TestCase(true, 170f, TestName = "PlayersAt_ARobotWithASetRotation_KeepsIt")]
    [TestCase(false, 45f, TestName = "PlayersAt_ARobotWithNoRotationSet_FollowsTheEyes")]
    public void PlayersAt_ACustomModel_KeepsTheAnimStatesYaw(bool set, float expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            [.. Enumerable.Range(100, 40).Select(tick => new SyntheticPlayer.GestureSnapshot(tick, 0f, PlayerActivityState.OnGround)
            {
                CustomModel = Robot,
                CustomModelRotationSet = set,
                CustomModelRotation = (0f, 170f, 0f),
                EyeYaw = 45f,
            })]));

        Played(timeline).Yaw.ShouldBe(expected, 0.5f);
    }

    /// <remarks>
    /// **The eye-yaw branch follows the eyes as drawn this frame**, interpolated, not the tick's arrived ones: the anim state
    /// is handed `m_angEyeAngles` as the interpolator left it (`c_tf_player.cpp:4287`). Eyes turning three degrees a tick
    /// lag the arrived value by the interpolation delay, the control that the two differ.
    /// </remarks>
    [Test]
    public void PlayersAt_ARobotTurningWithNoRotationSet_IsDrawnAtTheDrawnEyes()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            [.. Enumerable.Range(100, 40).Select(tick => new SyntheticPlayer.GestureSnapshot(tick, 0f, PlayerActivityState.OnGround)
            {
                CustomModel = Robot,
                EyeYaw = (tick - 100) * 3f,
            })]));

        ScenePlayer drawn = Played(timeline);
        float eyes = drawn.EyeYaw.ShouldNotBeNull();

        MathF.Abs(timeline.PlayersAt(130).Single().Yaw - eyes).ShouldBeGreaterThan(1f, "the control: arrived and drawn eyes differ");
        drawn.Yaw.ShouldBe(eyes);
    }

    /// <remarks>
    /// **A model that stops rotating holds the yaw last DRAWN**, a frame's, not a tick's: `m_angRender` is per-client state
    /// written each frame. Eyes turning three degrees a tick through tick 120, then no rotation: frame 130 is drawn at
    /// frame 120's interpolated eyes. The tick's held yaw, the arrived eyes, is the control that the two differ.
    /// </remarks>
    [Test]
    public void PlayersAt_ARobotThatStopsRotating_HoldsTheYawLastDrawn()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            [.. Enumerable.Range(100, 40).Select(tick => new SyntheticPlayer.GestureSnapshot(tick, 0f, PlayerActivityState.OnGround)
            {
                CustomModel = Robot,
                EyeYaw = Math.Min(tick, 120) * 3f % 360f,
                CustomModelRotates = tick <= 120,
            })]));
        PlayerFeet feet = new();
        float drawnAt120 = Played(timeline, feet, 101, 120).Yaw;

        ScenePlayer held = Played(timeline, feet, 121, 130);

        MathF.Abs(timeline.PlayersAt(130).Single().Yaw - drawnAt120).ShouldBeGreaterThan(1f, "the control: the tick's held yaw differs");
        held.Yaw.ShouldBe(drawnAt120);
    }

    /// <remarks>The class's animations are the control: the rotation of 170 is never read, so the feet stay by the eyes.</remarks>
    [Test]
    public void PlayersAt_ARobotUsingTheClasssAnimations_IgnoresItsRotation()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false,
            [.. Enumerable.Range(100, 40).Select(tick => new SyntheticPlayer.GestureSnapshot(tick, 0f, PlayerActivityState.OnGround)
            {
                CustomModel = Robot,
                UsesClassAnimations = true,
                CustomModelRotationSet = true,
                CustomModelRotation = (0f, 170f, 0f),
                EyeYaw = 45f,
            })]));

        // The feet are within their 45-degree tolerance of the eyes (`tf_playeranimstate.cpp`'s aim range), never at 170.
        MathF.Abs(Played(timeline).Yaw - 45f).ShouldBeLessThanOrEqualTo(45.5f);
    }

    /// <summary>
    /// The player as played frame by frame, a tick a frame: the feet are carried between calls and seeded from the first,
    /// so only a run of frames shows them turning.
    /// </summary>
    private static ScenePlayer Played(DemoTimeline timeline) => Played(timeline, new PlayerFeet(), 101, 130);

    private static ScenePlayer Played(DemoTimeline timeline, PlayerFeet feet, int from, int to)
    {
        List<ScenePlayer> players = [];

        for (int tick = from; tick <= to; tick++)
        {
            timeline.PlayersAt(tick, players, playerFeet: feet);
        }

        return players.ShouldHaveSingleItem();
    }

    [Test]
    public void Build_ARobotUsingTheClasssAnimations_IgnoresItsRotation()
    {
        ScenePlayer player = Single(new SyntheticPlayer.GestureSnapshot(100, 0f, PlayerActivityState.OnGround)
        {
            CustomModel = Robot,
            UsesClassAnimations = true,
            CustomModelRotationSet = true,
            CustomModelRotation = (10f, 90f, 5f),
            EyeYaw = 45f,
        });

        (player.RenderPitch, player.RenderRoll).ShouldBe((0f, 0f));
        player.Yaw.ShouldNotBe(90f);
    }

    private const string Robot = "models/bots/soldier/bot_soldier.mdl";

    private static ScenePlayer Single(SyntheticPlayer.GestureSnapshot snapshot) =>
        DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(0.015f, SceneTeams.Blu, 3, rules: null, alwaysLoser: false, snapshot))
            .Frames.Single(frame => frame.Tick == snapshot.Tick).Players.Single();

    /// <remarks>
    /// **A corpse takes its player's custom model at death** (`CreateTFRagdoll`, `c_tf_player.cpp:688-691`), captured
    /// with the cosmetics because the corpse itself carries none. An empty one is no custom model, the control.
    /// </remarks>
    [TestCase("models/bots/soldier/bot_soldier.mdl", "models/bots/soldier/bot_soldier.mdl", TestName = "Build_ACorpseOfACustomModelPlayer_CarriesTheModel")]
    [TestCase("", null, TestName = "Build_ACorpseOfAnOrdinaryPlayer_CarriesNone")]
    public void Build_ACorpse_CarriesItsPlayersCustomModel(string sent, string? expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(3, sent));

        timeline.Corpses.ShouldHaveSingleItem().CustomModel.ShouldBe(expected);
        timeline.CustomModels.ShouldBe(expected is null ? [] : [expected], "every custom model a player carried, to be loaded");
    }

    /// <remarks>
    /// **Captured once, as `CreateTFRagdoll` asks once.** The player's custom model changes a tick after the corpse was
    /// made, and the corpse keeps the one he died with. A capture of no custom model is kept just the same: the control.
    /// </remarks>
    [TestCase("models/bots/soldier/bot_soldier.mdl", "models/bots/heavy/bot_heavy.mdl", "models/bots/soldier/bot_soldier.mdl", TestName = "Build_APlayerWhoseModelChangesAfterDeath_KeepsHisCorpsesModel")]
    [TestCase("", "models/bots/heavy/bot_heavy.mdl", null, TestName = "Build_AHumanWhoBecomesARobotAfterDeath_LeavesHisCorpseHuman")]
    public void Build_ACorpseUpdatedAfterDeath_KeepsTheModelItWasMadeWith(string atDeath, string later, string? expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(3, atDeath, laterCustomModel: later));

        timeline.Corpses.ShouldHaveSingleItem().CustomModel.ShouldBe(expected);
    }

    /// <remarks>
    /// **A robot spy who died disguised to the recorder falls as his class's model** (`ShouldDrawSpyAsDisguised`,
    /// `c_tf_player.cpp:688-699`), read against the recorder's own team, RED, against the BLU spy. He still carries the
    /// custom model, which his pieces are broken from (`:7453-7476`). Undisguised is the control.
    /// </remarks>
    [TestCase(true, TestName = "Build_ARobotDisguisedToTheRecorder_IsDrawnAsHisDisguise")]
    [TestCase(false, TestName = "Build_ARobotUndisguised_IsNot")]
    public void Build_ADisguisedRobotsCorpse_IsDrawnAsShouldDrawSpyAsDisguisedSays(bool disguised)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfACorpse(8, "models/bots/spy/bot_spy.mdl", disguised));

        SceneRagdoll corpse = timeline.Corpses.ShouldHaveSingleItem();
        (corpse.CustomModel, corpse.DrawnAsDisguise).ShouldBe(("models/bots/spy/bot_spy.mdl", disguised));
    }
}
