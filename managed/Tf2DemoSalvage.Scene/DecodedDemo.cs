using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Logging;

namespace Tf2DemoSalvage.Scene;

/// <summary>A demo read off disk and decoded: its header, and what happened over time.</summary>
/// <param name="Demo">The header and what it claims.</param>
/// <param name="Timeline">Player positions over time, or null when they could not be built.</param>
/// <remarks>
/// **Lived in <c>MainForm</c> as a private record and a static <c>Decode</c>** (B188, D90). It was
/// already static and already touched no field — the split between reading a demo and putting it on
/// screen was made when the load went off-thread — so nothing about it was ever window work. It sat
/// in the form only because that is where its caller was, which is the drift D89 names.
/// </remarks>
public sealed record DecodedDemo(LoadedDemo Demo, DemoTimeline? Timeline)
{
    /// <summary>Reads and decodes a demo. Safe to call off the UI thread.</summary>
    /// <param name="path">The demo file.</param>
    /// <param name="demo">Where the decode reports what it found.</param>
    /// <returns>The header, and the timeline when one could be built.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The timeline has its own guard, because a timeline is not worth the demo.** A file with no
    /// schema, or one truncated mid-packet, still has a header, a map name and a length worth
    /// showing — so a failure there costs the player positions and nothing else. A failure reading
    /// the demo ITSELF is not caught here: there is nothing left to show, and the caller decides
    /// what to say about it.
    /// </remarks>
    public static DecodedDemo Read(string path, ILogger demo)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(demo);

        demo.LogInformation("{Message}", $"opening {Path.GetFileName(path)}");

        LoadedDemo loaded = LoadedDemo.Load(path);

        demo.LogInformation(
            "{Message}",
            $"{loaded.MapName}, {loaded.LastTick} ticks, protocol {loaded.NetworkProtocol}" +
            (loaded.LengthWasMeasured ? ", length measured (truncated)" : string.Empty));

        DemoTimeline? timeline = null;

        try
        {
            using (demo.Time("building the position timeline"))
            {
                timeline = DemoTimeline.Build(File.ReadAllBytes(path));
            }

            Report(timeline, demo);
        }
        catch (Exception failure) when (
            failure is ArgumentException or InvalidDataException or IOException)
        {
            // **Not redundant, and a sabotage pass is what established that.** Removing it fails no
            // test, because when `Build` itself throws the assignment never completed and the local
            // is still null — so the only way this line does anything is `Report` throwing AFTER a
            // successful build. That input cannot be written from outside, so the line stays and
            // says why rather than being deleted on the strength of a green suite
            // (`docs/memory/unreachable-can-be-proved-not-just-observed.md`).
            timeline = null;
            demo.LogWarning(failure, "{Message}", "building the position timeline");
        }

        return new DecodedDemo(loaded, timeline);
    }

    /// <summary>Says what the decode found, once per demo.</summary>
    /// <remarks>
    /// **Information rather than Debug, because this runs once per demo and is the record of what
    /// opened.** The lines B191 had to silence were the per-frame ones; a load report is what makes
    /// a later "why is nothing drawing" answerable at all.
    /// </remarks>
    private static void Report(DemoTimeline timeline, ILogger demo)
    {
        demo.LogInformation(
            "{Message}",
            $"{timeline.Frames.Count} recorded moments, ticks {timeline.FirstTick} to " +
            $"{timeline.LastTick}");

        float interval = timeline.IntervalPerTick > 0f
            ? timeline.IntervalPerTick
            : PlaybackClock.DefaultIntervalPerTick;

        string source = timeline.IntervalPerTick > 0f
            ? "from svc_ServerInfo"
            : "the engine default - the demo never said";

        demo.LogInformation(
            "{Message}",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{interval:F6}s per tick ({1f / interval:F1} per second), {source}"));

        // **What is actually going to be drawn, said once per demo.** Counts here are what a defect
        // looks like from the outside: a team colour that never arrives shows up as "0 red, 0 blu"
        // the moment the file opens, rather than as grey dots that have to be noticed and then
        // chased through a seven-minute suite.
        IReadOnlyList<ScenePlayer> roster =
        [
            .. timeline.Frames
                .SelectMany(frame => frame.Players)
                .GroupBy(player => player.EntityIndex)
                .Select(group => group.First()),
        ];

        demo.LogInformation(
            "{Message}",
            $"roster: {roster.Count(p => p.Team == SceneTeams.Red)} red, " +
            $"{roster.Count(p => p.Team == SceneTeams.Blu)} blu, " +
            $"{roster.Count(p => p.Team is SceneTeams.Spectator or SceneTeams.Unassigned)} watching, " +
            $"{roster.Count(p => p.Team is null)} unknown, " +
            $"{roster.Count(p => p.PlayerClass is >= 1 and <= 9)} of {roster.Count} with a class");

        int drawn = timeline.Frames.Count == 0
            ? 0
            : timeline.PlayersAt(timeline.Frames[timeline.Frames.Count / 2].Tick)
                .Count(player => player.IsPlaying);

        demo.LogInformation("{Message}", $"{drawn} players drawn at the midpoint of the demo");
    }
}
