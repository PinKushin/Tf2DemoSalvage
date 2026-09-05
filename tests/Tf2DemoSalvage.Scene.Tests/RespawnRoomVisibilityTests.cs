using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A spawn's team wall is drawn to the enemy, and not to the team that spawns there.
/// </summary>
/// <remarks>
/// **The owner reported the symptom three weeks before the cause was found:** *"these are the wrong
/// grates too btw … the actual locked before round starts spawn doors are the chickenwire
/// texture/prop and a yellow pipe like frame … our issue is we are dropping or not drawing the
/// yellow pipe frame"*.
///
/// The frame was never dropped. `viewer.log` shows `door_grate003_frame.mdl` loading with 120
/// vertices and 264 corners and the map reporting *"MISSING 0 models that would not load"*. What was
/// in front of it is a `func_respawnroomvisualizer` — measured on `cp_fulgur` at tick 900, **nine of
/// them in the draw list**, three standing inside the stage-one setup gates.
///
/// **`C_FuncRespawnRoomVisualizer::DrawModel`, `c_func_respawnroom.cpp:47`:**
///
/// <code>
///   if ( TFGameRules()-&gt;State_Get() == GR_STATE_TEAM_WIN )  return 1;
///   if ( pLocalPlayer &amp;&amp; pLocalPlayer-&gt;GetTeamNumber() == GetTeamNumber() )  return 1;
///   return BaseClass::DrawModel( flags );
/// </code>
///
/// Valve's own comment on the function is *"Don't draw for friendly players"*.
/// </remarks>
public sealed class RespawnRoomVisibilityTests
{
    private const int WallEntity = 698;

    private const int GateEntity = 508;

    [Test]
    public void Visible_ASpawnWallOfTheRecordersOwnTeam_IsHidden()
    {
        // The owner's case: standing in your own spawn, looking at your own gate.
        List<SceneProp> drawn = [Wall(ofRecordersTeam: true), Gate()];

        DrawList.KeepOnly(drawn, RespawnRoomVisibility.Visible(drawn, RoundRunning));

        drawn.ShouldNotContain(prop => prop.EntityIndex == WallEntity);
    }

    [Test]
    public void Visible_ASpawnWallOfTheOtherTeam_IsShown()
    {
        // **The control that stops this becoming "never draw spawn walls".** The wall exists to
        // tell the enemy they cannot come in, so hiding it from everybody would swap one divergence
        // for a worse one — and it would pass every assertion above.
        List<SceneProp> drawn = [Wall(ofRecordersTeam: false), Gate()];

        DrawList.KeepOnly(drawn, RespawnRoomVisibility.Visible(drawn, RoundRunning));

        drawn.ShouldContain(prop => prop.EntityIndex == WallEntity);
    }

    [Test]
    public void Visible_TheGateItself_IsNeverTouched()
    {
        // The other control, and the one that matters for the reported symptom: this rule is about
        // ONE class. A filter that removed the gate too would produce the missing-frame report all
        // over again from the opposite direction.
        List<SceneProp> drawn = [Wall(ofRecordersTeam: true), Gate()];

        DrawList.KeepOnly(drawn, RespawnRoomVisibility.Visible(drawn, RoundRunning));

        drawn.ShouldContain(prop => prop.EntityIndex == GateEntity);
    }

    [Test]
    public void Visible_EveryWallOnceTheRoundIsWon_IsHidden()
    {
        // `if ( State_Get() == GR_STATE_TEAM_WIN ) return 1` comes FIRST, so it applies to the
        // enemy's wall as much as to your own — which is what lets the winning team chase the
        // losers into their spawn.
        List<SceneProp> drawn = [Wall(ofRecordersTeam: false), Gate()];

        DrawList.KeepOnly(drawn, RespawnRoomVisibility.Visible(drawn, RespawnRoomVisibility.TeamWin));

        drawn.ShouldNotContain(prop => prop.EntityIndex == WallEntity);
    }

    [Test]
    public void Visible_AnEnemyWallBeforeTheRoundStarts_IsShown()
    {
        // **The control on the round state, in the state the owner's screenshot was taken in.**
        // `GR_STATE_PREROUND` is setup, when the gates are shut and the walls matter most. Without
        // a second state, a rule that hid every wall in every state would satisfy the win-state
        // assertion and nothing would notice.
        List<SceneProp> drawn = [Wall(ofRecordersTeam: false), Gate()];

        DrawList.KeepOnly(drawn, RespawnRoomVisibility.Visible(drawn, PreRound));

        drawn.ShouldContain(prop => prop.EntityIndex == WallEntity);
    }

