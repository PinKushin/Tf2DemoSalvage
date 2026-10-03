using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What a player was doing when a gesture event arrived — the air-walk and the loser state — read by the
/// timeline from an authored demo (B112).
/// </summary>
/// <remarks>
/// **The unit tests prove the latch and the loser test in isolation; these prove the timeline feeds them.**
/// Before this, <c>DemoTimeline.PostureOf</c> filled four of the context's seven fields and left
/// <c>InAirWalk</c> and <c>IsLoser</c> at false, so every reload begun mid-rocket-jump stood and every losing
/// scout's air dash was the winner's. Nothing a unit test could see: the mapping was right and nobody called
/// it with the right input.
///
/// **Heights are chosen against the engine's 300.** At 0.015 s a tick, six units is 400 u/s, a blast jump's
/// rise; four is 266.7, an ordinary jump's, which by design never air-walks; one is 66.7, too slow to set the
/// latch again once something has cleared it.
///
/// **An event reads the latch the LAST snapshot left**, because <c>DoAnimationEvent</c> runs before that
/// frame's <c>HandleJumping</c>. The event and the snapshot travel in one packet, and the timeline steps the
/// latch once the packet is read.
/// </remarks>
public sealed class SyntheticGesturePostureTests
{
    private const float Interval = 0.015f;

    private const int InAir = 0;
    private const int OnGround = PlayerActivityState.OnGround;
    private const int Ducking = PlayerActivityState.Ducking;

    private const int Scout = 1;
    private const int Soldier = 3;
    private const int Heavy = 6;

    /// <summary>`TF_COND_AIMING`, bit 0 of `m_nPlayerCond` (tf_shareddefs.h:690).</summary>
    private const int Aiming = 1 << 0;

    /// <summary>`EF_NODRAW` (const.h).</summary>
    private const int NoDraw = 0x020;

    /// <summary>`GR_STATE_TEAM_WIN` (teamplayroundbased_gamerules.h:63).</summary>
    private const int TeamWin = 5;

    /// <summary>`GR_STATE_RND_RUNNING` (:60).</summary>
    private const int RoundRunning = 4;

    /// <summary>`m_nMatchGroupType` with no match group: `k_eTFMatchGroup_Invalid`.</summary>
    private const int NoMatchGroup = -1;

    /// <summary>`m_nMatchGroupType` of a ladder 6v6 match, whose description is `MATCH_TYPE_COMPETITIVE`.</summary>
    private const int LadderMatchGroup = 2;

    [Test]
    public void Build_AReloadMidAirWalk_TakesTheAirwalkActivity()
    {
        DemoTimeline timeline = Build(
            Soldier,
            At(100, 0f, OnGround),
            At(101, 6f, InAir),
            At(102, 12f, InAir) with { Events = [PlayerAnimEvent.Reload] });

        Reload(timeline, 102).ActivityName.ShouldBe("ACT_MP_RELOAD_AIRWALK");
    }

    [Test]
    public void Build_AReloadAfterTheLanding_Stands()
    {
        DemoTimeline timeline = Build(
            Soldier,
            At(100, 0f, OnGround),
            At(101, 6f, InAir),
            At(102, 8f, InAir),
            At(103, 0f, OnGround),
            At(104, 0f, OnGround) with { Events = [PlayerAnimEvent.Reload] });

        Reload(timeline, 104).ActivityName.ShouldBe("ACT_MP_RELOAD_STAND");
    }

    /// <remarks>
    /// **The order inside one frame, pinned.** The landing and the reload arrive in the same packet; the engine
    /// fires the event before that frame's animation update clears the latch, so the reload still air-walks.
    /// </remarks>
    [Test]
    public void Build_AReloadArrivingWithTheLanding_StillAirWalks()
    {
        DemoTimeline timeline = Build(
            Soldier,
            At(100, 0f, OnGround),
            At(101, 6f, InAir),
            At(102, 8f, InAir),
            At(103, 0f, OnGround) with { Events = [PlayerAnimEvent.Reload] });

        Reload(timeline, 103).ActivityName.ShouldBe("ACT_MP_RELOAD_AIRWALK");
    }

