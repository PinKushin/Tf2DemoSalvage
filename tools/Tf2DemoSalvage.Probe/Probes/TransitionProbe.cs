using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Whether sequence transitions are created and cleared on a real demo (B346).
/// </summary>
/// <remarks>
/// **A transition needs TWO consecutive frames, which is why no existing probe could answer this.**
/// Every other animation probe calls `PropsAt` once; `CheckForSequenceChange` compares this frame's
/// sequence with the last one, so a single-frame instrument reports zero however the code behaves.
/// That is not a fact about the subject — `bone-flags` reported "0 by a discontinuity, 0 by
/// STUDIO_SNAP" for a demo that certainly contains both, purely because it samples one tick.
///
/// **The two clear reasons are reported separately on purpose.** `CheckForSequenceChange` empties
/// the queue on `(seqdesc.flags &amp; STUDIO_SNAP) || !bInterpolate`
/// (<c>sequence_Transitioner.cpp:41</c>), and only the first half was implemented until B346. A
/// non-zero snap count beside a zero jump count is precisely what a wired-but-unreachable second
/// half looks like, and one combined total would hide it.
///
/// <code>
///   transitions tf2-2026-pub-pov-cheater             the middle of the demo, 600 ticks
///   transitions tf2-2026-pub-pov-cheater 5000 9000   an explicit range
/// </code>
/// </remarks>
public sealed class TransitionProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "transitions";

    /// <inheritdoc/>
    public string Summary =>
        "sequence transitions created and cleared: transitions <demo> [from] [to]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 1)
        {
            output.WriteLine("transitions <demo> [from] [to]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        DemoTimeline timeline = DemoTimeline.Build(bytes);

        int from = arguments.Count > 1
            ? int.Parse(arguments[1], CultureInfo.InvariantCulture)
            : timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2);

        int to = arguments.Count > 2
            ? int.Parse(arguments[2], CultureInfo.InvariantCulture)
            : from + 600;

        // **The timeline's own answer first, over the WHOLE demo and with no rendering** (B346).
        // Two questions stack here and conflating them wastes a run: does the timeline ever stamp a
        // discontinuity, and does the pose path ever act on one. If the first is zero the second
        // cannot be anything else, and the fault is in the decode rather than in the transitioner.
        int jumped = 0;
        int jumpingTracks = 0;

        foreach (IReadOnlyList<(int Tick, ScenePose Pose)> keyframes in
            timeline.Props.Select(track => track.Keyframes))
        {
            double last = 0d;
            bool any = false;

            // **Indexed rather than a `foreach` over a projection, because the accumulator makes
            // this not a filter.** Each stamp is compared with the LAST one seen, so a `Where`
            // would count every keyframe after the first jump instead of each jump once — Sonar
            // asks for one and it would be wrong.
            for (int index = 0; index < keyframes.Count; index++)
            {
                double stamp = keyframes[index].Pose.DiscontinuitySeconds;

                if (stamp > last)
                {
                    last = stamp;
                    jumped++;
                    any = true;
                }
            }

            if (any)
            {
                jumpingTracks++;
            }
        }

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"timeline: {jumped} discontinuities stamped across {jumpingTracks} of " +
                $"{timeline.Props.Count} prop tracks (whole demo)"));

        // **And the same question asked of PLAYERS, which is where the answer actually is.** All
        // 332 sends that change the parity on a live entity belong to `CTFPlayer`, so a probe that
        // asked only the prop tracks would report a confident zero about the wrong population —
        // which it did, for one run, while the field was carried on the prop track alone (B346).
        Dictionary<int, double> playerJumps = [];
        List<int> jumpTicks = [];

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 4)
        {
            foreach (ScenePlayer one in timeline.PlayersAt(tick))
            {
                if (one.DiscontinuitySeconds > 0d &&
                    (!playerJumps.TryGetValue(one.EntityIndex, out double was) ||
                        one.DiscontinuitySeconds > was))
                {
                    playerJumps[one.EntityIndex] = one.DiscontinuitySeconds;
                    jumpTicks.Add(tick);
                }
            }
        }

        string where = jumpTicks.Count == 0
            ? string.Empty
            : $", first at ticks {string.Join(", ", jumpTicks.Take(8))}";

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"timeline: {jumpTicks.Count} player discontinuities across " +
                $"{playerJumps.Count} players{where}"));

        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader
            .Parse(bytes).MapName;

        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        if (mapName.Length == 0 ||
            locator.Find(mapName) is not { } mapPath ||
            locator.FindGameFolder() is not { } folder)
        {
            output.WriteLine(
                $"{Path.GetFileName(path)}: no game install or map '{mapName}' — " +
                "the pose path cannot run, so nothing can be counted.");
            return;
        }

        GameContent game = GameContent.Open(folder, NullLoggerFactory.Instance);

        LoadedMap map = LoadedMap.Read(
            File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

        if (map.Assets is not { } assets)
        {
            output.WriteLine($"{Path.GetFileName(path)}: the map's assets did not load.");
            return;
        }

        // **One set across every sample, because the queue and the counters live on it.** A fresh
        // `EntityModelSet` per tick would have no previous frame to compare against and would
        // report zero for ever — the same fault as the single-tick probes, one level down.
        EntityModelSet models = new() { Geometry = assets.Geometry };

        List<SceneProp> drawn = [];
        List<ScenePlayer> players = [];
        List<ModelInstance> instances = [];

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)} map {mapName}, ticks {from}..{to}"));

        for (int tick = from; tick <= to; tick++)
        {
            timeline.PlayersAt(tick, players);
            timeline.PropsAt(tick, drawn);

            PlayerProps.Add(
                players, drawn, new GameAppearance(game.Classes, null), (_, _, body) => body);

            models.Add(drawn, assets.Geometry);
            models.UpdateClientSideAnimations(drawn);
            models.Instances(drawn, instances, seconds: tick * timeline.IntervalPerTick);
        }

        // **The values the code USED, read off the object that did the work** (B243) — not a second
        // walk that could disagree with the first about which frames it saw.
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"sequence changes seen: {models.SequenceChangesSeen}, " +
                $"cross-fades queued: {models.TransitionsCreated}"));

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"transition queues cleared: {models.QueuesClearedByADiscontinuity} by a " +
                $"discontinuity (the bInterpolate half, B346), " +
                $"{models.QueuesClearedBySnap} by STUDIO_SNAP"));

        if (models.SequenceChangesSeen == 0)
        {
            output.WriteLine(
                "  the control is ZERO: no entity changed sequence in this window, so the clear " +
                "counts say nothing about the code. Widen the range before reading them.");
        }
    }
}
