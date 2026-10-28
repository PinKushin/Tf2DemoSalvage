using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Scene;
using Tf2DemoSalvage.Core.Tests;
using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Probe.Probes;

/// <summary>Whether the first-person camera path a POV demo plays back is evenly paced, against synthetic controls.</summary>
/// <remarks>
/// **The production path, stepped as the viewer steps it**: a <see cref="PlaybackClock"/> advanced by one frame's seconds,
/// its fractional <see cref="PlaybackClock.Position"/> handed to <see cref="TimelineEyes.ViewpointAt"/>, which is
/// <see cref="DemoPlayer.InterpolateViewpoint"/>. Nothing here recomputes the blend (B243). Snaps are counted by a second
/// player over the same timeline with <c>demo_interplimit</c> and <c>demo_avellimit</c> infinite: a frame whose two
/// answers differ is one the limits snapped — production with a ConVar changed, not a reimplementation.
///
/// **Pacing is measured against a local median**, because a real player stops, starts and turns: the speed of frame i
/// over the median of the eleven frames around it is 1 on an evenly paced path at any speed. A frame is "moving" when
/// that median is over <see cref="Moving"/> units a frame.
///
/// **Three controls, every run** (the instrument is proved before it is believed):
/// (a) a constant-velocity line, one packet every tick — must read CV ~0, no stalls;
/// (b1) the same line at the subject's packet rate, evenly spaced in true time, each stamped floor(time / interval) —
/// what that rate alone does against the tick;
/// (b2) the same line, with the subject demo's own packet tick stamps and each packet at an evenly spaced true time —
/// what this demo's arrival-stamp jitter alone does to a perfectly smooth motion.
/// Then the server's <c>net_Tick</c> inside each packet beside its stamp, which is whether the true time is recoverable.
/// `docs/measurements/2026-10-27-camera-path-pacing.md`.
/// </remarks>
public sealed class CameraPathProbe : IProbe
{
    /// <summary>A median speed below this (units a frame) is standing still, and its frames are not judged.</summary>
    private const double Moving = 0.05;

    /// <summary>Frames either side of the local median.</summary>
    private const int Half = 5;

    /// <summary>The synthetic line's speed, units a second: a TF2 run.</summary>
    private const float LineSpeed = 300f;

    /// <summary>The synthetic line's turn, degrees a second.</summary>
    private const float LineTurn = 90f;

    /// <summary>The synthetic demos' first packet tick.</summary>
    private const int LineStart = 100;

    /// <inheritdoc/>
    public string Name => "camerapath";

    /// <inheritdoc/>
    public string Summary => "first-person camera pacing through DemoPlayer, with synthetic controls: camerapath <demo> [hz=144]";

    /// <inheritdoc/>
    public void Run(TextWriter output, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(arguments);

        if (!ProbeArguments.TryDemo(arguments, Summary, output, out string path) ||
            !ProbeArguments.TryCount(arguments, 1, 144, 1, output, out int hz))
        {
            return;
        }

        byte[] bytes = File.ReadAllBytes(path);
        DemoTimeline subject = DemoTimeline.Build(bytes);
        int[] stamps = ViewPackets(subject);

        int first = stamps[0];
        int count = stamps.Length;
        // The synthetic lines are written at SyntheticViews.Interval; the subject's own rate decides its seconds.
        double seconds = (stamps[^1] - first) * (double)subject.IntervalPerTick;
        double spacing = seconds / (count - 1);

        Report(output, "control (a) line, a packet every tick", Line([.. Enumerable.Range(0, 2400)], at => at * (double)SyntheticViews.Interval), hz);

        // The subject's packet rate with no jitter: each packet stamped with the tick its own even time falls in.
        Report(output, "control (b1) line, this demo's packet rate, stamped floor(even time / interval)",
            Line([.. Enumerable.Range(0, count).Select(at => (int)Math.Floor(at * spacing / SyntheticViews.Interval))], at => at * spacing), hz);

        // The subject's own stamps, each packet at its even true time: arrival jitter alone.
        Report(output, "control (b2) line, this demo's own stamps at even true times",
            Line([.. stamps.Select(tick => tick - first)], at => at * spacing), hz);

        Report(output, string.Create(CultureInfo.InvariantCulture, $"subject {Path.GetFileName(path)}"), subject, hz);
        ServerTicks(output, bytes, [.. subject.ViewCommands.Where(command => command.Type == DemoCommandType.Packet).Select(command => command.View)]);
    }

