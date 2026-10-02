using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Scene.Prediction;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// What the POV recorder's movement trace meets besides the world: <c>CTFGameMovement::TracePlayerBBox</c>'s
/// <c>CTraceFilterObject</c> over <c>CTraceFilterSimple</c> (<c>tf_gamemovement.cpp:2223-2328</c>, <c>util_shared.cpp:241-314</c>)
/// with <c>COLLISION_GROUP_PLAYER_MOVEMENT</c> (8).
/// </summary>
/// <remarks>
/// <c>MASK_PLAYERSOLID</c> holds <c>CONTENTS_MONSTER</c>, <c>CONTENTS_WINDOW</c> and <c>CONTENTS_MOVEABLE</c>
/// (<c>bspflags.h:108</c>), so <c>StandardFilterRules</c> passes every entity; what decides is the entity's
/// <c>ShouldCollide</c> and <c>CTFGameRules::ShouldCollide</c> (<c>tf_gamerules.cpp:18000</c>, falling to
/// <c>gamerules.cpp:676</c>). Collision groups count from <c>const.h:398</c>: DEBRIS 1, PLAYER 5, PLAYER_MOVEMENT 8,
/// PASSABLE_DOOR 15, PUSHAWAY 17; TF's from <c>tf_shareddefs.h:1317</c>: OBJECT 21, RESPAWNROOMS 25.
/// </remarks>
public sealed class MovementWorldConformanceTests
{
    private const int RedPlayerMask = MovementWorld.MaskPlayerSolid | MovementWorld.ContentsBlueTeam;
    private const int RoundRunning = 4;

    [Test]
    public void ShouldCollide_ASolidDoor_Blocks() =>
        MovementWorld.ShouldCollide("CBaseDoor", Bsp(), team: 0, RoundRunning, RedPlayerMask).ShouldBeTrue();

    [Test]
    public void ShouldCollide_ADoorFlaggedNotSolid_DoesNotBlock()
    {
        // FSOLID_NOT_SOLID (4, const.h:252) takes an entity out of the solid partition the trace enumerates.
        MovementWorld.ShouldCollide("CBaseDoor", Bsp() with { SolidFlags = 4 }, 0, RoundRunning, RedPlayerMask).ShouldBeFalse();
    }

    [Test]
    public void ShouldCollide_ABrushWithNoSolidType_DoesNotBlock() =>
        MovementWorld.ShouldCollide("CBaseEntity", Bsp() with { SolidType = 0 }, 0, RoundRunning, RedPlayerMask).ShouldBeFalse();

    [Test]
    public void ShouldCollide_ADebrisBrush_DoesNotBlock()
    {
        // CBaseEntity::ShouldCollide (baseentity_shared.cpp:621): DEBRIS needs CONTENTS_DEBRIS in the mask, and
        // MASK_PLAYERSOLID has none.
        MovementWorld.ShouldCollide("CBaseEntity", Bsp() with { CollisionGroup = 1 }, 0, RoundRunning, RedPlayerMask).ShouldBeFalse();
    }

    [Test]
    public void ShouldCollide_APushawayBrush_DoesNotBlock()
    {
        // gamerules.cpp:685: PLAYER_MOVEMENT against PUSHAWAY is refused.
        MovementWorld.ShouldCollide("CBaseEntity", Bsp() with { CollisionGroup = 17 }, 0, RoundRunning, RedPlayerMask).ShouldBeFalse();
    }

    [Test]
    public void ShouldCollide_APassableDoor_StillBlocksPlayerMovement()
    {
        // gamerules.cpp:710 refuses PASSABLE_DOOR to COLLISION_GROUP_PLAYER alone; PLAYER_MOVEMENT falls through to true.
        MovementWorld.ShouldCollide("CBaseDoor", Bsp() with { CollisionGroup = 15 }, 0, RoundRunning, RedPlayerMask).ShouldBeTrue();
    }

    [Test]
    public void ShouldCollide_TheEnemysRespawnWall_Blocks()
    {
        // C_FuncRespawnRoomVisualizer::ShouldCollide (c_func_respawnroom.cpp:69-97): a BLU wall stops a mask carrying
        // CONTENTS_BLUETEAM, which is a RED player's (tf_gamemovement.cpp:273-275).
        MovementWorld.ShouldCollide("CFuncRespawnRoomVisualizer", Bsp() with { CollisionGroup = 25 }, team: 3, RoundRunning, RedPlayerMask)
            .ShouldBeTrue();
    }

    [Test]
    public void ShouldCollide_HisOwnTeamsRespawnWall_DoesNotBlock() =>
        MovementWorld.ShouldCollide("CFuncRespawnRoomVisualizer", Bsp() with { CollisionGroup = 25 }, team: 2, RoundRunning, RedPlayerMask)
            .ShouldBeFalse();