    [Test]
    public void Build_AnOrdinaryJumpsRise_DoesNotAirWalk()
    {
        DemoTimeline timeline = Build(
            Soldier,
            At(100, 0f, OnGround),
            At(101, 4f, InAir),
            At(102, 8f, InAir) with { Events = [PlayerAnimEvent.Reload] });

        Reload(timeline, 102).ActivityName.ShouldBe("ACT_MP_RELOAD_STAND");
    }

    [Test]
    public void Build_ARiseWhileDucked_TakesTheCrouchingReload()
    {
        DemoTimeline timeline = Build(
            Soldier,
            At(100, 0f, OnGround),
            At(101, 6f, InAir | Ducking),
            At(102, 12f, InAir | Ducking) with { Events = [PlayerAnimEvent.Reload] });

        Reload(timeline, 102).ActivityName.ShouldBe("ACT_MP_RELOAD_CROUCH");
    }

    /// <remarks>
    /// **Everything that stops a player being animated clears the latch** — `ClearAnimationState`
    /// (`tf_playeranimstate.cpp:114`) runs on every frame `Update` gives up: a custom model without the class's
    /// animations (`:340-366`), `EF_NODRAW`, a dormant player and a dead one (`multiplayer_animstate.cpp:1381-1395`).
    /// The rise after it is too slow to set the latch again, so the reload shows which way it went. A custom model
    /// that DOES use the class's animations is animated as usual, which is the control on reading that flag.
    /// </remarks>
    [TestCase("none", "ACT_MP_RELOAD_AIRWALK")]
    [TestCase("custom model with the class animations", "ACT_MP_RELOAD_AIRWALK")]
    [TestCase("dead", "ACT_MP_RELOAD_STAND")]
    [TestCase("EF_NODRAW", "ACT_MP_RELOAD_STAND")]
    [TestCase("custom model without the class animations", "ACT_MP_RELOAD_STAND")]
    [TestCase("dormant", "ACT_MP_RELOAD_STAND")]
    public void Build_AnInterruptionMidAirWalk_ClearsTheLatchOnlyWhenTheEngineStopsAnimating(string interruption, string expected)
    {
        SyntheticPlayer.GestureSnapshot interrupted = At(102, 8f, InAir);

        interrupted = interruption switch
        {
            "custom model with the class animations" =>
                interrupted with { CustomModel = "models/bots/headless_hatman.mdl", UsesClassAnimations = true },
            "dead" => interrupted with { LifeState = 2 },
            "EF_NODRAW" => interrupted with { Effects = NoDraw },
            "custom model without the class animations" =>
                interrupted with { CustomModel = "models/bots/headless_hatman.mdl" },
            "dormant" => interrupted with { Dormant = true },
            _ => interrupted,
        };

        DemoTimeline timeline = Build(
            Soldier,
            At(100, 0f, OnGround),
            At(101, 6f, InAir),
            interrupted,
            At(103, 9f, InAir),
            At(104, 10f, InAir) with { Events = [PlayerAnimEvent.Reload] });

        Reload(timeline, 104).ActivityName.ShouldBe(expected);
    }

    /// <remarks>
    /// **A firing heavy's latch is frozen** — `HandleJumping` returns before the air walk while a heavy is
    /// `TF_COND_AIMING` (`tf_playeranimstate.cpp:1439-1440`), so a landing does not clear it until he stops.
    /// The soldier row is the control on the CLASS: the same condition on anyone else freezes nothing.
    /// </remarks>
    [TestCase(Heavy, Aiming, "ACT_MP_RELOAD_AIRWALK")]
    [TestCase(Heavy, 0, "ACT_MP_RELOAD_STAND")]
    [TestCase(Soldier, Aiming, "ACT_MP_RELOAD_STAND")]
    public void Build_ALandingWhileAHeavyFires_KeepsTheLatch(int playerClass, int condition, string expected)
    {
        DemoTimeline timeline = Build(
            playerClass,
            At(100, 0f, OnGround),
            At(101, 6f, InAir),
            At(102, 0f, OnGround) with { PlayerCond = condition },
            At(103, 0f, OnGround) with { PlayerCond = condition, Events = [PlayerAnimEvent.Reload] });

        Reload(timeline, 103).ActivityName.ShouldBe(expected);
    }