    /// <summary>One <c>dem_packet</c>: its stamp, the last <c>net_Tick</c> inside it (null with none), its recorded view.</summary>
    private readonly record struct Sample(int Stamp, int? Server, RecordedView View);

    /// <summary>
    /// The server's own tick inside each <c>dem_packet</c> (<c>net_Tick</c>) beside the packet's stamp — whether the server
    /// tick advances evenly while the stamp does not — and whose clock the recorded views follow: each packet's view step
    /// over the local median step, grouped by the stamp's step and by the server tick's.
    /// </summary>
    /// <param name="output">Where the report goes.</param>
    /// <param name="bytes">The whole demo file.</param>
    /// <param name="views">The timeline's packet views, in stream order — the same packets this walk reads.</param>
    private static void ServerTicks(TextWriter output, byte[] bytes, RecordedView[] views)
    {
        NetDecodeState state = new() { NetworkProtocol = (ushort)DemoHeader.Parse(bytes).NetworkProtocol };
        List<(int Stamp, int? Server)> packets = [];
        Dictionary<int, int> perPacket = [];
        List<float> frameTimes = [];
        List<float> frameDeviations = [];

        foreach (DemoCommand command in DemoCommandReader.Read(bytes.AsMemory(DemoHeader.SizeBytes)))
        {
            if (command.Type is not (DemoCommandType.Packet or DemoCommandType.Signon))
            {
                continue;
            }

            NetTickMessage[] ticks = [.. NetMessageReader.Read(command.Payload.Span, state).Messages.OfType<NetTickMessage>()];

            if (command.Type != DemoCommandType.Packet)
            {
                continue;
            }

            perPacket[ticks.Length] = perPacket.GetValueOrDefault(ticks.Length) + 1;
            packets.Add((command.Tick, ticks.Length > 0 ? ticks[^1].Tick : null));
            frameTimes.AddRange(ticks.Select(tick => tick.HostFrameTimeSeconds));
            frameDeviations.AddRange(ticks.Select(tick => tick.HostFrameTimeStdDevSeconds));
        }

        output.WriteLine();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"== net_Tick inside the subject's dem_packets ({packets.Count} read here, {views.Length} in the timeline)"));
        output.WriteLine("net_Tick per packet: " + string.Join(' ',
            perPacket.OrderBy(pair => pair.Key).Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}x{pair.Value}"))));

        if (frameTimes.Count > 0)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"host_frametime mean {frameTimes.Average() * 1000:0.000} ms, min {frameTimes.Min() * 1000:0.000}, max {frameTimes.Max() * 1000:0.000}; " +
                $"reported stddev mean {frameDeviations.Average() * 1000:0.000} ms"));
        }

        if (packets.Count != views.Length)
        {
            output.WriteLine("packet counts differ: the view-step grouping is not reported");
            return;
        }

        Sample[] samples = [.. packets.Select((packet, at) => new Sample(packet.Stamp, packet.Server, views[at]))];
        CrossTab(output, samples);
        ViewSteps(output, samples);
    }

    /// <summary>Stamp step against server-tick step over consecutive packets that both carry a <c>net_Tick</c>.</summary>
    private static void CrossTab(TextWriter output, Sample[] samples)
    {
        IEnumerable<string> rows = Enumerable.Range(1, samples.Length - 1)
            .Where(at => samples[at].Server is not null && samples[at - 1].Server is not null)
            .GroupBy(at => (Stamp: Math.Min(samples[at].Stamp - samples[at - 1].Stamp, 3), Server: Math.Min(samples[at].Server.GetValueOrDefault() - samples[at - 1].Server.GetValueOrDefault(), 3)))
            .OrderBy(group => group.Key.Stamp).ThenBy(group => group.Key.Server)
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"stamp {group.Key.Stamp} server {group.Key.Server}: {group.Count()}"));

        output.WriteLine("stamp step x server step (3 = 3 or more): " + string.Join("; ", rows));
        output.WriteLine("stamp - server tick: " + Counts(samples.Where(sample => sample.Server is not null).Select(sample => sample.Stamp - sample.Server.GetValueOrDefault())));
    }

    /// <summary>
    /// Each packet's recorded origin step over the median of the eleven steps around it, grouped by the stamp's step and
    /// by the server tick's: ~0 at stamp 0 and ~2 at stamp 2 means the views were sampled on the stamp's clock; ~1 across
    /// stamp steps, tracking the server step, means the stamps are the uneven part.
    /// </summary>
    private static void ViewSteps(TextWriter output, Sample[] samples)
    {
        double[] step = [.. Enumerable.Range(1, samples.Length - 1).Select(at => Distance(samples[at - 1].View.Origin, samples[at].View.Origin))];
        double[] median = LocalMedians(step);
        int[] moving = [.. Enumerable.Range(0, step.Length).Where(at => median[at] > 0.5)];

        string Grouped(Func<int, int?> key) => string.Join("; ", moving
            .Where(at => key(at) is not null)
            .GroupBy(at => Math.Min(key(at).GetValueOrDefault(), 3))
            .OrderBy(group => group.Key)
            .Select(group => string.Create(CultureInfo.InvariantCulture,
                $"{group.Key}: n {group.Count()} step/median mean {group.Average(at => step[at] / median[at]):0.000}")));

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"recorded origin step / local median, {moving.Length} moving packet steps"));
        output.WriteLine("  by stamp step: " + Grouped(at => samples[at + 1].Stamp - samples[at].Stamp));
        output.WriteLine("  by server step: " + Grouped(at =>
            samples[at + 1].Server is { } next && samples[at].Server is { } previous ? next - previous : null));
    }

    private static string Counts(IEnumerable<int> values) => string.Join(' ',
        values.GroupBy(value => value).OrderBy(group => group.Key)
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key}x{group.Count()}")));

    /// <summary>A synthetic POV demo: packets at <paramref name="ticks"/> (offset to <see cref="LineStart"/>), packet k at the line's position at <paramref name="time"/>(k).</summary>
    private static DemoTimeline Line(int[] ticks, Func<int, double> time)
    {
        List<DemoCommand> commands = [];

        for (int at = 0; at < ticks.Length; at++)
        {
            float t = (float)time(at);
            commands.Add(SyntheticViews.Packet(LineStart + ticks[at], (LineSpeed * t, 0f, 0f), (0f, (LineTurn * t) % 360f, 0f)));
        }

        commands.Add(SyntheticViews.Stop(LineStart + ticks[^1]));

        return DemoTimeline.Build(SyntheticViews.Demo(24, commands));
    }

    /// <summary>The ticks of the <c>dem_packet</c>s carrying a view; a synthetic demo's <c>svc_ServerInfo</c> packet at tick 0 carries the default one.</summary>
    private static int[] ViewPackets(DemoTimeline timeline) =>
        [.. timeline.ViewCommands.Where(command => command.Type == DemoCommandType.Packet && !command.View.IsDefault).Select(command => command.Tick)];

    private static void Report(TextWriter output, string title, DemoTimeline timeline, int hz)
    {
        output.WriteLine();
        output.WriteLine("== " + title);

        int[] packets = ViewPackets(timeline);
        Histogram(output, packets, timeline.IntervalPerTick);

        // ParseAheadForInterval answers false within 8 ticks of the dem_stop, so the tail plays stepped by design: judged
        // frames are those whose whole tick is past the first packet and 9 or more short of the last.
        List<Frame> played = Play(timeline, hz, packets[0], packets[^1]);
        List<Frame> frames = [.. played.Where(frame => Math.Floor(frame.Position) >= packets[0] + 1 && Math.Floor(frame.Position) <= packets[^1] - 9)];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"frames {frames.Count} judged ({played.Count - frames.Count} at the ends excluded) at {hz} Hz, demo_interplimit/avellimit snaps {frames.Count(frame => frame.Snapped)}"));

        Summarise(output, "origin", frames, frame => frame.Move, packets);
        Summarise(output, "angles", frames, frame => frame.Turn, packets);
    }

    private static void Histogram(TextWriter output, int[] packets, float interval)
    {
        IEnumerable<string> deltas = packets.Skip(1).Select((tick, at) => tick - packets[at])
            .GroupBy(delta => delta).OrderBy(group => group.Key)
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key}x{group.Count()}"));
        double seconds = (packets[^1] - packets[0]) * interval;

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"packets {packets.Length} over {seconds:0.000} s = {(packets.Length - 1) / seconds:0.00}/s (tick {1 / interval:0.00} Hz); tick deltas {string.Join(' ', deltas)}"));
    }

    /// <summary>One drawn frame: where the clock was, how far the camera moved and turned since the last, whether it snapped.</summary>
    private readonly record struct Frame(double Position, double Move, double Turn, bool Snapped);

    /// <summary>The viewer's loop: advance the clock one frame, ask the eye source at the fractional position.</summary>
    /// <remarks>
    /// Played from the first view-bearing packet to the last: a synthetic demo's header states no length, so the
    /// timeline's own last tick is not used.
    /// </remarks>
    private static List<Frame> Play(DemoTimeline timeline, int hz, int firstTick, int lastTick)
    {
        TimelineEyes eyes = new(timeline, new DemoPlayer(timeline));
        DemoPlayer unlimited = new(timeline)
        {
            ConVars = () => DemoViewConVars.Defaults with { InterpLimit = float.PositiveInfinity, AvelLimit = float.PositiveInfinity },
        };

        PlaybackClock clock = new(timeline.IntervalPerTick, lastTick);
        clock.Seek(firstTick);

        List<Frame> frames = [];
        RecordedView? last = null;

        while (!clock.AtEnd)
        {
            clock.Advance(1d / hz);
            double position = clock.Position;
            RecordedView? view = eyes.ViewpointAt(position);
            RecordedView? free = unlimited.InterpolateViewpoint(position);

            if (view is { } now && last is { } before)
            {
                frames.Add(new Frame(position, Distance(before.Origin, now.Origin), Turned(before.Angles, now.Angles), view != free));
            }

            last = view;
        }

        return frames;
    }

    private static double Distance((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        Math.Sqrt(((double)b.X - a.X) * (b.X - a.X) + ((double)b.Y - a.Y) * (b.Y - a.Y) + ((double)b.Z - a.Z) * (b.Z - a.Z));

    private static double Turned((float Pitch, float Yaw, float Roll) a, (float Pitch, float Yaw, float Roll) b)
    {
        double pitch = Wrap(b.Pitch - a.Pitch);
        double yaw = Wrap(b.Yaw - a.Yaw);

        return Math.Sqrt((pitch * pitch) + (yaw * yaw));
    }

    private static double Wrap(double degrees)
    {
        degrees %= 360d;

        if (degrees > 180d)
        {
            return degrees - 360d;
        }

        return degrees < -180d ? degrees + 360d : degrees;
    }

    private static void Summarise(TextWriter output, string what, List<Frame> frames, Func<Frame, double> speedOf, int[] packets)
    {
        double[] speed = [.. frames.Select(speedOf)];
        double[] median = LocalMedians(speed);
        double mean = speed.Average();
        double sd = Math.Sqrt(speed.Average(value => (value - mean) * (value - mean)));

        int[] moving = [.. Enumerable.Range(0, speed.Length).Where(at => median[at] > Moving)];
        double[] ratio = [.. moving.Select(at => speed[at] / median[at])];
        double ratioMean = ratio.Length > 0 ? ratio.Average() : 0d;
        double ratioSd = ratio.Length > 0 ? Math.Sqrt(ratio.Average(value => (value - ratioMean) * (value - ratioMean))) : 0d;
        double[] deviation = [.. moving.Select(at => Math.Abs(speed[at] - median[at]))];
        Array.Sort(deviation);

        int outliers = moving.Count(at => Math.Abs(speed[at] - median[at]) > 0.5 * median[at]);
        int stalls = Enumerable.Range(1, Math.Max(0, speed.Length - 2))
            .Count(at => speed[at] <= 0d && speed[at - 1] > Moving && speed[at + 1] > Moving);

        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{what}: per-frame mean {mean:0.0000} sd {sd:0.0000} CV {(mean > 0 ? sd / mean : 0):0.0000}; moving frames {moving.Length}, " +
            $"speed/local-median CV {(ratioMean > 0 ? ratioSd / ratioMean : 0):0.0000}, |speed-median| p50 {Percentile(deviation, 0.5):0.0000} " +
            $"p99 {Percentile(deviation, 0.99):0.0000} max {(deviation.Length > 0 ? deviation[^1] : 0):0.0000}; >50% off median {outliers}, stalls {stalls}"));

        ByClass(output, frames, speed, median, moving, packets);
        AroundGaps(output, frames, speed, median, moving, packets);
        Worst(output, frames, speed, median, moving, packets);
    }

    /// <summary>
    /// Each moving frame, classed by the packet stamps around its whole playback tick: the step to the next distinct
    /// stamp, and how many packets share the stamp at or before it. A classification for the report only — the view came
    /// from production.
    /// </summary>
    private static (int Step, int Shared) ClassOf(double position, int[] packets)
    {
        int tick = (int)Math.Floor(position);
        int after = Array.FindIndex(packets, stamp => stamp > tick);

        if (after <= 0)
        {
            return (0, 0);
        }

        int before = packets[after - 1];

        return (packets[after] - before, packets.Count(stamp => stamp == before));
    }

    private static void ByClass(TextWriter output, List<Frame> frames, double[] speed, double[] median, int[] moving, int[] packets)
    {
        IEnumerable<string> rows = moving
            .GroupBy(at => ClassOf(frames[at].Position, packets))
            .OrderBy(group => group.Key.Step).ThenBy(group => group.Key.Shared)
            .Select(group => string.Create(CultureInfo.InvariantCulture,
                $"step {group.Key.Step}/shared {group.Key.Shared}: n {group.Count()} speed/median {group.Average(at => speed[at] / median[at]):0.000} " +
                $">50% {group.Count(at => Math.Abs(speed[at] - median[at]) > 0.5 * median[at])}"));

        output.WriteLine("  by packet class: " + string.Join("; ", rows));
    }

    private static void AroundGaps(TextWriter output, List<Frame> frames, double[] speed, double[] median, int[] moving, int[] packets)
    {
        for (int at = 1; at < packets.Length; at++)
        {
            if (packets[at] - packets[at - 1] < 4)
            {
                continue;
            }

            int from = packets[at - 1] - 3;
            int to = packets[at] + 3;
            int[] inside = [.. moving.Where(frame => frames[frame].Position >= from && frames[frame].Position < to)];
            double worst = inside.Length > 0 ? inside.Max(frame => Math.Abs(speed[frame] / median[frame] - 1d)) : 0d;

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  gap {packets[at - 1]}->{packets[at]} ({packets[at] - packets[at - 1]}): moving frames {inside.Length}, " +
                $"worst |speed/median-1| {worst:0.000}, >50% {inside.Count(frame => Math.Abs(speed[frame] - median[frame]) > 0.5 * median[frame])}, " +
                $"stalls {inside.Count(frame => speed[frame] <= 0d)}"));
        }
    }

    private static void Worst(TextWriter output, List<Frame> frames, double[] speed, double[] median, int[] moving, int[] packets)
    {
        foreach (int at in moving.OrderByDescending(frame => Math.Abs(speed[frame] / median[frame] - 1d)).Take(10))
        {
            (int step, int shared) = ClassOf(frames[at].Position, packets);

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  worst: position {frames[at].Position:0.000} speed {speed[at]:0.0000} median {median[at]:0.0000} " +
                $"ratio {speed[at] / median[at]:0.000} step {step} shared {shared}{(frames[at].Snapped ? " SNAP" : string.Empty)}"));
        }
    }

    private static double[] LocalMedians(double[] speed)
    {
        double[] medians = new double[speed.Length];

        for (int at = 0; at < speed.Length; at++)
        {
            int from = Math.Max(0, at - Half);
            int to = Math.Min(speed.Length, at + Half + 1);
            double[] window = speed[from..to];
            Array.Sort(window);
            medians[at] = window[window.Length / 2];
        }

        return medians;
    }

    private static double Percentile(double[] sorted, double fraction) =>
        sorted.Length == 0 ? 0d : sorted[Math.Min(sorted.Length - 1, (int)(fraction * sorted.Length))];
}
