using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One `CTFRobotDestruction_RobotGroup` (`DT_TFRobotDestruction_RobotGroup`, tf_logic_robot_destruction.cpp).</summary>
/// <param name="EntityIndex">The entity index.</param>
/// <param name="Team">`GetTeamNumber()`.</param>
/// <param name="GroupNumber">`m_nGroupNumber`.</param>
/// <param name="State">`m_nState`, `eRobotUIState`: 0 inactive, 1 active, 2 dead, 3 shielded.</param>
/// <param name="RespawnStartTime">`m_flRespawnStartTime`.</param>
/// <param name="RespawnEndTime">`m_flRespawnEndTime`.</param>
/// <param name="LastAttackedTime">`m_flLastAttackedTime`.</param>
/// <param name="HudIcon">`m_pszHudIcon`.</param>
public readonly record struct SceneRobotGroup(
    int EntityIndex,
    int Team,
    int GroupNumber,
    int State,
    float RespawnStartTime,
    float RespawnEndTime,
    float LastAttackedTime,
    string HudIcon)
{
    /// <summary>The entity's serial number, which a `CHandle` checks with its index.</summary>
    public int SerialNumber { get; init; }
}

/// <summary>`CTFRobotDestructionLogic` (`DT_TFRobotDestructionLogic`) and its robot groups at one tick.</summary>
/// <param name="PlayerDestruction">Whether the logic is `CTFPlayerDestructionLogic`, whose `GetType()` is `TYPE_PLAYER_DESTRUCTION`.</param>
/// <param name="MaxPoints">`m_nMaxPoints`.</param>
/// <param name="RedScore">`m_nRedScore`.</param>
/// <param name="BlueScore">`m_nBlueScore`.</param>
/// <param name="RedTargetPoints">`m_nRedTargetPoints`.</param>
/// <param name="BlueTargetPoints">`m_nBlueTargetPoints`.</param>
/// <param name="RedFinaleEndTime">`m_flRedFinaleEndTime`.</param>
/// <param name="BlueFinaleEndTime">`m_flBlueFinaleEndTime`.</param>
/// <param name="FinaleLength">`m_flFinaleLength`.</param>
/// <param name="ResFile">`m_szResFile`.</param>
/// <param name="CountdownEndTime">`m_flCountdownEndTime`.</param>
/// <param name="Groups">The robot groups, in entity order as `IRobotDestructionGroupAutoList` holds them by creation.</param>
public sealed record SceneRobotDestruction(
    bool PlayerDestruction,
    int MaxPoints,
    int RedScore,
    int BlueScore,
    int RedTargetPoints,
    int BlueTargetPoints,
    float RedFinaleEndTime,
    float BlueFinaleEndTime,
    float FinaleLength,
    string ResFile,
    float CountdownEndTime,
    IReadOnlyList<SceneRobotGroup> Groups)
{
    private const int TeamRed = 2;

    /// <summary>`m_hRedTeamLeader` / `m_hBlueTeamLeader`'s entity indices (`DT_TFPlayerDestructionLogic`), 0 when unset.</summary>
    public (int Red, int Blue) TeamLeaders { get; init; }

    /// <summary>`m_iszCountdownImage`.</summary>
    public string CountdownImage { get; init; } = string.Empty;

    /// <summary>`m_bUsingCountdownImage`.</summary>
    public bool UsingCountdownImage { get; init; }

    /// <summary>`GetScore( nTeam )` (tf_logic_robot_destruction.cpp:674): RED's for RED, else BLU's.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The score.</returns>
    public int Score(int team) => team == TeamRed ? RedScore : BlueScore;

    /// <summary>`GetTargetScore( nTeam )` (:683).</summary>
    /// <param name="team">The team.</param>
    /// <returns>The target score.</returns>
    public int TargetScore(int team) => team == TeamRed ? RedTargetPoints : BlueTargetPoints;

    /// <summary>`GetFinaleWinTime( nTeam )` (:689).</summary>
    /// <param name="team">The team.</param>
    /// <returns>The finale's end.</returns>
    public float FinaleWinTime(int team) => team == TeamRed ? RedFinaleEndTime : BlueFinaleEndTime;

    /// <summary>`GetTeamLeader( nTeam )`.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The leader's entity index, or 0.</returns>
    public int TeamLeader(int team) => team == TeamRed ? TeamLeaders.Red : TeamLeaders.Blue;

    /// <inheritdoc/>
    public bool Equals(SceneRobotDestruction? other) =>
        other is not null
        && (PlayerDestruction, MaxPoints, RedScore, BlueScore, RedTargetPoints, BlueTargetPoints, RedFinaleEndTime, BlueFinaleEndTime, FinaleLength, ResFile, CountdownEndTime)
            == (other.PlayerDestruction, other.MaxPoints, other.RedScore, other.BlueScore, other.RedTargetPoints, other.BlueTargetPoints, other.RedFinaleEndTime, other.BlueFinaleEndTime, other.FinaleLength, other.ResFile, other.CountdownEndTime)
        && (TeamLeaders, CountdownImage, UsingCountdownImage) == (other.TeamLeaders, other.CountdownImage, other.UsingCountdownImage)
        && Groups.SequenceEqual(other.Groups);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(MaxPoints, RedScore, BlueScore, Groups.Count);

    /// <summary>`GetRobotDestructionLogic()`'s state and every group, sharing the last frame's when unchanged; null without a logic.</summary>
    /// <param name="entities">The frame's entities.</param>
    /// <param name="last">The last frame's, or null.</param>
    /// <returns>The state, or null.</returns>
    internal static SceneRobotDestruction? Read(EntityStateTable entities, SceneRobotDestruction? last)
    {
        EntityState? logic = entities.OfClass("CTFRobotDestructionLogic").FirstOrDefault();
        bool playerDestruction = false;

        if (logic is null)
        {
            logic = entities.OfClass("CTFPlayerDestructionLogic").FirstOrDefault();
            playerDestruction = logic is not null;
        }

        if (logic is null)
        {
            return null;
        }

        List<SceneRobotGroup> groups = [];

        foreach (EntityState group in entities.OfClass("CTFRobotDestruction_RobotGroup"))
        {
            groups.Add(new SceneRobotGroup(
                group.EntityIndex,
                group.Integer("DT_TFRobotDestruction_RobotGroup.m_iTeamNum") ?? 0,
                group.Integer("DT_TFRobotDestruction_RobotGroup.m_nGroupNumber") ?? 0,
                group.Integer("DT_TFRobotDestruction_RobotGroup.m_nState") ?? 0,
                group.Number("DT_TFRobotDestruction_RobotGroup.m_flRespawnStartTime") ?? 0f,
                group.Number("DT_TFRobotDestruction_RobotGroup.m_flRespawnEndTime") ?? 0f,
                group.Number("DT_TFRobotDestruction_RobotGroup.m_flLastAttackedTime") ?? 0f,
                group.Text("DT_TFRobotDestruction_RobotGroup.m_pszHudIcon") ?? string.Empty)
            {
                SerialNumber = group.SerialNumber,
            });
        }

        SceneRobotDestruction read = new(
            playerDestruction,
            logic.Integer("DT_TFRobotDestructionLogic.m_nMaxPoints") ?? 0,
            logic.Integer("DT_TFRobotDestructionLogic.m_nRedScore") ?? 0,
            logic.Integer("DT_TFRobotDestructionLogic.m_nBlueScore") ?? 0,
            logic.Integer("DT_TFRobotDestructionLogic.m_nRedTargetPoints") ?? 0,
            logic.Integer("DT_TFRobotDestructionLogic.m_nBlueTargetPoints") ?? 0,
            logic.Number("DT_TFRobotDestructionLogic.m_flRedFinaleEndTime") ?? 0f,
            logic.Number("DT_TFRobotDestructionLogic.m_flBlueFinaleEndTime") ?? 0f,
            logic.Number("DT_TFRobotDestructionLogic.m_flFinaleLength") ?? 0f,
            logic.Text("DT_TFRobotDestructionLogic.m_szResFile") ?? string.Empty,
            logic.Number("DT_TFRobotDestructionLogic.m_flCountdownEndTime") ?? 0f,
            groups)
        {
            TeamLeaders = (
                entities.Resolve(logic.Integer("DT_TFPlayerDestructionLogic.m_hRedTeamLeader")) ?? 0,
                entities.Resolve(logic.Integer("DT_TFPlayerDestructionLogic.m_hBlueTeamLeader")) ?? 0),
            CountdownImage = logic.Text("DT_TFPlayerDestructionLogic.m_iszCountdownImage") ?? string.Empty,
            UsingCountdownImage = logic.Integer("DT_TFPlayerDestructionLogic.m_bUsingCountdownImage") is > 0,
        };

        return last is not null && last.Equals(read) ? last : read;
    }
}