    [Test]
    public void Visible_WhenTheDemoDoesNotSayTheRoundState_DrawsTheEnemysWall()
    {
        // **Absent is not `GR_STATE_TEAM_WIN`.** A demo whose game rules entity was never seen
        // tells us nothing about the round, and the engine draws in every state but one — so the
        // unknown case must draw. Treating null as the win state would blank every spawn wall on
        // every era demo that does not carry `m_iRoundState`.
        List<SceneProp> drawn = [Wall(ofRecordersTeam: false), Gate()];

        DrawList.KeepOnly(drawn, RespawnRoomVisibility.Visible(drawn, roundState: null));

        drawn.ShouldContain(prop => prop.EntityIndex == WallEntity);
    }

    [Test]
    public void TeamWin_CountedFromTheEnum_Is5()
    {
        // **A constant nobody can check by reading it, so it is checked here.**
        // `gamerules_roundstate_t` gives an explicit value only to its first member —
        // `GR_STATE_INIT = 0` — and the rest are positional: PREGAME, STARTGAME, PREROUND,
        // RND_RUNNING, TEAM_WIN. Six in, so 5.
        //
        // A first draft said 4, which is `GR_STATE_RND_RUNNING` — the state a match spends almost
        // all of its time in. That mistake hides every spawn wall for the whole round instead of
        // for the seconds after a win, and every other test in this file would still have passed.
        RespawnRoomVisibility.TeamWin.ShouldBe(5);
        RoundRunning.ShouldBe(RespawnRoomVisibility.TeamWin - 1);
    }

    /// <summary><c>GR_STATE_RND_RUNNING</c>, where a match spends almost all its time.</summary>
    private const int RoundRunning = 4;

    /// <summary><c>GR_STATE_PREROUND</c> — setup, with the gates shut.</summary>
    private const int PreRound = 3;

    private static SceneProp Wall(bool ofRecordersTeam) =>
        new(
            EntityIndex: WallEntity,
            ModelPath: "*109",
            Kind: SceneModelKind.Brush,
            Pose: default,
            ClassName: "CFuncRespawnRoomVisualizer",
            OfRecordersTeam: ofRecordersTeam);

    private static SceneProp Gate() =>
        new(
            EntityIndex: GateEntity,
            ModelPath: "*16",
            Kind: SceneModelKind.Brush,
            Pose: default,
            ClassName: "CBaseDoor");

    /// <summary>A spawn-door force field, which shares only the team-win half of the rule.</summary>
    private static SceneProp ForceField(bool ofRecordersTeam) =>
        new(
            EntityIndex: 44,
            ModelPath: "*57",
            Kind: SceneModelKind.Brush,
            Pose: default,
            ClassName: "CFuncForceField",
            OfRecordersTeam: ofRecordersTeam);

    /// <remarks>
    /// **`C_FuncForceField::DrawModel` is two lines and this is the first**
    /// (<c>c_func_forcefield.cpp:28</c>):
    ///
    /// <code>
    ///   // Don't draw for anyone during a team win
    ///   if ( TFGameRules()-&gt;State_Get() == GR_STATE_TEAM_WIN )
    ///       return 1;
    ///   return BaseClass::DrawModel( flags );
    /// </code>
    ///
    /// **The same round state the wall beside it already obeyed** (B359). The visualizer's rule was
    /// implemented and its sibling's was not, so the losing team's spawn kept a solid team-coloured
    /// slab across the doorway at exactly the moment TF2 removes it and the winners run in.
    ///
    /// `ShouldCollide` turns the field off in the same state, which is the other half of the same
    /// intent: after a win it is neither drawn nor solid.
    /// </remarks>
    [Test]
    public void Visible_AForceFieldOnATeamWin_IsDropped()
    {
        RespawnRoomVisibility.Visible(
            [ForceField(ofRecordersTeam: false)], RespawnRoomVisibility.TeamWin).ShouldBeEmpty();
    }

    /// <remarks>
    /// The control for the pair: during a running round the field is drawn, so "never draw a force
    /// field" cannot pass the test above.
    /// </remarks>
    [Test]
    public void Visible_AForceFieldDuringTheRound_IsDrawn()
    {
        RespawnRoomVisibility.Visible([ForceField(ofRecordersTeam: false)], RoundRunning)
            .Count.ShouldBe(1);
    }

    /// <remarks>
    /// **A force field does NOT take the visualizer's own-team rule, and this is the case that
    /// separates the two.** `C_FuncForceField::DrawModel` has no local-player test at all — you see
    /// your own team's field, which is why you can watch enemies fail to walk through it. Reusing
    /// the wall's rule wholesale would delete every force field the recorder's own team owns.
    /// </remarks>
    [Test]
    public void Visible_AForceFieldOfTheRecordersOwnTeam_IsStillDrawn()
    {
        RespawnRoomVisibility.Visible([ForceField(ofRecordersTeam: true)], RoundRunning)
            .Count.ShouldBe(1, "the engine's force field has no own-team test");
    }
}
