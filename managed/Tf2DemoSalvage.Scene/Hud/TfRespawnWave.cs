using System;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Hud;

/// <summary>The client's `CTFGameRules` respawn-wave arithmetic (teamplayroundbased_gamerules.cpp:561-657, tf_gamerules.cpp:3609-3653).</summary>
public static class TfRespawnWave
{
    private const int TeamRed = 2;
    private const int TeamBlue = 3;
    private const int ClassScout = 1;
    private const int MaxTeams = 32; // MAX_TEAMS
    private const int RoundStateRunning = 4; // GR_STATE_RND_RUNNING
    private const int RoundStateBetweenRounds = 10; // GR_STATE_BETWEEN_RNDS

    /// <summary>`GetNextRespawnWave( iTeam, pPlayer )` (teamplayroundbased_gamerules.cpp:561): when this player may next spawn.</summary>
    /// <param name="state">The game rules and convars.</param>
    /// <param name="team">The team.</param>
    /// <param name="player">The player.</param>
    /// <returns>A server time, or 0 in a stalemate.</returns>
    public static float GetNextRespawnWave(HudState state, int team, ScenePlayer player)
    {
        if (state.RoundState == HudState.RoundStateStalemate)
        {
            return 0f;
        }

        // "The soonest this player may spawn".
        float minSpawnTime = GetMinTimeWhenPlayerMaySpawn(state, player);

        if (ShouldRespawnQuickly(state, player))
        {
            return minSpawnTime;
        }

        float nextRespawnTime = team switch
        {
            TeamRed => state.Rules.NextRespawnWave.Red,
            TeamBlue => state.Rules.NextRespawnWave.Blue,
            _ => 0f,
        };
        float waveMaxLength = GetRespawnWaveMaxLength(state, team, scaleWithNumPlayers: true);

        if (waveMaxLength <= 0f)
        {
            return nextRespawnTime;
        }

        // "Keep adding the length of one respawn until we find a wave that this player will be eligible to spawn in."
        while (nextRespawnTime < minSpawnTime)
        {
            nextRespawnTime += waveMaxLength;
        }

        return nextRespawnTime;
    }

    /// <summary>`GetMinTimeWhenPlayerMaySpawn` (:613): death time, the death animation and freeze cam, and one unscaled wave.</summary>
    private static float GetMinTimeWhenPlayerMaySpawn(HudState state, ScenePlayer player)
    {
        float deathAnimLength = (float)(2.0 + state.ConVars.GetFloat("spec_freeze_traveltime") + state.ConVars.GetFloat("spec_freeze_time"));
        float minDelay = deathAnimLength;

        if (!ShouldRespawnQuickly(state, player))
        {
            minDelay += GetRespawnWaveMaxLength(state, player.Team ?? 0, scaleWithNumPlayers: false);
        }

        return player.DeathTime + minDelay;
    }

    /// <summary>`CTFGameRules::ShouldRespawnQuickly` (tf_gamerules.cpp:18700): an MvM defender scout, or a competitive break.</summary>
    private static bool ShouldRespawnQuickly(HudState state, ScenePlayer player)
    {
        // `IsPVEModeActive()` is MvM; `TF_TEAM_PVE_DEFENDERS` is RED.
        if (state.Rules.MannVsMachine && player.Team == TeamRed && player.PlayerClass == ClassScout)
        {
            return true;
        }

        return state.Rules.IsCompetitiveMode && state.RoundState == RoundStateBetweenRounds;
    }

    /// <summary>`CTFGameRules::GetRespawnWaveMaxLength` (tf_gamerules.cpp:3621) over the base (teamplayroundbased_gamerules.cpp:3459).</summary>
    /// <param name="state">The game rules and convars.</param>
    /// <param name="team">The team.</param>
    /// <param name="scaleWithNumPlayers">Whether a long wave shortens with fewer players.</param>
    /// <returns>Seconds.</returns>
    public static float GetRespawnWaveMaxLength(HudState state, int team, bool scaleWithNumPlayers)
    {
        bool scale = scaleWithNumPlayers && !state.Rules.MannVsMachine;
        float time = BaseRespawnWaveMaxLength(state, team, scale);

        if (state.Rules.RobotDestructionRespawnScale is { } robot)
        {
            // `GetRespawnScaleForTeam`: RED's, else BLU's (tf_logic_robot_destruction.cpp:659).
            time *= 1f - (team == TeamRed ? robot.Red : robot.Blue);
        }

        return time;
    }

    private static float BaseRespawnWaveMaxLength(HudState state, int team, bool scale)
    {
        if (team >= MaxTeams || state.RoundState != RoundStateRunning || state.ConVars.GetBool("mp_disable_respawn_times"))
        {
            return 0f;
        }

        // "Let's just turn off respawn times while players are messing around waiting for the tournament to start".
        if (state.ConVars.GetBool("mp_tournament") && state.Rules.WaitingForPlayers)
        {
            return 0f;
        }

        float waveTime = team switch
        {
            TeamRed => state.Rules.TeamRespawnWaveTimes.Red,
            TeamBlue => state.Rules.TeamRespawnWaveTimes.Blue,
            _ => -1f,
        };
        float time = waveTime >= 0f ? waveTime : state.ConVars.GetFloat("mp_respawnwavetime");

        // "For long respawn times, scale the time as the number of players drops".
        if (scale && time > 5f)
        {
            time = MathF.Max(5f, time * GetRespawnTimeScalar(state, team));
        }

        return time;
    }

    /// <summary>`GetRespawnTimeScalar` (teamplayroundbased_gamerules.cpp:648): 0.25 at one player to 1 at eight; 1 in MvM (tf_gamerules.cpp:3612).</summary>
    private static float GetRespawnTimeScalar(HudState state, int team)
    {
        if (state.Rules.MannVsMachine)
        {
            return 1f;
        }

        int players = state.TeamStanding(team)?.Players.Count ?? 0;

        // `RemapValClamped( iNumPlayers, 1, iOptimalPlayers, 0.25, 1.0 )`.
        float t = Math.Clamp((players - 1f) / (8f - 1f), 0f, 1f);

        return 0.25f + ((1f - 0.25f) * t);
    }
}
