using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>
/// Whether a drawn entity's position moves smoothly between ticks, and where it does not.
/// </summary>
/// <remarks>
/// **The owner's report: the demos are jittery and it is not a frame-rate thing.** That is a claim
/// about the interpolated position, so this samples it at sub-tick steps and reports the SECOND
/// difference — how much the velocity changes from step to step. Smooth interpolation gives a small,
/// continuous number; a snap gives a spike, and a spike at a keyframe boundary names the mechanism.
///
/// **The value is carried from `ScenePropTrack.At`, not recomputed** (B243). This asks the production
/// sampler the same question the renderer asks it, at the same fractional ticks, so a difference
/// between what this reports and what is drawn can only be the renderer's own smoothing.
///
/// <code>
///   jitter tf2-2026-pub-pov-clean 9
///   jitter tf2-2026-pub-pov-clean 9 1000 40
/// </code>
///
/// **Also counts which of `At`'s exits each sample took**, because the interesting failure is not a
/// wrong number but a HELD one: the sampler returns the earlier pose outright on three paths, and a
/// held pose between two moving keyframes is a stall followed by a jump.
/// </remarks>
public sealed class JitterProbe : IProbe
{
    /// <inheritdoc/>
    public string Name => "jitter";

    /// <inheritdoc/>
    public string Summary =>
        "how smoothly an entity's drawn position moves: jitter <demo> <entity> [tick] [ticks]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count < 2)
        {
            output.WriteLine("jitter <demo> <entity|model substring> [tick] [ticks]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

        // **A model substring rather than an index, because a door has no index anybody knows.**
        // A brush entity's model is `*NN` and its edict slot is whatever the server handed out, so
        // asking "are the doors smooth" needs the population rather than one number (B370, B377).
        if (!int.TryParse(
            arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entity))
        {
            Survey(output, timeline, arguments[1]);
            return;
        }

        if (timeline.TrackFor(entity) is not { } track)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"No track for entity {entity}. Tracks: " +
                $"{string.Join(", ", timeline.PlayerTracks.Take(12).Select(one => one.EntityIndex))}"));

