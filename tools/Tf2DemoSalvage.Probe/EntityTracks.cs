using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe;

/// <summary>
/// Resolving a probe's entity argument to the track that argument actually names.
/// </summary>
/// <remarks>
/// **An entity index does not name a track, and a probe that treats it as one lies confidently**
/// (`docs/memory/an-entity-index-does-not-name-a-track.md`). The engine recycles edict slots, so
/// `DemoTimeline.TrackFor(entity)` hands back whichever occupant was written last — on the 2013
/// granary match `cycle … 141 8200` reported a client-side-animated player track while entity 141 at
/// that tick is `models/props_well/main_entrance_door.mdl`, a `prop_dynamic`. A plausible model name
/// on a fully-formed table is the shape that gets believed
/// (`docs/memory/instrument-bugs-outnumber-decoder-bugs.md`).
///
/// **One implementation, for the reason <see cref="DemoCorpus"/> is one**: `jitter` had solved this
/// and `cycle` had not, and two probes disagreeing about which entity they opened is the drift
/// `docs/memory/one-place-or-it-drifts.md` is about. The selection itself is
/// <see cref="DemoTimeline.TrackFor(int, double)"/> — in Core, beside the overload it corrects — and
/// what lives here is the REPORT, which is a probe's business and not the timeline's.
///
/// **The report is the half that made the doors tractable.** "No track" and "that index owns seven
/// tracks, none covering the tick you asked about" send the reader to opposite places, and only the
/// second names the ticks to ask instead.
/// </remarks>
internal static class EntityTracks
{
    /// <summary>The track an index names at a tick, or a listing of what it does name.</summary>
    /// <param name="output">Where to report a miss.</param>
    /// <param name="timeline">The recording.</param>
    /// <param name="entity">Slot in the entity table.</param>
    /// <param name="tick">The moment being asked about, or a negative tick for "any occupant".</param>
    /// <returns>The track, or <c>null</c> — in which case the windows have been written to output.</returns>
    /// <remarks>
    /// **A negative tick means the caller has no moment yet**, which is `jitter`'s default: it picks
    /// the tick FROM the track it finds, because a door's whole motion is twenty-five ticks inside a
    /// fifty-thousand-tick recording and a tick chosen by hand lands outside it. That is the one case
    /// where "any occupant" is the right question, and it is stated rather than reached by accident.
    /// </remarks>
    public static ScenePropTrack? Select(
        TextWriter output, DemoTimeline timeline, int entity, double tick)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(timeline);

        IReadOnlyList<ScenePropTrack> owned = timeline.TracksFor(entity);

        if (tick < 0)
        {
            if (owned.Count > 0)
            {
                return owned[0];
            }
        }
        else if (timeline.TrackFor(entity, tick) is { } alive)
        {
            return alive;
        }

        output.WriteLine(Describe(timeline, entity, tick));
        return null;
    }

    /// <summary>Why the index answered nothing, and every window it does answer for.</summary>
    /// <param name="timeline">The recording.</param>
    /// <param name="entity">Slot in the entity table.</param>
    /// <param name="tick">The moment asked about, or negative.</param>
    /// <returns>One line naming the count and each track's window.</returns>
    /// <remarks>
    /// **The window printed is <see cref="ScenePropTrack.EndTick"/>, the bound the selection tested**
    /// — not the last keyframe, which is a different number and can put an accepted tick outside the
    /// range this prints (B243).
    /// </remarks>
    public static string Describe(DemoTimeline timeline, int entity, double tick)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        IReadOnlyList<ScenePropTrack> owned = timeline.TracksFor(entity);

        if (owned.Count == 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"No track for entity {entity}: the demo records nothing for that index at all.");
        }

        List<string> windows = [];

        foreach (ScenePropTrack track in owned)
        {
            windows.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"[{track.FirstTick}..{(track.EndTick == int.MaxValue ? "end" : track.EndTick.ToString(CultureInfo.InvariantCulture))}]"
                + $" '{track.ModelPath}'"));
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"No track for entity {entity}{(tick >= 0 ? $" alive at tick {tick:0.##}" : string.Empty)}. "
            + $"That index owns {owned.Count} track(s): {string.Join(", ", windows)}");
    }
}
