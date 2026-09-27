using System.Collections.Generic;
using System.Linq;

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

    /// <summary>`m_aPlayers` (`c_team.h:63`): every entity index on this team, as `player_array` sent it.</summary>
    public IReadOnlyList<int> Players { get; init; } = [];

    /// <summary>
    /// Hand-written so <see cref="Players"/> compares by sequence rather than by list-instance reference —
    /// the compiler-generated record equality would use <see cref="object.Equals(object?)"/> per field.
    /// </summary>
    public bool Equals(SceneTeam other) =>
        TeamNumber == other.TeamNumber
        && Score == other.Score
        && RoundsWon == other.RoundsWon
        && Name == other.Name
        && Players.SequenceEqual(other.Players);

    /// <inheritdoc/>
    public override int GetHashCode() => System.HashCode.Combine(TeamNumber, Score, RoundsWon, Name);
}