            return;
        }

        int from = arguments.Count > 2 &&
            int.TryParse(arguments[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int at)
                ? at
                : track.Keyframes[0].Tick;

        int span = arguments.Count > 3 &&
            int.TryParse(arguments[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int many)
                ? many
                : 30;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Path.GetFileName(path)} entity {entity}: {track.Keyframes.Count:N0} keyframes, " +
            $"interval {timeline.IntervalPerTick:F6}"));

        // **Ten samples a tick, which is finer than any frame rate would ask for.** The point is not
        // to imitate the renderer's cadence but to see the CURVE: a stall and a jump are visible at
        // this resolution and invisible at one sample per tick, which is the resolution a keyframe
        // dump has.
        const int Steps = 10;

        List<(double Tick, float X, float Y, float Z)> samples = [];

        for (int step = 0; step <= span * Steps; step++)
        {
            double tick = from + ((double)step / Steps);

            if (track.At(tick) is { } pose)
            {
                samples.Add((tick, pose.X, pose.Y, pose.Z));
            }
        }

        if (samples.Count < 3)
        {
            output.WriteLine("  fewer than three samples, so nothing can be differenced.");
            return;
        }

        // First difference is the drawn speed per step; second is how much that speed CHANGED. A
        // linear interpolation between two keyframes has a second difference of zero throughout and
        // a spike only where the pair changes; a snap has a spike with a stall beside it.
        double worst = 0d;
        double worstAt = 0d;
        double moved = 0d;
        int stalls = 0;

        for (int index = 2; index < samples.Count; index++)
        {
            double firstBefore = Distance(samples[index - 2], samples[index - 1]);
            double firstNow = Distance(samples[index - 1], samples[index]);

            moved += firstNow;

            if (firstNow <= 0.0001d && firstBefore > 0.0001d)
            {
                stalls++;
            }

            double second = Math.Abs(firstNow - firstBefore);

            if (second > worst)
            {
                worst = second;
                worstAt = samples[index].Tick;
            }
        }

        // **Net displacement beside path length, because they answer different questions.** A path
        // that stalls and jumps, or oscillates, has a LONGER path length for the same endpoints — so
        // path length falling is evidence of smoothing, and net displacement changing would mean the
        // entity was moved rather than smoothed. Only the second would be a defect in a fix.
        double net = Distance(samples[0], samples[^1]);

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {samples.Count:N0} samples over {span} ticks, path {moved:F1} units, " +
            $"net displacement {net:F1} (wander {moved - net:F1})"));

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  worst step-to-step speed change {worst:F3} units at tick {worstAt:F1}; " +
            $"{stalls} stalls (a moving sample followed by a still one)"));

        // **How long the DRAWN motion takes against how long the demo says it took** (B370). A door's
        // `speed` is on the map — granary's are 300 units a second — so a 111-unit shutter is 0.37
        // seconds of travel, and the owner's report is that it looks slower than that: *"he is very
        // very close to the door by the time it opens"*. A duration is the only number that says so;
        // smoothness and direction both look fine on a motion that is simply stretched.
        Durations(output, track, samples, timeline.IntervalPerTick);

        // **The keyframes around the worst moment, with BOTH clocks**, because that is what decides
        // which pair the sampler chose. `Tick` is when the packet arrived and `AppliedAt` is the
        // entity's own `GetSimulationTime()`, and the engine's `GetInterpolationInfo` searches on the
        // latter (`interpolatedvar.h:820`) while a search on the former is a different answer
        // whenever the two disagree.
        output.WriteLine("  keyframes near the worst moment (tick, appliedAt, heldUntil, position):");

        for (int index = 0; index < track.Keyframes.Count; index++)
        {
            (int tick, ScenePose pose) = track.Keyframes[index];

            if (Math.Abs(tick - worstAt) > 12)
            {
                continue;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    {tick,6} applied {track.AppliedAt(index),6} " +
                $"held {track.HeldUntil(index),6}  " +
                $"({pose.X:F1}, {pose.Y:F1}, {pose.Z:F1})"));
        }

        // **How often the two clocks disagree at all, over the whole track.** This is the number that
        // says whether a search on the wrong one could matter: at zero it cannot, and the measured
        // 50/50 split for a player says it can.
        int away = 0;
        int backwards = 0;

        for (int index = 1; index < track.Keyframes.Count; index++)
        {
            if (track.AppliedAt(index) != track.Keyframes[index].Tick)
            {
                away++;
            }

            if (track.AppliedAt(index) < track.AppliedAt(index - 1))
            {
                backwards++;
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {away:N0} of {track.Keyframes.Count:N0} keyframes apply away from their arrival tick; " +
            $"{backwards:N0} apply EARLIER than the keyframe before them"));
    }

    /// <summary>Each motion run of one track, sampled around its own window.</summary>
    /// <param name="output">Where to report.</param>
    /// <param name="track">The track.</param>
    /// <param name="interval">Seconds per tick.</param>
    /// <remarks>
    /// **The window comes from the track's keyframes, not from a tick anybody chose.** A door's
    /// opening is about twenty-five ticks inside a fifty-thousand-tick recording, and the first attempt
    /// at this measured nothing because the tick was picked by hand and the track was not even alive
    /// there — entity 49 has seven tracks on one demo, because a round restart recreates the doors.
    ///
    /// **Sampled past the run's end by the interpolation delay**, since what is drawn at tick T is the
    /// state at T minus the delay: a window that stopped at the last stated tick would cut the drawn
    /// motion off before it finished.
    /// </remarks>
    private static void Runs(TextWriter output, ScenePropTrack track, float interval)
    {
        int reported = 0;
        int index = 1;

        while (index < track.Keyframes.Count && reported < 3)
        {
            if (Math.Abs(track.Keyframes[index].Pose.Z - track.Keyframes[index - 1].Pose.Z) <= 1f)
            {
                index++;
                continue;
            }

            // Walk to the end of this run: consecutive keyframes that keep moving.
            int last = index;

            while (last + 1 < track.Keyframes.Count &&
                   track.Keyframes[last + 1].Tick - track.Keyframes[last].Tick <= 12 &&
                   Math.Abs(track.Keyframes[last + 1].Pose.Z - track.Keyframes[last].Pose.Z) > 0.01f)
            {
                last++;
            }

            List<(double Tick, float X, float Y, float Z)> samples = [];

            for (int step = 0; step <= 600; step++)
            {
                double at = track.Keyframes[index - 1].Tick + (step / 10d);

                if (at > track.Keyframes[last].Tick + 40)
                {
                    break;
                }

                if (track.At(at) is { } pose)
                {
                    samples.Add((at, pose.X, pose.Y, pose.Z));
                }
            }

            if (samples.Count > 2)
            {
                Run(output, track, index - 1, last, samples, interval);
                reported++;
            }

            index = last + 1;
        }
    }

    /// <summary>Each run of continuous motion, drawn against stated, so a stretched one shows.</summary>
    /// <param name="output">Where to report.</param>
    /// <param name="track">The track, for the keyframes that say what the motion really was.</param>
    /// <param name="samples">The drawn positions, already sampled at sub-tick steps.</param>
    /// <param name="interval">Seconds per tick, so a duration can be stated in seconds.</param>
    /// <remarks>
    /// **Compares two durations rather than two positions**, because a stretched motion is correct at
    /// both ends. A door that starts where it should and finishes where it should, taking three times
    /// as long in between, passes every smoothness and direction check — and is exactly what the owner
    /// described. `func_door speed='300'` on granary makes a 111-unit shutter 0.37 seconds.
    /// </remarks>
    private static void Durations(
        TextWriter output,
        ScenePropTrack track,
        List<(double Tick, float X, float Y, float Z)> samples,
        float interval)
    {
        // **The STATED motion first: consecutive keyframes whose Z differs.** These are the moments the
        // demo actually spoke, so the span between the first and last of a run is what the server took.
        int firstMoving = -1;
        int lastMoving = -1;

        for (int index = 1; index < track.Keyframes.Count; index++)
        {
            if (Math.Abs(track.Keyframes[index].Pose.Z - track.Keyframes[index - 1].Pose.Z) <= 0.01f)
            {
                continue;
            }

            if (firstMoving < 0 ||
                track.Keyframes[index].Tick - track.Keyframes[lastMoving].Tick > 30)
            {
                if (firstMoving >= 0)
                {
                    Run(output, track, firstMoving, lastMoving, samples, interval);
                }

                firstMoving = index - 1;
            }

            lastMoving = index;
        }

        if (firstMoving >= 0)
        {
            Run(output, track, firstMoving, lastMoving, samples, interval);
        }
    }

    /// <summary>One run of stated motion, and how long the drawn position took to cover it.</summary>
    private static void Run(
        TextWriter output,
        ScenePropTrack track,
        int from,
        int to,
        List<(double Tick, float X, float Y, float Z)> samples,
        float interval)
    {
        float startZ = track.Keyframes[from].Pose.Z;
        float endZ = track.Keyframes[to].Pose.Z;
        float travel = Math.Abs(endZ - startZ);

        if (travel <= 1f)
        {
            return;
        }

        // **Measured on the APPLIED times, not the arrival ticks** (B370). Arrival spacing is the wire's
        // cadence and has nothing to do with how fast the door moved: the same 111-unit granary shutter
        // read 25 ticks in a quiet moment and 50 in a busy one, purely because the server sent its
        // updates further apart. The interpolation runs on `GetSimulationTime()`, so the duration the
        // demo STATED is the span between the applied times of the run's two ends.
        int statedTicks = track.AppliedAt(to) - track.AppliedAt(from);

        if (statedTicks <= 0)
        {
            return;
        }

        // **The drawn run is measured by when the sampled Z leaves one end and reaches the other.**
        // Thresholds at 1% and 99% rather than 5% and 95%: the old band covered nine tenths of the
        // travel and reported it as the whole, which biased every ratio down by about a tenth before
        // anything real was measured.
        double began = double.NaN;
        double ended = double.NaN;
        bool clipped = false;

        for (int index = 0; index < samples.Count; index++)
        {
            (double tick, _, _, float z) = samples[index];
            float along = Math.Abs(z - startZ);

            if (double.IsNaN(began) && along > travel * 0.01f)
            {
                // **A run already under way at the window's first sample is CLIPPED, not fast.** Its
                // start is outside what was sampled, so `began` would be the window edge and the
                // duration would come out short — which is most of what the 0.46x cluster was.
                clipped = index == 0;
                began = tick;
            }

            if (!double.IsNaN(began) && along >= travel * 0.99f)
            {
                ended = tick;
                break;
            }
        }

        if (double.IsNaN(began) || double.IsNaN(ended) || clipped)
        {
            return;
        }

        double drawnTicks = ended - began;

        // **Speed against speed, which is the question.** A duration comparison cannot separate "the
        // door moved at the wrong rate" from "the run detection picked a different span at each end";
        // units per tick can, because a `func_door` has ONE speed and the map states it. Granary's is
        // `speed='300'`, which at 66.67 ticks a second is 4.5 units a tick.
        double statedSpeed = travel / statedTicks;
        double drawnSpeed = drawnTicks > 0 ? travel / drawnTicks : 0d;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"    tick {track.Keyframes[from].Tick} motion of {travel:F1} units: stated {statedTicks} ticks " +
            $"({statedTicks * interval:F2}s, {statedSpeed:F2} u/tick), drawn {drawnTicks:F1} ticks " +
            $"({drawnTicks * interval:F2}s, {drawnSpeed:F2} u/tick) — " +
            $"{(statedSpeed > 0 ? drawnSpeed / statedSpeed : 0d):F2}x speed"));
    }

    /// <summary>Every track whose model matches, with the two numbers that decide whether it moves right.</summary>
    /// <param name="output">Where to report.</param>
    /// <param name="timeline">The recording.</param>
    /// <param name="model">A substring of the model path — <c>*</c> matches every brush entity.</param>
    /// <remarks>
    /// **Reports the DIRECTION the drawn motion takes, not just its smoothness** (B370). The owner's
    /// report about granary's shutters was that they ran BACKWARDS — *"they would close when a player
    /// was walking through them, even if they were open before the player got to them"* — and a
    /// backwards span is exactly what a keyframe applying earlier than its predecessor produces. So the
    /// number that matters here is how many keyframes carry a decreasing simulation time, and how far
    /// the drawn position moves against its own net direction.
    /// </remarks>
    private static void Survey(TextWriter output, DemoTimeline timeline, string model)
    {
        List<ScenePropTrack> matched =
        [
            .. timeline.Props.Where(track =>
                track.ModelPath.Contains(model, StringComparison.OrdinalIgnoreCase)),
        ];

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {matched.Count:N0} tracks whose model contains '{model}'"));

        foreach (ScenePropTrack track in matched.OrderBy(one => one.EntityIndex))
        {
            int backwards = 0;
            int away = 0;
            double travelled = 0d;

            for (int index = 1; index < track.Keyframes.Count; index++)
            {
                if (track.AppliedAt(index) < track.AppliedAt(index - 1))
                {
                    backwards++;
                }

                if (track.AppliedAt(index) != track.Keyframes[index].Tick)
                {
                    away++;
                }

                travelled += Math.Abs(track.Keyframes[index].Pose.Z - track.Keyframes[index - 1].Pose.Z);
            }

            // **Only the tracks that actually MOVE**, because a door that never opened in this
            // recording says nothing about whether a door opens correctly, and there are twenty of
            // them on the map.
            if (travelled <= 0.5d)
            {
                continue;
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    entity {track.EntityIndex,5} '{track.ModelPath}': " +
                $"{track.Keyframes.Count:N0} keyframes, {travelled:F1} units of vertical travel, " +
                $"{away:N0} apply off their arrival tick, {backwards:N0} apply BACKWARDS"));

            // **Each motion run's DRAWN duration against its STATED duration** (B370). The owner's
            // second report is that the doors are slow — *"he is very very close to the door by the
            // time it opens"* — and a stretched motion is correct at both ends, so only a duration
            // shows it. The window comes from the track's own keyframes rather than from a tick
            // somebody guessed, because a door's opening is twenty-five ticks in a fifty-thousand-tick
            // recording and picking that by hand is how the first attempt measured nothing.
            Runs(output, track, timeline.IntervalPerTick);
        }
    }

    private static double Distance(
        (double Tick, float X, float Y, float Z) first, (double Tick, float X, float Y, float Z) second)
    {
        double x = second.X - first.X;
        double y = second.Y - first.Y;
        double z = second.Z - first.Z;

        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }
}
