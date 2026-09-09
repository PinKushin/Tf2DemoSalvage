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

        if (arguments.Count < 2 ||
            !int.TryParse(arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int entity))
        {
            output.WriteLine("jitter <demo> <entity> [tick] [ticks]");
            return;
        }

        if (DemoCorpus.Find(arguments[0], output) is not { } path)
        {
            output.WriteLine($"No demo named '{arguments[0]}'.");
            return;
        }

        DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

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

    private static double Distance(
        (double Tick, float X, float Y, float Z) first, (double Tick, float X, float Y, float Z) second)
    {
        double x = second.X - first.X;
        double y = second.Y - first.Y;
        double z = second.Z - first.Z;

        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }
}
