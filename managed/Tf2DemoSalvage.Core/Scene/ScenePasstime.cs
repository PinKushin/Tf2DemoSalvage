using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One `CFuncPasstimeGoal` (`DT_FuncPasstimeGoal`, func_passtime_goal.cpp).</summary>
/// <param name="EntityIndex">The entity index.</param>
/// <param name="Team">`GetTeamNumber()`: the team that SCORES here, so the goal on BLU's side is RED's.</param>
/// <param name="TriggerDisabled">`m_bTriggerDisabled`: `BGoalTriggerDisabled()`.</param>
/// <param name="GoalType">`m_iGoalType`: 0 hoop, 1 endzone, 2 tower.</param>
/// <param name="Origin">`GetAbsOrigin()`.</param>
public readonly record struct ScenePasstimeGoal(int EntityIndex, int Team, bool TriggerDisabled, int GoalType, (float X, float Y, float Z) Origin);

/// <summary>A player's Pass Time fields (`DT_TFPlayerShared`).</summary>
/// <param name="EntityIndex">The player.</param>
/// <param name="AskForBallTime">`m_askForBallTime`: `AskForBallTime()`.</param>
/// <param name="TargetedForPass">`m_bIsTargetedForPasstimePass`.</param>
/// <param name="HasBall">`m_bHasPasstimeBall`.</param>
/// <param name="Eye">`EyePosition()`: the origin plus `m_vecViewOffset`.</param>
public readonly record struct ScenePasstimePlayer(int EntityIndex, float AskForBallTime, bool TargetedForPass, bool HasBall, (float X, float Y, float Z) Eye);