    [Test]
    public void ShouldCollide_TheEnemysRespawnWallWhilePassingThroughEnemies_DoesNotBlock()
    {
        // m_isPassingThroughEnemies drops the team contents from the mask (:269), and the wall asks for exactly that bit.
        MovementWorld.ShouldCollide("CFuncRespawnRoomVisualizer", Bsp(), team: 3, RoundRunning, MovementWorld.MaskPlayerSolid)
            .ShouldBeFalse();
    }

    [Test]
    public void ShouldCollide_TheEnemysRespawnWallOnATeamWin_DoesNotBlock()
    {
        // :72: GR_STATE_TEAM_WIN (5) opens every respawn room.
        MovementWorld.ShouldCollide("CFuncRespawnRoomVisualizer", Bsp(), team: 3, roundState: 5, RedPlayerMask).ShouldBeFalse();
    }

    [Test]
    public void ShouldCollide_AnUnassignedRespawnWall_DoesNotBlock() =>
        MovementWorld.ShouldCollide("CFuncRespawnRoomVisualizer", Bsp(), team: 0, RoundRunning, RedPlayerMask).ShouldBeFalse();

    [Test]
    public void ShouldCollide_TheEnemysForceField_Blocks()
    {
        // C_FuncForceField::ShouldCollide (c_func_forcefield.cpp:45-73) is the same rule.
        MovementWorld.ShouldCollide("CFuncForceField", Bsp(), team: 3, RoundRunning, RedPlayerMask).ShouldBeTrue();
        MovementWorld.ShouldCollide("CFuncForceField", Bsp(), team: 2, RoundRunning, RedPlayerMask).ShouldBeFalse();
    }

    [Test]
    public void Boxes_AStandingBuildingOfEitherTeam_IsABoxAtItsOrigin()
    {
        // CTraceFilterObject (tf_gamemovement.cpp:2234-2255) hits his own buildings, and CTraceFilterSimple everyone
        // else's: TFCOLLISION_GROUP_OBJECT is refused to no group of player movement. SOLID_BBOX (2, const.h:240) is world-aligned.
        ScenePlayer recorder = Player(1, team: 2);
        SceneBuilding sentry = Building(40, team: 3) with { Position = (100f, 0f, 0f) };
        SceneBuilding own = Building(41, team: 2) with { Position = (-100f, 0f, 0f) };

        List<MovementBox> boxes = MovementWorld.Boxes([recorder], [sentry, own], recorder);

        boxes.ShouldBe(
        [
            new MovementBox(new Vector3(80f, -20f, 0f), new Vector3(120f, 20f, 66f), 40, 0),
            new MovementBox(new Vector3(-120f, -20f, 0f), new Vector3(-80f, 20f, 66f), 41, 0),
        ]);
    }

    [Test]
    public void Boxes_ABlueprintBeingPlaced_IsNoBox()
    {
        // tf_obj.cpp:881: a building being placed is FSOLID_NOT_SOLID.
        ScenePlayer recorder = Player(1, team: 2);

        MovementWorld.Boxes([recorder], [Building(40, team: 3) with { SolidFlags = 4 }], recorder).ShouldBeEmpty();
    }

    [Test]
    public void Boxes_AnEnemyAndATeamMate_OnlyTheEnemyIsABoxAndOnlyForHisTeamsContents()
    {
        // C_TFPlayer::ShouldCollide passes a player to PLAYER_MOVEMENT only when the mask has his team's contents.
        ScenePlayer recorder = Player(1, team: 2);
        ScenePlayer enemy = Player(2, team: 3) with { X = 50f };
        ScenePlayer mate = Player(3, team: 2) with { X = -50f };

        MovementWorld.Boxes([recorder, enemy, mate], [], recorder).ShouldBe(
            [new MovementBox(new Vector3(26f, -24f, 0f), new Vector3(74f, 24f, 82f), 2, MovementWorld.ContentsBlueTeam)]);
    }

    [Test]
    public void Sweep_FromInsideABoxStayingInside_IsAllSolid()
    {
        // CM_ClipBoxToBrush: never out at either end is allsolid, fraction 0, and the entity is the trace's.
        MovementBox box = new(new Vector3(-10f), new Vector3(10f), 40, 0);

        BspTrace trace = MovementWorld.Sweep(Vector3.Zero, new Vector3(1f, 0f, 0f), Vector3.Zero, Vector3.Zero, box).ShouldNotBeNull();

        trace.AllSolid.ShouldBeTrue();
        trace.StartSolid.ShouldBeTrue();
        trace.Fraction.ShouldBe(0f);
        trace.BrushEntity.ShouldBe(40);
    }

