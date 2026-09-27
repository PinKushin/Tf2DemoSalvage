namespace Tf2DemoSalvage.Core.Scene;

/// <summary>A team entity — `CTFTeam`, as `DT_TFTeam`/`DT_Team` sends it (`team.cpp:40-44`).</summary>
/// <param name="TeamNumber">`m_iTeamNum`: 2 is RED, 3 is BLU.</param>
public readonly record struct SceneTeam(int TeamNumber)
{
    /// <summary>`m_iScore` (`team.cpp:42`).</summary>
    public int Score { get; init; }

    /// <summary>`m_iRoundsWon` (`team.cpp:43`) — the round counter's own count, separate from <see cref="Score"/>.</summary>
    public int RoundsWon { get; init; }

    /// <summary>`m_szTeamname` (`team.cpp:44`).</summary>
    public string Name { get; init; } = string.Empty;
}
