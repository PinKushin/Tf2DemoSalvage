namespace Tf2DemoSalvage.Core.Scene;

/// <summary>One `CTeamTrainWatcher`'s networked state when it changed (`DT_TeamTrainWatcher`, c_team_train_watcher.cpp).</summary>
/// <param name="Tick">The tick it changed.</param>
/// <param name="Team">`GetTeamNumber()`.</param>
/// <param name="Progress">`m_flTotalProgress`.</param>
/// <param name="Speed">`m_iTrainSpeedLevel`.</param>
/// <param name="RecedeTime">`m_flRecedeTime`.</param>
/// <param name="Cappers">`m_nNumCappers`.</param>
public readonly record struct SceneTrainWatcher(int Tick, int Team, float Progress, int Speed, float RecedeTime, int Cappers);