    [Test]
    public void Sweep_FromInsideABoxLeavingIt_StartsSolidButGoesTheWholeWay()
    {
        // CM_ClipBoxToBrush: in at the start and out at the end is startsolid with the fraction left alone.
        MovementBox box = new(new Vector3(-10f), new Vector3(10f), 40, 0);

        BspTrace trace = MovementWorld.Sweep(Vector3.Zero, new Vector3(50f, 0f, 0f), Vector3.Zero, Vector3.Zero, box).ShouldNotBeNull();

        trace.AllSolid.ShouldBeFalse();
        trace.StartSolid.ShouldBeTrue();
        trace.Fraction.ShouldBe(1f);
    }

    [Test]
    public void Sweep_IntoABox_StopsAnEpsilonShortOfIt()
    {
        // (start distance − DIST_EPSILON) / travel: (40 − 0.03125) / 60.
        MovementBox box = new(new Vector3(40f, -10f, -10f), new Vector3(60f, 10f, 10f), 40, 0);

        BspTrace trace = MovementWorld.Sweep(Vector3.Zero, new Vector3(60f, 0f, 0f), Vector3.Zero, Vector3.Zero, box).ShouldNotBeNull();

        trace.Fraction.ShouldBe((40f - 0.03125f) / 60f, 1e-6f);
        trace.Normal.ShouldBe((-1f, 0f, 0f));
        trace.BrushEntity.ShouldBe(40);
    }

    private static SceneCollision Bsp() => new(SolidType: 1, SolidFlags: 0, CollisionGroup: 0);

    private static ScenePlayer Player(int index, int team) =>
        new(index, 0f, 0f, 0f, team, Health: 100, PlayerClass: 3, LifeState: 0);

    private static SceneBuilding Building(int index, int team) => new(index)
    {
        Team = team,
        Mins = (-20f, -20f, 0f),
        Maxs = (20f, 20f, 66f),
        SolidType = 2,
        SolidFlags = 0,
        Position = (0f, 0f, 0f),
    };
}

/// <summary>What prediction restores from the recorder's last networked state (<c>CPrediction::_Update</c>).</summary>
public sealed class RecorderPredictionRestoreTests
{
    [Test]
    public void Restore_TheDtLocalDuckState_IsWhatTheMovementStartsFrom()
    {
        // DT_Local (playerlocaldata.cpp:30-35, :49, :59) and DT_LocalPlayerExclusive (c_baseplayer.cpp:236, :246) are what
        // prediction restores before re-running the commands; FL_DUCKING stays the flag's.
        ScenePlayer recorder = new(1, 10f, 20f, 30f, Team: 3, Health: 100, PlayerClass: 1, LifeState: 0, Flags: 1 | 2, MaxSpeed: 400f)
        {
            Movement = new SceneLocalMovement
            {
                Ducked = true,
                Ducking = false,
                InDuckJump = true,
                DuckTime = 812.5f,
                DuckJumpTime = 3.5f,
                JumpTime = 7.5f,
                FallVelocity = 120.25f,
                AllowAutoMovement = false,
                BaseVelocity = (1f, 2f, 3f),
                AirDash = 1,
                AirDucked = 2,
                DuckTimer = 55.5f,
                TickBase = 4000,
            },
        };

        PredictedPlayer player = RecorderPrediction.Restore(recorder, intervalPerTick: 0.015f, packetTick: 3000);

        player.Ducked.ShouldBeTrue();
        player.Ducking.ShouldBeFalse();
        player.InDuckJump.ShouldBeTrue();
        player.FlDucking.ShouldBeTrue();
        player.DuckTime.ShouldBe(812.5f);
        player.DuckJumpTime.ShouldBe(3.5f);
        player.JumpTime.ShouldBe(7.5f);
        player.FallVelocity.ShouldBe(120.25f);
        player.AllowAutoMovement.ShouldBeFalse();
        player.BaseVelocity.ShouldBe(new Vector3(1f, 2f, 3f));
        player.AirDash.ShouldBe(1);
        player.AirDucked.ShouldBe(2);
        player.DuckTimer.ShouldBe(55.5f);
        player.CurTime.ShouldBe(4000 * 0.015f);
        player.Team.ShouldBe(3);
        player.EntityIndex.ShouldBe(1);
    }

    [Test]
    public void Restore_WithNoDtLocal_DucksFromTheFlagAndTimesFromThePacket()
    {
        // The control: a demo without DT_Local keeps the flag-only start, and curtime falls back to the packet's tick.
        ScenePlayer recorder = new(1, 0f, 0f, 0f, Team: 2, Health: 100, PlayerClass: 1, LifeState: 0, Flags: 2, MaxSpeed: 400f);

        PredictedPlayer player = RecorderPrediction.Restore(recorder, intervalPerTick: 0.015f, packetTick: 3000);

        player.Ducked.ShouldBeTrue();
        player.FlDucking.ShouldBeTrue();
        player.DuckTime.ShouldBe(0f);
        player.AllowAutoMovement.ShouldBeTrue();
        player.CurTime.ShouldBe(3000 * 0.015f);
    }
}
