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

        // **Where the miniguns are, before rendering anything** (B347). "Zero barrels spun" has two
        // causes — the spin never runs, or no minigun is drawn in the window — and only this tells
        // them apart. It is the same stacking the discontinuity scan above uses, and it is cheap
        // because it asks the timeline rather than the pose path.
        Dictionary<int, int> spunAt = [];
        List<SceneProp> scannedProps = [];
        int miniguns = 0;
        List<int> minigunTicks = [];
        string minigunModel = string.Empty;

        for (int tick = timeline.FirstTick; tick <= timeline.LastTick; tick += 4)
        {
            timeline.PropsAt(tick, scannedProps);

            foreach (SceneProp one in scannedProps)
            {
                if (one.Pose.MinigunState is { } minigun && !spunAt.ContainsKey(tick))
                {
                    spunAt[tick] = minigun;
                }

                // **The control one level below.** "No tick carries a minigun state" has two causes
                // — no minigun is drawn, or the state does not reach the pose — and counting the
                // MODEL separates them without rendering anything.
                if (one.ModelPath.Contains("minigun", StringComparison.OrdinalIgnoreCase))
                {
                    miniguns++;

                    if (minigunTicks.Count < 8 && !minigunTicks.Contains(tick))
                    {
                        minigunTicks.Add(tick);
                        minigunModel = one.ModelPath;
                    }
                }
            }
        }

        string spinning = spunAt.Count == 0
            ? string.Empty
            : $", first at ticks {string.Join(", ", spunAt.Keys.Take(8))}";

        string seenAs = minigunTicks.Count == 0
            ? string.Empty
            : $", from tick {minigunTicks[0]} as '{minigunModel}'";

        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"timeline: {spunAt.Count} sampled ticks carry a minigun state{spinning}; " +
                $"{miniguns} minigun-model props seen across the same samples{seenAs}"));

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

        // **Whether a minigun's barrel ever spins on a real demo** (B347). The conformance tests
        // pin the arithmetic against the engine; only this says production reaches it. The ANGLE is
        // reported beside the count for the reason the IK locks report their effect: a non-zero
        // count with a zero angle means every minigun in the window is idle, which looks identical
        // on screen to the spin never running and is a different fact about the demo.
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"barrels spun: {models.SpunBarrels} bone writes, furthest angle " +
                $"{models.FurthestBarrelAngle:0.###} rad"));

        // **Whether a flinch the model cannot play falls back to the chest one** (B350). The
        // conformance suite proves the substitution picks chest; only this says a real demo asks
        // for it. Zero here, on a demo whose `CTEPlayerAnimEvent` stream fires 69 non-chest flinch
        // events, would mean they never reach the gesture feed at all.
        output.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"flinches substituted to CHEST: {models.SubstitutedFlinches}"));

        if (models.SequenceChangesSeen == 0)
        {
            output.WriteLine(
                "  the control is ZERO: no entity changed sequence in this window, so the clear " +
                "counts say nothing about the code. Widen the range before reading them.");
        }
    }
}
