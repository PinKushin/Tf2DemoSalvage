using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

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
                + $"  playerSpeed {(asPlayer is { } who ? who.Speed.ToString("0.##", CultureInfo.InvariantCulture) : "-")}"
                + $"  moveX {(asPlayer is { } m ? m.MoveX.ToString("0.###", CultureInfo.InvariantCulture) : "-")}");

            previous = cycle;
        }
    }
}
