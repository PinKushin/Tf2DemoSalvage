using System;
using System.Collections.Generic;
using System.Linq;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>The payload track the objective resource carries (c_team_objectiveresource.h:213-269, :303-337).</summary>
/// <param name="PathDistances">`m_flPathDistance`, each control point's share along the track.</param>
/// <param name="HillCounts">`m_nNumNodeHillData` by team, 0 through 3.</param>
/// <param name="HillData">`m_flNodeHillData`: a start and an end per hill, `TEAM_TRAIN_MAX_HILLS` hills per team.</param>
/// <param name="TrackAlarms">`m_bTrackAlarm` by team.</param>
/// <param name="Downhills">`m_bHillIsDownhill`, `hill + team * TEAM_TRAIN_MAX_HILLS`.</param>
public sealed record SceneTrack(
    IReadOnlyList<float> PathDistances,
    IReadOnlyList<int> HillCounts,
    IReadOnlyList<float> HillData,
    IReadOnlyList<bool> TrackAlarms,
    IReadOnlyList<bool> Downhills)
{
    /// <summary>`TEAM_TRAIN_MAX_TEAMS` (shareddefs.h:947).</summary>
    public const int MaxTeams = 4;

    /// <summary>`TEAM_TRAIN_MAX_HILLS`.</summary>
    public const int MaxHills = 5;

    /// <summary>`GetNumNodeHillData( team )`: 0 past the teams.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The hills.</returns>
    public int HillCount(int team) => team is >= 0 and < MaxTeams ? HillCounts[team] : 0;

    /// <summary>`GetHillData( team, hill, start, end )`: (0, 0) outside the array.</summary>
    /// <param name="team">The team.</param>
    /// <param name="hill">The hill.</param>
    /// <returns>Its start and end along the track.</returns>
    public (float Start, float End) Hill(int team, int hill)
    {
        int index = (hill * 2) + (team * MaxHills * 2);

        return hill is >= 0 and < MaxHills && team is >= 0 and < MaxTeams && index < HillData.Count - 1
            ? (HillData[index], HillData[index + 1])
            : (0f, 0f);
    }

    /// <summary>`IsHillDownhill( team, hill )`.</summary>
    /// <param name="team">The team.</param>
    /// <param name="hill">The hill.</param>
    /// <returns>Whether it runs down.</returns>
    public bool IsDownhill(int team, int hill) =>
        team is >= 0 and < MaxTeams && hill is >= 0 and < MaxHills && Downhills[hill + (team * MaxHills)];

    /// <summary>`GetTrackAlarm( team )`.</summary>
    /// <param name="team">The team.</param>
    /// <returns>Whether its alarm sounds.</returns>
    public bool TrackAlarm(int team) => team is >= 0 and < MaxTeams && TrackAlarms[team];

    /// <inheritdoc/>
    public bool Equals(SceneTrack? other) =>
        other is not null
        && PathDistances.SequenceEqual(other.PathDistances)
        && HillCounts.SequenceEqual(other.HillCounts)
        && HillData.SequenceEqual(other.HillData)
        && TrackAlarms.SequenceEqual(other.TrackAlarms)
        && Downhills.SequenceEqual(other.Downhills);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(PathDistances.Count, HillCounts.Sum());
}
