using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// One entity's animation cycle sampled BETWEEN ticks, which is where stepping shows.
/// </summary>
/// <remarks>
/// **Every other animation probe here samples per tick, and per-tick is exactly the resolution that
/// cannot see this defect.** The viewer draws at several hundred frames a second against a demo
/// whose snapshots arrive fifteen to sixty times a second, so an animation that advances only when
/// a snapshot does looks smooth in a per-tick table and steps on screen. The owner's report —
/// *"its still animating in steps not like it should be"* — is a statement about the frames
/// between the ticks.
///
/// <code>
///   cycle z1800 691 20000        one entity, from tick 20000
///   cycle z1800 691 20000 0.05   finer steps
/// </code>
///
/// Prints the cycle and the DELTA between samples. A smooth animation shows a near-constant delta;
/// one that steps shows runs of zero with an occasional jump, and the size of the run is the
/// snapshot interval.
/// </remarks>
public sealed class CycleProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "cycle";

    /// <inheritdoc/>
    public string Summary =>
        "an entity's cycle sampled between ticks: cycle <demo> <entity> [tick] [step]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("cycle <demo> <entity> [tick] [step]");
            return;
        }

        string? path = DemoCorpus.Find(arguments[0], output);
        if (path is null)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        int entity = int.Parse(arguments[1], CultureInfo.InvariantCulture);

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        double from = arguments.Count > 2
            ? double.Parse(arguments[2], CultureInfo.InvariantCulture)
            : timeline.FirstTick + ((timeline.LastTick - timeline.FirstTick) / 2.0);

        double step = arguments.Count > 3
            ? double.Parse(arguments[3], CultureInfo.InvariantCulture)
            : 0.1;

        output.WriteLine(
            $"{Path.GetFileName(path)} entity {entity.ToString(CultureInfo.InvariantCulture)} "
            + $"from tick {from.ToString("0.##", CultureInfo.InvariantCulture)} "
            + $"step {step.ToString("0.###", CultureInfo.InvariantCulture)}");

        // **The track's own keyframes first, because the sampled shape is only readable against
        // them.** A run of identical cycles that lines up with the gap between two keyframes is a
        // different fault from one that does not.
        if (timeline.TrackFor(entity) is { } track)
        {
            output.WriteLine(
                $"  track: {track.Keyframes.Count.ToString(CultureInfo.InvariantCulture)} keyframes, "
                + $"client-side animated {track.ClientSideAnimated}");

            foreach ((int tick, ScenePose pose) in track.Keyframes
                .Where(frame => frame.Tick >= from - 20 && frame.Tick <= from + 40)
                .Take(12))
            {
                output.WriteLine(
                    $"    keyframe tick {tick.ToString(CultureInfo.InvariantCulture),7} "
                    + $"cycle {pose.Cycle:0.#####} sequence {pose.Sequence}");
            }
        }
        else
        {
            output.WriteLine("  track: NONE — the demo records nothing for that entity");
        }

        // **Sampled off the TRACK, not out of `PropsAt`.** A player is not a prop: `PropsAt`
        // reports entities the demo describes with models, and a player becomes one only when
        // `MomentScene` puts it there. The first version of this asked `PropsAt` and printed
        // "absent" forty times for an entity whose track holds 6,743 keyframes — the third probe
        // today to report a confident nothing because it looked in the wrong list.
        //
        // The track is also the more direct subject: `At` is the interpolation under inspection.
        if (timeline.TrackFor(entity) is not { } sampled)
        {
            return;
        }

        // **The frame the sampler was HANDED, which is the only number that tells gliding from
        // animating** (B280). A player's cycle is not on the wire, so the pose's cycle reads zero
        // for ever and says nothing; what moves the model is the phase `EntityModelSet.Simulate`
        // computes and the frame and fraction it hands the skeleton. Read back through `FrameOf`
        // — carried, not recomputed — after driving the production pipeline exactly as the viewer
        // does: players into props, the activity selection, then `Instances`.
        MapLocator locator = new(MapProvider.SteamLibraryFile, MapProvider.OwnMapsFolder);

        string mapName = Tf2DemoSalvage.Core.Container.DemoHeader
            .Parse(File.ReadAllBytes(path)).MapName;

        EntityModelSet? models = null;
        GameContent? game = null;
        Func<string, PropModels.ModelFrames?>? geometry = null;

        if (mapName.Length > 0
            && locator.Find(mapName) is { } mapPath
            && locator.FindGameFolder() is { } folder)
        {
            game = GameContent.Open(folder, NullLoggerFactory.Instance);

            LoadedMap map = LoadedMap.Read(
                File.ReadAllBytes(mapPath), game, timeline, 0, NullLoggerFactory.Instance);

            if (map.Assets is { } assets)
            {
                geometry = assets.Geometry;
                models = new EntityModelSet { Geometry = assets.Geometry };
            }
        }

        if (models is null)
        {
            output.WriteLine("  (no game install found: the posed frame column is unavailable)");
        }

        List<SceneProp> drawn = [];
        List<ModelInstance> instances = [];

        // **`Speed` is what decides whether a player is animated at all.**
        // `EntityModelSet.UpdateClientSideAnimations` skips any prop whose pose carries no speed —
        // `if (prop.Pose.Speed is not { } speed) continue;` — because that is the engine's
        // `g_ClientSideAnimationList` membership test in this project's terms. A null there leaves
        // the player on whatever sequence the wire decoded, which is zero, and they hold one pose
        // while their position interpolates. Printed from `PlayersAt`, which is where it is
        // derived, rather than recomputed here.
        List<ScenePlayer> players = [];
        double? previous = null;

        for (int sample = 0; sample < 40; sample++)
        {
            double at = from + (sample * step);

            timeline.PlayersAt(at, players);

            ScenePlayer? asPlayer = players.FirstOrDefault(one => one.EntityIndex == entity);

            // The production pipeline, in the viewer's order: entities and players into one drawn
            // list, the activity-driven sequence selection, then `Instances` — which is where
            // `Simulate` advances the cycle and hands the skeleton a frame and a fraction.
            (int Sequence, int Frame, float Fraction)? posedAt = null;

            // **The prop as `Simulate` saw it, read out of the drawn list AFTER the activity
            // selection wrote into it.** `UpdateClientSideAnimations` skips any prop whose pose has
            // no `Speed`, and `Simulate` takes the sequence off that same pose — so whether a
            // player animates at all is decided by two fields on this record, and the only honest
            // way to report them is to read the record production read.
            SceneProp? asDrawn = null;

            if (models is { } set && geometry is { } load && game is { } content)
            {
                timeline.PropsAt(at, drawn);
                PlayerProps.Add(
                    players, drawn, new GameAppearance(content.Classes, null), (_, _, body) => body);

                set.Add(drawn, load);
                set.UpdateClientSideAnimations(drawn);

                asDrawn = drawn.FirstOrDefault(one => one.EntityIndex == entity);

                set.Instances(drawn, instances, seconds: at * timeline.IntervalPerTick);

                posedAt = set.FrameOf(entity);
            }

            if (sampled.At(at) is not { } pose)
            {
                output.WriteLine(
                    $"  {at.ToString("0.###", CultureInfo.InvariantCulture),10}  absent");
                continue;
            }

            double cycle = pose.Cycle;

            // **`AnimationStartSeconds` decides whether a client-side-animated entity moves at
            // all.** `EntityModelSet.Simulate` advances such an entity by
            // `elapsed * CyclesPerSecond` where `elapsed = seconds - AnimationStartSeconds`, so a
            // start that keeps being re-stamped leaves elapsed near zero and the model frozen in
            // one pose while its POSITION still interpolates — which is exactly what sliding looks
            // like. Printed beside the cycle because the pose's own cycle is zero for a player and
            // says nothing on its own.
            output.WriteLine(
                $"  {at.ToString("0.###", CultureInfo.InvariantCulture),10}  "
                + $"cycle {cycle:0.#####}  "
                + $"delta {(previous is { } was ? (cycle - was).ToString("+0.#####;-0.#####;0", CultureInfo.InvariantCulture) : "-")}"
                + $"  start {pose.AnimationStartSeconds:0.####}"
                + $"  seq {pose.Sequence}"
                + (posedAt is { } handed
                    ? $"  POSED seq {handed.Sequence.ToString(CultureInfo.InvariantCulture)}"
                      + $" frame {handed.Frame.ToString(CultureInfo.InvariantCulture)}+{handed.Fraction:0.###}"
                    : "  POSED -")
                + $"  drawnSeq {(asDrawn is { } shown ? shown.Pose.Sequence.ToString(CultureInfo.InvariantCulture) : "-")}"
                + $"  drawnSpeed {(asDrawn?.Pose.Speed is { } fast ? fast.ToString("0.#", CultureInfo.InvariantCulture) : "NULL")}"
                + $"  csa {(asDrawn is { } anim ? anim.ClientSideAnimated.ToString() : "-")}"

                // **The layers the SKELETON was handed** (B282), not the gestures the timeline
                // collected. A gesture that resolves to no sequence on this model is dropped
                // exactly as the engine drops it, so the two counts differ legitimately and only
                // the second one says whether anything was drawn.
                + $"  gestures {(asDrawn?.Pose.Gestures?.Count ?? 0).ToString(CultureInfo.InvariantCulture)}"
                + $"  layers {(models?.LayersOf(entity)?.Count ?? 0).ToString(CultureInfo.InvariantCulture)}"

                // Each gesture's activity and how old it is, because "resolved to no sequence on
                // this model" and "expired and auto-killed" both show as zero layers and want
                // opposite fixes.
                + (asDrawn?.Pose.Gestures is { Count: > 0 } held
                    ? "  [" + string.Join(
                        " ",
                        held.Select(one =>
                            $"{one.ActivityName}@{(at * timeline.IntervalPerTick) - one.StartedSeconds:0.0}s")) + "]"
                    : string.Empty)
                + $"  playerSpeed {(asPlayer is { } who ? who.Speed.ToString("0.##", CultureInfo.InvariantCulture) : "-")}"
                + $"  moveX {(asPlayer is { } m ? m.MoveX.ToString("0.###", CultureInfo.InvariantCulture) : "-")}");

            previous = cycle;
        }
    }
}
