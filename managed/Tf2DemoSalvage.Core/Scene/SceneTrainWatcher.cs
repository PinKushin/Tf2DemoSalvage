using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One `CTeamTrainWatcher`'s networked state (`DT_TeamTrainWatcher`, c_team_train_watcher.cpp).</summary>
/// <param name="Team">`GetTeamNumber()`.</param>
/// <param name="Progress">`m_flTotalProgress`.</param>
/// <param name="Speed">`m_iTrainSpeedLevel`.</param>
/// <param name="RecedeTime">`m_flRecedeTime`.</param>
/// <param name="Cappers">`m_nNumCappers`.</param>
public readonly record struct SceneTrainWatcher(int Team, float Progress, int Speed, float RecedeTime, int Cappers)
{
    /// <summary>`C_TeamTrainWatcher()`'s constructor values (c_team_train_watcher.cpp): "force updates when we get our baseline".</summary>
    public static SceneTrainWatcher Created(int team) => new(team, -1f, -2, -1f, 0);

    /// <summary>
    /// `OnDataChanged`'s client-side events applied to a HUD's state: `escort_speed` when the speed or cappers change,
    /// `escort_progress` when the progress does, `escort_recede` when the recede time does.
    /// </summary>
    /// <param name="old">The watcher before the update.</param>
    /// <param name="escort">The HUD's state before.</param>
    /// <param name="now">`gpGlobals->curtime`.</param>
    /// <returns>The HUD's state after.</returns>
    public SceneEscort Apply(SceneTrainWatcher old, SceneEscort escort, float now)
    {
        if (old.Speed != Speed || old.Cappers != Cappers)
        {
            // "iSpeedLevel > 0 && m_iSpeedLevel == 0": the swoop's start (tf_hud_escort.cpp:915).
            escort = escort with
            {
                SwoopStart = Speed > 0 && escort.Speed == 0 ? now : escort.SwoopStart,
                Speed = Speed,
                Players = Cappers,
            };
        }

        if (!EqualityComparer<float>.Default.Equals(old.Progress, Progress))
        {
            escort = escort with { Progress = Progress };
        }

        if (!EqualityComparer<float>.Default.Equals(old.RecedeTime, RecedeTime))
        {
            escort = escort with { RecedeTime = RecedeTime };
        }

        return escort;
    }
}
