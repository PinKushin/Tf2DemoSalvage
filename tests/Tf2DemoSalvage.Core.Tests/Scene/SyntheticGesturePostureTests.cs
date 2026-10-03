using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Net;
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
        DemoTimeline timeline = Decode(SyntheticPlayer.DemoOfGestures(
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
        DemoTimeline timeline = Decode(SyntheticPlayer.DemoOfGestures(
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
        DemoTimeline timeline = Decode(SyntheticPlayer.DemoOfGestures(
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
        DemoTimeline timeline = Decode(SyntheticPlayer.DemoOfGestures(
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
        DemoTimeline timeline = Decode(SyntheticPlayer.DemoOfGestures(
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

    /// <remarks>
    /// **The model's crouch walk decides the duck as much as the flag does** (B437). `DoAnimationEvent` drops `bInDuck`
    /// when `SelectWeightedSequence( TranslateActivity( ACT_MP_CROUCHWALK ) ) &lt; 0` (`tf_playeranimstate.cpp:971-975`),
    /// and during humiliation the loser's table rewrites that to `ACT_MP_CROUCHWALK_LOSERSTATE`, which no shipped class
    /// model declares — so a loser's reload stands even ducked. The winner's row is the control on the TABLE: the same
    /// model asked about a player whose table does not rewrite the crouch walk answers yes.
    /// </remarks>
    [TestCase(SceneTeams.Blu, false, "ACT_MP_RELOAD_STAND")]
    [TestCase(SceneTeams.Blu, true, "ACT_MP_RELOAD_CROUCH")]
    [TestCase(SceneTeams.Red, false, "ACT_MP_RELOAD_CROUCH")]
    public void Build_ADuckedReloadDuringHumiliation_CrouchesOnlyIfTheModelHasTheLosersCrouchWalk(
        int winningTeam, bool modelHasIt, string expected)
    {
        DemoTimeline timeline = Decode(
            SyntheticPlayer.DemoOfGestures(
                Interval,
                SceneTeams.Red,
                Soldier,
                (TeamWin, winningTeam, NoMatchGroup),
                alwaysLoser: false,
                At(100, 0f, OnGround | Ducking),
                At(101, 0f, OnGround | Ducking) with { Events = [PlayerAnimEvent.Reload] }),
            new LoserCrouchWalk(modelHasIt));

        Reload(timeline, 101).ActivityName.ShouldBe(expected);
    }

    /// <remarks>
    /// **`HandleJumping` asks the same duck** (`:1429-1433`): a latched loser who ducks without the model's crouch walk
    /// is not ducking, so the air-walk block keeps running and he air-walks, where one whose model has it stands (B437).
    /// </remarks>
    [TestCase(false, PlayerActivity.Airwalk)]
    [TestCase(true, PlayerActivity.StandIdle)]
    public void Build_ALatchedLoserWhoDucks_AirWalksUnlessTheModelHasTheLosersCrouchWalk(bool modelHasIt, PlayerActivity expected)
    {
        DemoTimeline timeline = Decode(
            SyntheticPlayer.DemoOfGestures(
                Interval,
                SceneTeams.Red,
                Soldier,
                (TeamWin, SceneTeams.Blu, NoMatchGroup),
                alwaysLoser: false,
                At(100, 0f, OnGround),
                At(101, 6f, InAir),
                At(102, 7f, InAir | Ducking)),
            new LoserCrouchWalk(modelHasIt));

        timeline.Frames.Single(frame => frame.Tick == 102).Players.Single().JumpActivity.ShouldBe(expected);
    }

    /// <remarks>
    /// **An event fires an interpolation window after it arrives, and reads the posture THEN** (B437). `CL_QueueEvent`
    /// delays a temp entity by `GetClientInterpAmount()` during playback (B415), and `DoAnimationEvent` reads
    /// `GetFlags()` when it fires. A tenth of a second at 0.015 s a tick is six ticks: a reload arriving standing at 101
    /// fires at 107, by which time the player has crouched. With no window it fires on arrival and stands — the control.
    /// </remarks>
    [Test]
    public void Build_AReloadThatFiresAfterACrouch_TakesTheCrouchingReloadAtTheFireTick()
    {
        SyntheticPlayer.GestureSnapshot[] snapshots =
        [
            At(100, 0f, OnGround),
            At(101, 0f, OnGround) with { Events = [PlayerAnimEvent.Reload] },
            .. Enumerable.Range(102, 7).Select(tick => At(tick, 0f, OnGround | Ducking)),
        ];
        byte[] demo = SyntheticPlayer.DemoOfGestures(
            Interval, SceneTeams.Red, Soldier, rules: null, alwaysLoser: false, snapshots);

        DemoTimeline windowed = DemoTimeline.Build(demo);

        windowed.Frames.Single(frame => frame.Tick == 106).Players.Single().Gestures?
            .Any(gesture => gesture.Slot == GestureSlot.AttackAndReload).ShouldNotBe(true, "it has not fired yet");
        SceneGesture fired = Reload(windowed, 107);
        fired.ActivityName.ShouldBe("ACT_MP_RELOAD_CROUCH");
        fired.StartedSeconds.ShouldBe(107 * (double)Interval, 1e-9);

        Reload(Decode(demo), 101).ActivityName.ShouldBe("ACT_MP_RELOAD_STAND", "no window: it fires on arrival");
    }

    /// <remarks>
    /// **An event due between two packets fires against the EARLIER one** — the client fires it on the frame its time
    /// passes, holding whatever it last received. Due at 107 with packets at 104 (standing) and 110 (ducked), the reload
    /// stands; reading the packet it is first seen after would crouch it.
    /// </remarks>
    [Test]
    public void Build_AnEventDueBetweenPackets_ReadsTheEarlierPacketsPosture()
    {
        DemoTimeline timeline = DemoTimeline.Build(SyntheticPlayer.DemoOfGestures(
            Interval,
            SceneTeams.Red,
            Soldier,
            rules: null,
            alwaysLoser: false,
            At(100, 0f, OnGround),
            At(101, 0f, OnGround) with { Events = [PlayerAnimEvent.Reload] },
            At(104, 1f, OnGround),
            At(110, 0f, OnGround | Ducking)));

        SceneGesture fired = Reload(timeline, 110);
        fired.ActivityName.ShouldBe("ACT_MP_RELOAD_STAND");
        fired.StartedSeconds.ShouldBe(107 * (double)Interval, 1e-9);
    }

    /// <remarks>
    /// **The weapon in hand is asked too** (B437): `TranslateActivity( ACT_MP_CROUCHWALK )` runs the weapon role's
    /// table and the item's `animation_replacement`, so the check needs the weapon's class, its item and the player's
    /// team. The model here lacks the crouch walk only for item 18 in a red soldier's rocket launcher; the other rows are
    /// the controls on each of the three.
    /// </remarks>
    [TestCase(18, SceneTeams.Red, "ACT_MP_RELOAD_STAND")]
    [TestCase(19, SceneTeams.Red, "ACT_MP_RELOAD_CROUCH")]
    [TestCase(18, SceneTeams.Blu, "ACT_MP_RELOAD_CROUCH")]
    [TestCase(null, SceneTeams.Red, "ACT_MP_RELOAD_CROUCH")]
    public void Build_ADuckedReload_AsksTheModelAboutTheHeldWeaponsCrouchWalk(int? item, int team, string expected)
    {
        DemoTimeline timeline = Decode(
            SyntheticPlayer.DemoOfGestures(
                Interval,
                team,
                Soldier,
                rules: null,
                alwaysLoser: false,
                At(100, 0f, OnGround | Ducking) with { WeaponItem = item },
                At(101, 0f, OnGround | Ducking) with { WeaponItem = item, Events = [PlayerAnimEvent.Reload] }),
            new WeaponCrouchWalk());

        Reload(timeline, 101).ActivityName.ShouldBe(expected);
    }

    /// <remarks>`HandleJumping` asks the same weapon (`:1429-1433`): a latched red soldier holding item 18 keeps air-walking.</remarks>
    [TestCase(18, PlayerActivity.Airwalk)]
    [TestCase(19, PlayerActivity.StandIdle)]
    public void Build_ALatchedPlayerWhoDucks_AsksTheModelAboutTheHeldWeaponsCrouchWalk(int item, PlayerActivity expected)
    {
        DemoTimeline timeline = Decode(
            SyntheticPlayer.DemoOfGestures(
                Interval,
                SceneTeams.Red,
                Soldier,
                rules: null,
                alwaysLoser: false,
                At(100, 0f, OnGround) with { WeaponItem = item },
                At(101, 6f, InAir) with { WeaponItem = item },
                At(102, 7f, InAir | Ducking) with { WeaponItem = item }),
            new WeaponCrouchWalk());

        timeline.Frames.Single(frame => frame.Tick == 102).Players.Single().JumpActivity.ShouldBe(expected);
    }

    /// <remarks>
    /// **`m_Shared.IsLoser()` itself reaches the body, not the table it picks** (B437): `HandleDucking` and
    /// `HandleMoving` ask `IsLoser()` (`tf_playeranimstate.cpp:1307`, `:1344`, `:1351`), the rule
    /// <see cref="LoserState.IsLoser"/> already ports. The winning team's player is the control.
    /// </remarks>
    [TestCase(SceneTeams.Blu, true)]
    [TestCase(SceneTeams.Red, false)]
    public void Build_DuringHumiliation_CarriesIsLoserToThePosture(int winningTeam, bool expected)
    {
        DemoTimeline timeline = Decode(SyntheticPlayer.DemoOfGestures(
            Interval, SceneTeams.Red, Scout, (TeamWin, winningTeam, NoMatchGroup), alwaysLoser: false,
            At(100, 0f, OnGround), At(101, 0f, OnGround)));

        timeline.Frames.Single(frame => frame.Tick == 101).Players.Single().Posture.IsLoser.ShouldBe(expected);
    }

    /// <remarks>
    /// **`IsAiming()` is `TF_COND_AIMING` on anyone but a soldier** (`tf_player_shared.cpp:11429-11441`) — the soldier
    /// row is the control on the class — and the air dash is `m_iAirDash &gt; 0`.
    /// </remarks>
    [TestCase(Heavy, Aiming, 0, true, false)]
    [TestCase(Soldier, Aiming, 0, false, false)]
    [TestCase(Scout, 0, 1, false, true)]
    public void Build_TheAimAndTheAirDash_ReachThePosture(int playerClass, int condition, int airDash, bool aiming, bool dashing)
    {
        DemoTimeline timeline = Decode(SyntheticPlayer.DemoOfGestures(
            Interval, SceneTeams.Red, playerClass, rules: null, alwaysLoser: false,
            At(100, 0f, OnGround), At(101, 0f, OnGround) with { PlayerCond = condition, AirDash = airDash }));

        TfPosture posture = timeline.Frames.Single(frame => frame.Tick == 101).Players.Single().Posture;
        (posture.IsAiming, posture.AirDashing).ShouldBe((aiming, dashing));
    }

    /// <remarks>
    /// **A zoomed sniper's shot holds the deployed pose for two seconds** (`m_flHoldDeployedPoseUntilTime = curtime +
    /// 2.0`, `tf_playeranimstate.cpp:1028`), and `HandleMoving` cancels it the frame the player moves (`:1301-1305`).
    /// Held still, it lapses at two seconds; moving at 102, it is gone at once.
    /// </remarks>
    [Test]
    public void Build_AZoomedSnipersShot_HoldsTheDeployedPoseUntilHeMovesOrTwoSecondsPass()
    {
        const int Sniper = 2;
        const int Zoomed = 1 << 1;

        SyntheticPlayer.GestureSnapshot Held(int tick, float x = 64f) =>
            At(tick, 0f, OnGround) with { WeaponItem = 14, SniperRifle = true, PlayerCond = Zoomed, X = x };

        bool Holds(DemoTimeline timeline, int tick) =>
            timeline.Frames.Single(frame => frame.Tick == tick).Players.Single().Posture.HoldsDeployedPose;

        DemoTimeline still = Decode(SyntheticPlayer.DemoOfGestures(
            Interval, SceneTeams.Red, Sniper, rules: null, alwaysLoser: false,
            Held(100), Held(101) with { Events = [PlayerAnimEvent.AttackPrimary] }, Held(102), Held(234), Held(236)));

        Holds(still, 100).ShouldBeFalse("the control: no shot yet");
        Holds(still, 102).ShouldBeTrue();
        Holds(still, 234).ShouldBeTrue("101 + 2 s is tick 234.3");
        Holds(still, 236).ShouldBeFalse();

        DemoTimeline moved = Decode(SyntheticPlayer.DemoOfGestures(
            Interval, SceneTeams.Red, Sniper, rules: null, alwaysLoser: false,
            Held(100), Held(101) with { Events = [PlayerAnimEvent.AttackPrimary] }, Held(102, x: 70f), Held(103, x: 70f)));

        Holds(moved, 102).ShouldBeFalse("HandleMoving cancelled it");
        Holds(moved, 103).ShouldBeFalse();
    }

    /// <summary>A model lacking the crouch walk only for item 18 in a red soldier's <see cref="SyntheticPlayer.GestureWeaponClass"/>.</summary>
    private sealed class WeaponCrouchWalk : IClassAnimationScripts
    {
        public ClassAnimationScript ScriptOf(int? playerClass) => default;

        public bool HasCrouchWalk(
            int? playerClass, PlayerActivityOverride table, string? weaponClass, int? weaponItem, int team) =>
            !(playerClass == Soldier && weaponClass == SyntheticPlayer.GestureWeaponClass && weaponItem == 18 &&
              team == SceneTeams.Red);
    }

    /// <summary>A class-script source that sets one class's flags.</summary>
    private sealed class Scripts(int scripted, ClassAnimationScript script) : IClassAnimationScripts
    {
        public ClassAnimationScript ScriptOf(int? playerClass) => playerClass == scripted ? script : default;
    }

    /// <summary>A model that has, or lacks, the loser's crouch walk, and has every other.</summary>
    private sealed class LoserCrouchWalk(bool has) : IClassAnimationScripts
    {
        public ClassAnimationScript ScriptOf(int? playerClass) => default;

        public bool HasCrouchWalk(
            int? playerClass, PlayerActivityOverride table, string? weaponClass, int? weaponItem, int team) =>
            has || table != PlayerActivityOverride.LoserState;
    }

    private static SyntheticPlayer.GestureSnapshot At(int tick, float z, int flags) => new(tick, z, flags);

    /// <summary>`cl_interp 0`, `cl_interp_ratio 0`: no window, so an event fires on the tick it arrives.</summary>
    private static readonly ClientInterp NoWindow = new(0f, 0f, 66f);

    /// <summary>Decodes with no interpolation window, which every test here but the fire-time ones assumes.</summary>
    private static DemoTimeline Decode(byte[] demo, IClassAnimationScripts? classes = null) =>
        DemoTimeline.Build(demo, client: NoWindow, classes: classes);

    private static DemoTimeline Build(int playerClass, IClassAnimationScripts classes, params SyntheticPlayer.GestureSnapshot[] snapshots) =>
        Decode(
            SyntheticPlayer.DemoOfGestures(Interval, SceneTeams.Red, playerClass, rules: null, alwaysLoser: false, snapshots),
            classes);

    private static DemoTimeline Build(int playerClass, params SyntheticPlayer.GestureSnapshot[] snapshots) =>
        Decode(SyntheticPlayer.DemoOfGestures(
            Interval, SceneTeams.Red, playerClass, rules: null, alwaysLoser: false, snapshots));

    private static SceneGesture Reload(DemoTimeline timeline, int tick) =>
        Gesture(timeline, tick, GestureSlot.AttackAndReload);

    private static SceneGesture Gesture(DemoTimeline timeline, int tick, GestureSlot slot) =>
        timeline.Frames.Single(frame => frame.Tick == tick).Players.Single().Gestures.ShouldNotBeNull()
            .Single(gesture => gesture.Slot == slot);
}
