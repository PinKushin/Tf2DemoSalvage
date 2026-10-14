using System.Collections;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>Each player's earliest stated team and class, kept beside the frames rather than written into them (D217).</summary>
/// <remarks>
/// **An addition above the engine, kept on purpose.** `C_PlayerResource::GetTeam` (`c_playerresource.cpp:172`) returns
/// `m_iTeam[iIndex]` as received and never looks back, so a player sighted before the resource mentions them is
/// unassigned in TF2. The whole demo is in hand here, so the earliest statement fills that gap. The owner kept it under
/// checkpoints, 2026-10-14: *"i dont really mind a divergence if its a added feature, we cant drop anything valve does,
/// adding to it to be better is OK"*.
///
/// **A table, not a rewrite.** It was `Backfill`, a post-pass that rewrote earlier frames — which a builder that must be
/// snapshotted and resumed cannot do. The builder now only appends to this table as each frame is finished, and
/// <see cref="TimelineFrames"/> fills a frame when it is read.
/// </remarks>
internal sealed class FirstStatedRoster
{
    private readonly Dictionary<int, (int? Team, int? PlayerClass)> _earliest = [];

    /// <summary>Records the first frame in which each player states a team or a class.</summary>
    /// <param name="frame">A frame that will not be replaced again.</param>
    public void Observe(TimelineFrame frame)
    {
        foreach (ScenePlayer player in frame.Players)
        {
            if ((player.Team is not null || player.PlayerClass is not null) && !_earliest.ContainsKey(player.EntityIndex))
            {
                _earliest[player.EntityIndex] = (player.Team, player.PlayerClass);
            }
        }
    }

    /// <summary>The frame with every unstated team and class filled from the earliest statement.</summary>
    /// <param name="frame">The frame as built.</param>
    /// <returns>The same instance when nothing needed filling.</returns>
    public TimelineFrame Fill(TimelineFrame frame)
    {
        if (_earliest.Count == 0)
        {
            return frame;
        }

        List<ScenePlayer>? replaced = null;

        for (int at = 0; at < frame.Players.Count; at++)
        {
            ScenePlayer player = frame.Players[at];

            if ((player.Team is null || player.PlayerClass is null)
                && _earliest.TryGetValue(player.EntityIndex, out (int? Team, int? PlayerClass) known))
            {
                replaced ??= [.. frame.Players];
                replaced[at] = player with
                {
                    Team = player.Team ?? known.Team,
                    PlayerClass = player.PlayerClass ?? known.PlayerClass,
                };
            }
        }

        return replaced is null ? frame : frame with { Players = replaced };
    }

    /// <summary>A deep copy, for a snapshot of the builder.</summary>
    /// <returns>An independent roster with the same entries.</returns>
    public FirstStatedRoster Clone()
    {
        FirstStatedRoster copy = new();

        foreach (KeyValuePair<int, (int? Team, int? PlayerClass)> entry in _earliest)
        {
            copy._earliest[entry.Key] = entry.Value;
        }

        return copy;
    }
}

/// <summary>The timeline's frames as built, each filled from a <see cref="FirstStatedRoster"/> when read.</summary>
internal sealed class TimelineFrames(List<TimelineFrame> frames, FirstStatedRoster roster) : IReadOnlyList<TimelineFrame>
{
    /// <inheritdoc/>
    public int Count => frames.Count;

    /// <inheritdoc/>
    public TimelineFrame this[int index] => roster.Fill(frames[index]);

    /// <summary>A frame's tick, without filling it.</summary>
    /// <param name="index">Which frame.</param>
    /// <returns>Its tick.</returns>
    public int TickAt(int index) => frames[index].Tick;

    /// <inheritdoc/>
    public IEnumerator<TimelineFrame> GetEnumerator()
    {
        for (int index = 0; index < frames.Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