    /// <remarks>
    /// **A grappling hook sets the latch without any rise** — `GetGrapplingHookTarget() != NULL` stands beside
    /// the velocity in `:1446`. The target has to be an entity the client holds, which the game rules entity is;
    /// a handle naming a slot the client holds nothing in is a null `Get()`, and no hook at all. So is a handle
    /// whose serial is not the occupant's: `Get()` compares it (`RecvProxy_IntToEHandle` keeps both,
    /// `recvproxy.cpp:80`), because a slot that changed hands must name nothing rather than its new occupant (B231).
    /// </remarks>
    [TestCase(SyntheticPlayer.GestureRulesEntityIndex, 1, "ACT_MP_RELOAD_AIRWALK")]
    [TestCase(SyntheticPlayer.GestureRulesEntityIndex, 2, "ACT_MP_RELOAD_STAND")]
    [TestCase(SyntheticPlayer.GestureRulesEntityIndex + 1, 1, "ACT_MP_RELOAD_STAND")]
    [TestCase(null, 1, "ACT_MP_RELOAD_STAND")]
    public void Build_AGrappledPlayerRisingSlowly_AirWalksOnlyWhileHooked(int? target, int serial, string expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            Interval,
            SceneTeams.Red,
            Soldier,
            (RoundRunning, SceneTeams.Unassigned, NoMatchGroup),
            alwaysLoser: false,
            At(100, 0f, OnGround),
            At(101, 1f, InAir) with { GrapplingHookTarget = target, GrapplingHookSerial = serial },
            At(102, 2f, InAir) with
            {
                GrapplingHookTarget = target, GrapplingHookSerial = serial, Events = [PlayerAnimEvent.Reload],
            }));

        Reload(timeline, 102).ActivityName.ShouldBe(expected);
    }

    /// <remarks>
    /// **The loser's air dash, and only the loser's** — `DoAnimationEvent` asks `IsLoser()` when the event fires
    /// (`tf_playeranimstate.cpp:1196`), and during humiliation that is every team but the winner
    /// (`tf_player_shared.cpp:13674`).
    /// </remarks>
    [TestCase(SceneTeams.Blu, "ACT_MP_DOUBLEJUMP_LOSERSTATE")]
    [TestCase(SceneTeams.Red, "ACT_MP_DOUBLEJUMP")]
    public void Build_ADoubleJumpDuringHumiliation_IsTheLosersOnlyOnTheLosingTeam(int winningTeam, string expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            Interval,
            SceneTeams.Red,
            Scout,
            (TeamWin, winningTeam, NoMatchGroup),
            alwaysLoser: false,
            At(100, 0f, OnGround),
            At(101, 4f, InAir) with { Events = [PlayerAnimEvent.DoubleJump] }));

        Gesture(timeline, 101, GestureSlot.Jump).ActivityName.ShouldBe(expected);
    }

    /// <remarks>
    /// **"No loser mode in competitive"** (`tf_player_shared.cpp:13663`), read by the timeline from the game rules'
    /// `m_nMatchGroupType`: in a ladder match the losing team's air dash is the ordinary one.
    /// </remarks>
    [TestCase(NoMatchGroup, "ACT_MP_DOUBLEJUMP_LOSERSTATE")]
    [TestCase(LadderMatchGroup, "ACT_MP_DOUBLEJUMP")]
    public void Build_ADoubleJumpDuringACompetitiveHumiliation_IsTheOrdinaryOne(int matchGroup, string expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            Interval,
            SceneTeams.Red,
            Scout,
            (TeamWin, SceneTeams.Blu, matchGroup),
            alwaysLoser: false,
            At(100, 0f, OnGround),
            At(101, 4f, InAir) with { Events = [PlayerAnimEvent.DoubleJump] }));

        Gesture(timeline, 101, GestureSlot.Jump).ActivityName.ShouldBe(expected);
    }

    /// <remarks>
    /// **Waist-deep water clears the latch** (`tf_playeranimstate.cpp:1455-1458`), read by the timeline from
    /// `m_nWaterLevel`; water at the feet does not, which is the control on the threshold.
    /// </remarks>
    [TestCase(2, "ACT_MP_RELOAD_STAND")]
    [TestCase(1, "ACT_MP_RELOAD_AIRWALK")]
    public void Build_AnAirWalkIntoWater_ClearsOnlyAtTheWaist(int waterLevel, string expected)
    {
        DemoTimeline timeline = Build(
            Soldier,
            At(100, 0f, OnGround),
            At(101, 6f, InAir),
            At(102, 8f, InAir) with { WaterLevel = waterLevel },
            At(103, 9f, InAir),
            At(104, 10f, InAir) with { Events = [PlayerAnimEvent.Reload] });

        Reload(timeline, 104).ActivityName.ShouldBe(expected);
    }

    /// <remarks>
    /// **`tf_always_loser` is the first line of `IsLoser`** (`tf_player_shared.cpp:13656`), and it arrives the way
    /// every replicated ConVar does, as `net_SetConVar` at signon. The round is running here, so without it
    /// nobody is a loser.
    /// </remarks>
    [TestCase(true, "ACT_MP_DOUBLEJUMP_LOSERSTATE")]
    [TestCase(false, "ACT_MP_DOUBLEJUMP")]
    public void Build_ADoubleJumpUnderTfAlwaysLoser_IsTheLosers(bool alwaysLoser, string expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            Interval,
            SceneTeams.Red,
            Scout,
            (RoundRunning, SceneTeams.Unassigned, NoMatchGroup),
            alwaysLoser,
            At(100, 0f, OnGround),
            At(101, 4f, InAir) with { Events = [PlayerAnimEvent.DoubleJump] }));

        Gesture(timeline, 101, GestureSlot.Jump).ActivityName.ShouldBe(expected);
    }

    /// <remarks>
    /// **The player's activity table is chosen every tick from the same `IsLoser` the gestures ask** (B437) —
    /// `CTFPlayerAnimState::ActivityOverride` (`tf_playeranimstate.cpp:223-269`) — so the losing team's body runs
    /// and stands as the loser during humiliation, and the winning team's does not.
    /// </remarks>
    [TestCase(SceneTeams.Blu, PlayerActivityOverride.LoserState)]
    [TestCase(SceneTeams.Red, PlayerActivityOverride.None)]
    public void Build_DuringHumiliation_TheLosingTeamTakesTheLoserTable(int winningTeam, PlayerActivityOverride expected)
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            Interval,
            SceneTeams.Red,
            Scout,
            (TeamWin, winningTeam, NoMatchGroup),
            alwaysLoser: false,
            At(100, 0f, OnGround),
            At(101, 0f, OnGround)));

        timeline.Frames.Single(frame => frame.Tick == 101).Players.Single().ActivityOverride.ShouldBe(expected);
    }

    [Test]
    public void ActivityOverrides_TheEnginesOrder_KartThenCompetitiveLoserThenLoserThenCarrying()
    {
        // :232-258, in order, each ahead of the next.
        PlayerConditions none = default;

        PlayerActivityOverrides.For(Conditions(PlayerConditions.HalloweenKart, PlayerConditions.CompetitiveLoser), isLoser: true, carrying: true)
            .ShouldBe(PlayerActivityOverride.KartState);
        PlayerActivityOverrides.For(Conditions(PlayerConditions.CompetitiveLoser), isLoser: true, carrying: true)
            .ShouldBe(PlayerActivityOverride.CompetitiveLoserState);
        PlayerActivityOverrides.For(none, isLoser: true, carrying: true).ShouldBe(PlayerActivityOverride.LoserState);
        PlayerActivityOverrides.For(none, isLoser: false, carrying: true).ShouldBe(PlayerActivityOverride.BuildingDeployed);
        PlayerActivityOverrides.For(none, isLoser: false, carrying: false).ShouldBe(PlayerActivityOverride.None);
    }

    /// <summary>`m_nPlayerCond` and its extensions with the named conditions set.</summary>
    private static PlayerConditions Conditions(params int[] set)
    {
        int[] words = new int[5];

        foreach (int condition in set)
        {
            words[condition / 32] |= 1 << (condition % 32);
        }

        return new PlayerConditions(words[0], words[1], words[2], words[3], words[4]);
    }

    /// <remarks>
    /// **The body and the reload read ONE latch**, which is how the engine has it: `HandleJumping` sets
    /// `m_bInAirWalk` and returns the air-walking body activity from the same test. So the body holds through a
    /// ducked landing exactly as the reload does, and clears when the player stands on the ground — a separate
    /// latch for the body cleared on landing and would have drawn the two apart.
    /// </remarks>
    [Test]
    public void Build_TheBodysAirWalk_IsTheSameLatchTheReloadReads()
    {
        DemoTimeline timeline = Build(
            Soldier,
            At(100, 0f, OnGround),
            At(101, 6f, InAir),
            At(102, 7f, InAir | Ducking),
            At(103, 0f, OnGround | Ducking),
            At(104, 0f, OnGround));

        // Ducked while latched, HandleJumping returns true having set nothing: ACT_MP_STAND_IDLE (B437).
        IEnumerable<PlayerActivity?> answered = new[] { 100, 101, 102, 103, 104 }
            .Select(tick => timeline.Frames.Single(frame => frame.Tick == tick).Players.Single().JumpActivity);

        answered.ShouldBe([null, PlayerActivity.Airwalk, PlayerActivity.StandIdle, PlayerActivity.StandIdle, null]);
    }

    /// <remarks>
    /// **The class script reaches the timeline** (B437). `DemoTimeline.Build` takes the scripts the client reads —
    /// `scripts/playerclasses/*.txt` — so a class that sets `DontDoAirwalk` never latches, and one that sets
    /// `DontDoNewJump` jumps as the single `ACT_MP_JUMP`. Without them the engine's own default holds: every class
    /// air-walks and jumps the new way.
    /// </remarks>
    [Test]
    public void Build_TheClassScripts_DecideTheAirWalkAndTheJump()
    {
        SyntheticPlayer.GestureSnapshot[] rocketJump =
        [
            At(100, 0f, OnGround),
            At(101, 6f, InAir) with { Events = [PlayerAnimEvent.Jump] },
            At(102, 12f, InAir),
        ];

        PlayerActivity? Answer(DemoTimeline timeline) =>
            timeline.Frames.Single(frame => frame.Tick == 102).Players.Single().JumpActivity;

        Answer(Build(Soldier, rocketJump)).ShouldBe(PlayerActivity.Airwalk, "no scripts: the engine's defaults");

        Answer(Build(Soldier, new Scripts(Soldier, new(DontDoAirwalk: true, DontDoNewJump: true)), rocketJump))
            .ShouldBe(PlayerActivity.LegacyJump);
        Answer(Build(Soldier, new Scripts(Soldier, new(DontDoAirwalk: true, DontDoNewJump: false)), rocketJump))
            .ShouldBe(PlayerActivity.JumpStart);
        Answer(Build(Soldier, new Scripts(Scout, new(DontDoAirwalk: true, DontDoNewJump: true)), rocketJump))
            .ShouldBe(PlayerActivity.Airwalk, "the control: another class's script is not this player's");
    }

    /// <summary>A class-script source that sets one class's flags.</summary>
    private sealed class Scripts(int scripted, ClassAnimationScript script) : IClassAnimationScripts
    {
        public ClassAnimationScript ScriptOf(int? playerClass) => playerClass == scripted ? script : default;
    }

    private static SyntheticPlayer.GestureSnapshot At(int tick, float z, int flags) => new(tick, z, flags);

    private static DemoTimeline Build(int playerClass, IClassAnimationScripts classes, params SyntheticPlayer.GestureSnapshot[] snapshots) =>
        DemoTimeline.Build(
            SyntheticPlayer.DemoOfGestures(Interval, SceneTeams.Red, playerClass, rules: null, alwaysLoser: false, snapshots),
            classes: classes);

    private static DemoTimeline Build(int playerClass, params SyntheticPlayer.GestureSnapshot[] snapshots) =>
        DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            Interval, SceneTeams.Red, playerClass, rules: null, alwaysLoser: false, snapshots));

    private static SceneGesture Reload(DemoTimeline timeline, int tick) =>
        Gesture(timeline, tick, GestureSlot.AttackAndReload);

    private static SceneGesture Gesture(DemoTimeline timeline, int tick, GestureSlot slot) =>
        timeline.Frames.Single(frame => frame.Tick == tick).Players.Single().Gestures.ShouldNotBeNull()
            .Single(gesture => gesture.Slot == slot);
}