/// <summary>`CTFPasstimeLogic` and its `CPasstimeBall` (`DT_TFPasstimeLogic`, `DT_PasstimeBall`), as the Pass Time HUD reads them.</summary>
/// <param name="TrackPoints">`m_trackPoints`: the sixteen points of the progress track, zero past its end.</param>
/// <param name="NumSections">`m_iNumSections`.</param>
/// <param name="MaxPassRange">`m_flMaxPassRange`.</param>
/// <param name="BallPower">`m_iBallPower`, 0 to 100.</param>
/// <param name="Ball">The ball's entity index, or 0 when `m_hBall` names nothing.</param>
/// <param name="BallTeam">The ball's `GetTeamNumber()`.</param>
/// <param name="BallOrigin">The ball's `GetNetworkOrigin()`.</param>
/// <param name="Carrier">`m_hCarrier`'s player, or 0.</param>
/// <param name="PrevCarrier">`m_hPrevCarrier`'s player, or 0.</param>
/// <param name="HomingTarget">`m_hHomingTarget`'s player, or 0.</param>
/// <param name="Goals">`C_FuncPasstimeGoal::GetAutoList()`, in entity order.</param>
/// <param name="Players">Every player's Pass Time fields.</param>
/// <param name="Target">The reticle's entity, the carrier or else the ball: its `EF_NODRAW` and `WorldSpaceCenter()`.</param>
public sealed record ScenePasstime(
    IReadOnlyList<(float X, float Y, float Z)> TrackPoints,
    int NumSections,
    float MaxPassRange,
    int BallPower,
    int Ball,
    int BallTeam,
    (float X, float Y, float Z) BallOrigin,
    int Carrier,
    int PrevCarrier,
    int HomingTarget,
    IReadOnlyList<ScenePasstimeGoal> Goals,
    IReadOnlyList<ScenePasstimePlayer>? Players = null,
    (bool NoDraw, (float X, float Y, float Z) Center) Target = default)
{
    private const int EffectNoDraw = 0x020;

    /// <summary>A player's Pass Time fields, or null.</summary>
    /// <param name="index">The player.</param>
    /// <returns>The fields.</returns>
    public ScenePasstimePlayer? Player(int index)
    {
        IReadOnlyList<ScenePasstimePlayer> players = Players ?? [];

        for (int at = 0; at < players.Count; at++)
        {
            if (players[at].EntityIndex == index)
            {
                return players[at];
            }
        }

        return null;
    }

    private const int TrackPointCount = 16;

    /// <inheritdoc/>
    public bool Equals(ScenePasstime? other) =>
        other is not null
        && TrackPoints.SequenceEqual(other.TrackPoints)
        && NumSections == other.NumSections
        && BitConverter.SingleToInt32Bits(MaxPassRange) == BitConverter.SingleToInt32Bits(other.MaxPassRange)
        && BallPower == other.BallPower
        && Ball == other.Ball
        && BallTeam == other.BallTeam
        && BallOrigin.Equals(other.BallOrigin)
        && Carrier == other.Carrier
        && PrevCarrier == other.PrevCarrier
        && HomingTarget == other.HomingTarget
        && Goals.SequenceEqual(other.Goals)
        && (Players ?? []).SequenceEqual(other.Players ?? [])
        && Target.Equals(other.Target);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Ball, Carrier, BallPower, Goals.Count);

    /// <summary>
    /// `CalcProgressFrac` (tf_hud_passtime.cpp:946): how far along the track a point lies, by the nearest segment, as a
    /// fraction of the track's length plus one.
    /// </summary>
    /// <param name="origin">The point.</param>
    /// <returns>The fraction.</returns>
    public float ProgressFrac((float X, float Y, float Z) origin)
    {
        float bestDist = float.MaxValue;
        float bestLen = 0f;

        // "don't set 0 so div by zero is impossible".
        float totalLen = 1f;
        (float X, float Y, float Z) prev = TrackPoints.Count > 0 ? TrackPoints[0] : default;

        for (int i = 1; i < TrackPoints.Count; i++)
        {
            (float X, float Y, float Z) point = TrackPoints[i];

            // `Vector::IsZero()` (vector.h:132), within its default tolerance of 0.01.
            if (MathF.Abs(point.X) < 0.01f && MathF.Abs(point.Y) < 0.01f && MathF.Abs(point.Z) < 0.01f)
            {
                break;
            }

            (float X, float Y, float Z) segment = (point.X - prev.X, point.Y - prev.Y, point.Z - prev.Z);
            float thisLen = MathF.Sqrt((segment.X * segment.X) + (segment.Y * segment.Y) + (segment.Z * segment.Z));
            totalLen += thisLen;

            // `CalcClosestPointOnLineSegment` (mathlib_base.cpp:3138): `CalcClosestPointToLineT` (:3094), `(DP - DA) / DD` or 0
            // below 0.00001, clamped to the segment.
            float div = (segment.X * segment.X) + (segment.Y * segment.Y) + (segment.Z * segment.Z);
            float dp = (segment.X * origin.X) + (segment.Y * origin.Y) + (segment.Z * origin.Z);
            float da = (segment.X * prev.X) + (segment.Y * prev.Y) + (segment.Z * prev.Z);
            float frac = Math.Clamp(div < 0.00001f ? 0f : (dp - da) / div, 0f, 1f);
            (float X, float Y, float Z) onLine = (prev.X + (segment.X * frac), prev.Y + (segment.Y * frac), prev.Z + (segment.Z * frac));
            float dx = onLine.X - origin.X;
            float dy = onLine.Y - origin.Y;
            float dz = onLine.Z - origin.Z;
            float thisDist = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

            if (thisDist < bestDist)
            {
                bestDist = thisDist;
                bestLen = totalLen - (thisLen * (1f - frac));
            }

            prev = point;
        }

        return bestLen / totalLen;
    }

    /// <summary>Reads the frame's Pass Time logic, sharing the last frame's when unchanged; null where there is none.</summary>
    /// <param name="entities">The frame's entities.</param>
    /// <param name="last">The last frame's, or null.</param>
    /// <returns>The state, or null.</returns>
    internal static ScenePasstime? Read(EntityStateTable entities, ScenePasstime? last)
    {
        // `g_pPasstimeLogic`: the one logic entity.
        if (entities.OfClass("CTFPasstimeLogic").FirstOrDefault() is not { } logic)
        {
            return null;
        }

        (float X, float Y, float Z)[] points = new (float X, float Y, float Z)[TrackPointCount];

        for (int i = 0; i < TrackPointCount; i++)
        {
            points[i] = logic.VectorElement("DT_TFPasstimeLogic.m_trackPoints", i) ?? default;
        }

        int ball = entities.Resolve(logic.Integer("DT_TFPasstimeLogic.m_hBall")) ?? 0;
        EntityState? ballState = ball != 0 && entities.TryGet(ball, out EntityState? found) ? found : null;
        List<ScenePasstimeGoal> goals = [];

        foreach (EntityState goal in entities.OfClass("CFuncPasstimeGoal"))
        {
            goals.Add(new ScenePasstimeGoal(
                goal.EntityIndex,
                goal.Integer("DT_BaseEntity.m_iTeamNum") ?? 0,
                goal.Integer("DT_FuncPasstimeGoal.m_bTriggerDisabled") is > 0,
                goal.Integer("DT_FuncPasstimeGoal.m_iGoalType") ?? 0,
                goal.Origin() ?? default));
        }

        List<ScenePasstimePlayer> players = [];

        foreach (EntityState player in entities.OfClass("CTFPlayer"))
        {
            (float X, float Y, float Z) origin = player.Origin() ?? default;
            // `m_vecViewOffset` is sent to the local player alone, as three floats. **Interpolated:** another player's eye is
            // taken as its origin, where the client holds the view offset it last received or its default.
            (float X, float Y, float Z) offset = (
                player.Number("DT_LocalPlayerExclusive.m_vecViewOffset[0]") ?? 0f,
                player.Number("DT_LocalPlayerExclusive.m_vecViewOffset[1]") ?? 0f,
                player.Number("DT_LocalPlayerExclusive.m_vecViewOffset[2]") ?? 0f);

            players.Add(new ScenePasstimePlayer(
                player.EntityIndex,
                player.Number("DT_TFPlayerShared.m_askForBallTime") ?? 0f,
                player.Integer("DT_TFPlayerShared.m_bIsTargetedForPasstimePass") is > 0,
                player.Integer("DT_TFPlayerShared.m_bHasPasstimeBall") is > 0,
                (origin.X + offset.X, origin.Y + offset.Y, origin.Z + offset.Z)));
        }

        int carrier = Player(entities, ballState, "DT_PasstimeBall.m_hCarrier");
        EntityState? target = carrier != 0 && entities.TryGet(carrier, out EntityState? carrierState) ? carrierState : ballState;

        ScenePasstime read = new(
            points,
            logic.Integer("DT_TFPasstimeLogic.m_iNumSections") ?? 0,
            logic.Number("DT_TFPasstimeLogic.m_flMaxPassRange") ?? 0f,
            logic.Integer("DT_TFPasstimeLogic.m_iBallPower") ?? 0,
            ballState is null ? 0 : ball,
            ballState?.Integer("DT_BaseEntity.m_iTeamNum") ?? 0,
            ballState?.Origin() ?? default,
            carrier,
            Player(entities, ballState, "DT_PasstimeBall.m_hPrevCarrier"),
            Player(entities, ballState, "DT_PasstimeBall.m_hHomingTarget"),
            goals,
            players,
            target is null ? default : (((target.Integer("DT_BaseEntity.m_fEffects") ?? 0) & EffectNoDraw) != 0, Center(target)));

        return last is not null && last.Equals(read) ? last : read;
    }

    /// <summary>`WorldSpaceCenter()`: the origin plus the middle of the collision box.</summary>
    private static (float X, float Y, float Z) Center(EntityState entity)
    {
        (float X, float Y, float Z) origin = entity.Origin() ?? default;
        (float X, float Y, float Z) mins = entity.Vector("DT_CollisionProperty.m_vecMins") ?? default;
        (float X, float Y, float Z) maxs = entity.Vector("DT_CollisionProperty.m_vecMaxs") ?? default;

        return (origin.X + ((mins.X + maxs.X) / 2f), origin.Y + ((mins.Y + maxs.Y) / 2f), origin.Z + ((mins.Z + maxs.Z) / 2f));
    }

    private static int Player(EntityStateTable entities, EntityState? ball, string key) =>
        ball is null ? 0 : entities.Resolve(ball.Integer(key)) ?? 0;
}
