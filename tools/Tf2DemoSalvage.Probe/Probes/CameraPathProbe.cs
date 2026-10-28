using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Microsoft.Extensions.Logging.Abstractions;

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
/// what this demo's arrival-stamp jitter alone does to a perfectly smooth motion; it carries no <c>net_Tick</c>, so
/// <c>demo_retimeview</c> 0 and 1 must read alike on it;
/// (b3) the subject's stamps carrying an even <c>net_Tick</c>, each view the line at its server tick — the snapshot
/// shape — run with <c>demo_retimeview</c> 0 and 1: off must read b2's jitter, on the line's own pace.
/// Then the subject with the setting off and on, the server's <c>net_Tick</c> beside each stamp, and where the
/// setting placed each packet (D238). `docs/measurements/2026-10-27-camera-path-pacing.md`,
/// `docs/measurements/2026-10-27-retime-view-by-server-tick.md`.
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

        Report(output, "control (a) line, a packet every tick", Line([.. Enumerable.Range(0, 2400)], at => at * (double)SyntheticViews.Interval), hz, true);

        // The subject's packet rate with no jitter: each packet stamped with the tick its own even time falls in.
        int[] evenTicks = [.. Enumerable.Range(0, count).Select(at => (int)Math.Floor(at * spacing / SyntheticViews.Interval))];
        Report(output, "control (b1) line, this demo's packet rate, stamped floor(even time / interval)",
            Line(evenTicks, at => at * spacing), hz, true);

        // The subject's own stamps, each packet at its even true time: arrival jitter alone. No net_Tick, so
        // demo_retimeview has nothing to place and the two settings must read alike — the fallback's control.
        int[] ownStamps = [.. stamps.Select(tick => tick - first)];
        DemoTimeline jitterOnly = Line(ownStamps, at => at * spacing);
        Report(output, "control (b2) line, this demo's own stamps at even true times, demo_retimeview 0", jitterOnly, hz, false);
        Report(output, "control (b2) line, this demo's own stamps at even true times, demo_retimeview 1", jitterOnly, hz, true);

        // The subject's own two clocks — its stamps and its net_Ticks — with each view the line AT its server tick: a
        // snapshot, as the measured views are. Off reads the jitter (b2); on must collapse to the line's own pace, so
        // whatever the subject keeps on top of this is its motion, not its timing. A packet without a net_Tick takes
        // the one before it plus one for its view and carries none, as the subject's do.
        int?[] servers = ViewServerTicks(subject);
        int[] serverTimes = new int[servers.Length];

        for (int at = 0; at < servers.Length; at++)
        {
            serverTimes[at] = servers[at] ?? (at > 0 ? serverTimes[at - 1] + 1 : 0);
        }

        DemoTimeline snapshots = Line(
            ownStamps, at => (serverTimes[at] - serverTimes[0]) * (double)SyntheticViews.Interval, at => servers[at]);
        Report(output, "control (b3) line, this demo's stamps and net_Ticks, views at the server tick, demo_retimeview 0", snapshots, hz, false);
        Report(output, "control (b3) line, this demo's stamps and net_Ticks, views at the server tick, demo_retimeview 1", snapshots, hz, true);

        string name = Path.GetFileName(path);
        Report(output, string.Create(CultureInfo.InvariantCulture, $"subject {name}, demo_retimeview 0"), subject, hz, false);
        Report(output, string.Create(CultureInfo.InvariantCulture, $"subject {name}, demo_retimeview 1"), subject, hz, true);
        ServerTicks(output, bytes, [.. subject.ViewCommands.Where(command => command.Type == DemoCommandType.Packet).Select(command => command.View)]);
        Placement(output, subject);
        WorldPacing(output, subject, hz, stamps[0], stamps[^1]);

        (int Placed, int Same, int ChangedNear, int ChangedFar) identity = RetimeIdentity(subject, hz);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"camera on vs off: {identity.Placed} packets placed; frames with none near: {identity.Same} identical, " +
            $"{identity.ChangedFar} differing; frames near one: {identity.ChangedNear} differing"));
    }

    /// <summary>
    /// The world's pacing beside the camera's: every player's origin from <see cref="DemoTimeline.PlayerAt(double, int)"/>, the
    /// per-entity sampler the moment source reads for a player's networked track, per frame, with the same
    /// speed-over-local-median measure. The recorder is read on his networked track (not viewed), so none of this
    /// depends on <c>demo_retimeview</c>; it answers whether the world was ever on the stamp's jitter.
    /// </summary>
    /// <remarks>At most <see cref="WorldFrames"/> frames from the first packet: the question is the clock, not the whole match.</remarks>
    private static void WorldPacing(TextWriter output, DemoTimeline timeline, int hz, int firstTick, int lastTick)
    {
        int middle = (firstTick + lastTick) / 2;
        int[] entities = [.. timeline.PlayersAt(middle).Where(player => player.Team is 2 or 3).Select(player => player.EntityIndex)];
        int[] packets = ViewPackets(timeline);
        DemoPlayer off = new(timeline) { ConVars = () => DemoViewConVars.Defaults with { RetimeView = false } };
        DemoPlayer on = new(timeline) { ConVars = () => DemoViewConVars.Defaults with { RetimeView = true } };

        // Every series through the SAME loop, frames and classing. The two cameras are the positive control: off must
        // show the stamp's pattern here too, or this instrument cannot see it.
        List<(string Name, Func<double, (double X, double Y, double Z)?> Sample)> series =
        [
            (OffCamera, tick => Point(off.InterpolateViewpoint(tick))),
            (OnCamera, tick => Point(on.InterpolateViewpoint(tick))),
        ];

        static (double X, double Y, double Z)? Point(RecordedView? view) =>
            view is { } seen ? ((double)seen.Origin.X, (double)seen.Origin.Y, (double)seen.Origin.Z) : null;

        foreach (int entity in entities)
        {
            series.Add((
                string.Create(CultureInfo.InvariantCulture,
                    $"entity {entity}{(entity == timeline.RecorderEntityIndex ? Recorder : string.Empty)}"),
                tick => timeline.PlayerAt(tick, entity) is { Drawn: true, LifeState: null or 0 } player
                    ? ((double)player.X, (double)player.Y, (double)player.Z)
                    : null));
        }

        // A frame faster than demo_interplimit is a teleport, as the engine judges one. It ends a segment, as a death or
        // an undrawn frame does, so no local median mixes two lives.
        double teleport = DemoViewConVars.Defaults.InterpLimit / hz;
        List<List<(double Tick, double Speed)>>[] segments = [.. series.Select(_ => new List<List<(double Tick, double Speed)>>())];
        (double X, double Y, double Z)?[] last = new (double X, double Y, double Z)?[series.Count];
        double step = 1d / hz / timeline.IntervalPerTick;
        int frames = 0;

        for (double tick = firstTick + 1; tick < lastTick - 9 && frames < WorldFrames; tick += step, frames++)
        {
            for (int at = 0; at < series.Count; at++)
            {
                (double X, double Y, double Z)? now = series[at].Sample(tick);
                double? speed = now is { } a && last[at] is { } b
                    ? Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)))
                    : null;

                if (speed is { } moved && moved <= teleport)
                {
                    segments[at][^1].Add((tick, moved));
                }
                else if (now is not null)
                {
                    segments[at].Add([]);
                }

                last[at] = now;
            }
        }

        output.WriteLine();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"== the world's pacing, {entities.Length} players on a team at tick {middle}, {frames} frames at {hz} Hz; " +
            $"a dead or undrawn frame, or one over {teleport:0.0} units, splits a segment"));

        List<((int Step, int Shared) Class, double Ratio)>[] classed = new List<((int Step, int Shared) Class, double Ratio)>[series.Count];

        for (int at = 0; at < series.Count; at++)
        {
            classed[at] = [];

            foreach (List<(double Tick, double Speed)> part in segments[at])
            {
                double[] speed = [.. part.Select(sample => sample.Speed)];
                double[] median = LocalMedians(speed);

                classed[at].AddRange(Enumerable.Range(0, speed.Length)
                    .Where(frame => median[frame] > Moving)
                    .Select(frame => (ClassOf(part[frame].Tick, packets), speed[frame] / median[frame])));
            }

            double[] values = [.. classed[at].Select(sample => sample.Ratio).Order()];
            double mean = values.Length > 0 ? values.Average() : 0d;
            double sd = values.Length > 0 ? Math.Sqrt(values.Average(value => (value - mean) * (value - mean))) : 0d;

            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {series[at].Name}: moving frames {values.Length}, speed/local-median mean {mean:0.000} median {Percentile(values, 0.5):0.000} " +
                $"CV {(mean > 0 ? sd / mean : 0):0.0000}, >50% off median {values.Count(value => Math.Abs(value - 1d) > 0.5)}"));
        }

        void Table(string title, IEnumerable<((int Step, int Shared) Class, double Ratio)> samples) =>
            output.WriteLine($"  {title} by packet class: " + string.Join("; ", samples
                .Where(sample => sample.Class.Step is >= 1 and <= 3 && sample.Class.Shared is 1 or 2)
                .GroupBy(sample => sample.Class).OrderBy(group => group.Key.Step).ThenBy(group => group.Key.Shared)
                .Select(group =>
                {
                    double[] sorted = [.. group.Select(sample => sample.Ratio).Order()];

                    return string.Create(CultureInfo.InvariantCulture,
                        $"step {group.Key.Step}/shared {group.Key.Shared}: n {sorted.Length} mean {sorted.Average():0.000} median {Percentile(sorted, 0.5):0.000}");
                })));

        Table(OffCamera, classed[0]);
        Table(OnCamera, classed[1]);
        Table("other players", Enumerable.Range(2, series.Count - 2)
            .Where(at => !series[at].Name.EndsWith(Recorder, StringComparison.Ordinal))
            .SelectMany(at => classed[at]));
    }

    /// <summary>The most frames <see cref="WorldPacing"/> samples.</summary>
    private const int WorldFrames = 20000;

    private const string OffCamera = "camera, demo_retimeview 0";
    private const string OnCamera = "camera, demo_retimeview 1";
    private const string Recorder = " (recorder, networked)";

    /// <summary>
    /// The production camera (<see cref="SpectatorView.Eye"/> over <see cref="TimelineEyes"/>) with <c>demo_retimeview</c>
    /// on and off, every frame at <paramref name="hz"/>: which differ, and whether a packet the setting placed is near.
    /// </summary>
    /// <param name="timeline">A POV demo.</param>
    /// <param name="hz">Frames a second.</param>
    /// <returns>
    /// Packets the setting placed off their stamp; frames with none near, whose two cameras agreed; frames near one whose
    /// cameras differ; and frames with none near whose cameras differ — zero, or the setting leaks.
    /// </returns>
    /// <remarks>
    /// "Near" is from a placed packet's predecessor's earlier tick to its successor's later one, on either axis, two
    /// ticks either side: every pair the packet can be an end of. Held by <c>CorpusRetimeViewTests</c>.
    /// </remarks>
    public static (int Placed, int Same, int ChangedNear, int ChangedFar) RetimeIdentity(DemoTimeline timeline, int hz)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        const int reach = 2;
        DemoPlayer on = new(timeline) { ConVars = () => DemoViewConVars.Defaults with { RetimeView = true } };
        DemoPlayer off = new(timeline) { ConVars = () => DemoViewConVars.Defaults with { RetimeView = false } };
        IReadOnlyList<int> placed = on.ServerTimedTicks;
        IReadOnlyList<DemoViewCommand> commands = timeline.ViewCommands;
        int[] packets = [.. Enumerable.Range(0, commands.Count).Where(at => commands[at].Type == DemoCommandType.Packet && !commands[at].View.IsDefault)];
        List<(int From, int To)> near = [.. Enumerable.Range(1, Math.Max(0, packets.Length - 2))
            .Where(at => placed[packets[at]] != commands[packets[at]].Tick)
            .Select(at => (
                Math.Min(placed[packets[at - 1]], commands[packets[at - 1]].Tick) - reach,
                Math.Max(placed[packets[at + 1]], commands[packets[at + 1]].Tick) + reach))];

        SpectatorView retimed = new(NullLogger.Instance) { Eyes = new TimelineEyes(timeline, on) };
        SpectatorView engine = new(NullLogger.Instance) { Eyes = new TimelineEyes(timeline, off) };
        int same = 0;
        int changedNear = 0;
        int changedFar = 0;
        int last = commands[packets[^1]].Tick - 9;

        for (double tick = commands[packets[0]].Tick + 1; tick < last; tick += 1d / hz / timeline.IntervalPerTick)
        {
            bool differs = retimed.Eye(tick, 1f)?.Origin != engine.Eye(tick, 1f)?.Origin;
            int target = (int)Math.Floor(tick);

            if (near.Exists(span => span.From <= target && target <= span.To))
            {
                changedNear += differs ? 1 : 0;
            }
            else if (differs)
            {
                changedFar++;
            }
            else
            {
                same++;
            }
        }

        return (near.Count, same, changedNear, changedFar);
    }

    /// <summary>
    /// Where <c>demo_retimeview</c> put each packet, read from the array the blend itself reads
    /// (<see cref="DemoPlayer.ServerTimedTicks"/>, B243): stamp minus placement, over the whole demo and by thirds — a
    /// drift between the clocks shows as the thirds disagreeing.
    /// </summary>
    private static void Placement(TextWriter output, DemoTimeline timeline)
    {
        IReadOnlyList<int> placed = new DemoPlayer(timeline).ServerTimedTicks;
        int[] packets = [.. Enumerable.Range(0, timeline.ViewCommands.Count).Where(at => timeline.ViewCommands[at].Type == DemoCommandType.Packet)];
        int[] shift = [.. packets.Select(at => timeline.ViewCommands[at].Tick - placed[at])];
        int third = Math.Max(1, shift.Length / 3);

        output.WriteLine();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"== demo_retimeview placement, {packets.Length} packets, {shift.Count(value => value != 0)} moved off their stamp"));
        output.WriteLine("stamp - placed: " + Counts(shift));

        for (int part = 0; part < 3; part++)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  third {part + 1}: ") +
                Counts(shift.Skip(part * third).Take(part == 2 ? int.MaxValue : third)));
        }

        // Around each stamp gap of 8 or more, and each packet moved by 3 or more: stamp/server/placed, packet by packet.
        foreach (int at in Enumerable.Range(1, Math.Max(0, packets.Length - 1))
            .Where(at => timeline.ViewCommands[packets[at]].Tick - timeline.ViewCommands[packets[at - 1]].Tick >= 8 || Math.Abs(shift[at]) >= 3)
            .Take(12))
        {
            output.WriteLine("  around packet " + at.ToString(CultureInfo.InvariantCulture) + ": " + string.Join(' ',
                Enumerable.Range(Math.Max(0, at - 4), Math.Min(packets.Length, at + 5) - Math.Max(0, at - 4)).Select(near =>
                    string.Create(CultureInfo.InvariantCulture,
                        $"{timeline.ViewCommands[packets[near]].Tick}/{timeline.ViewCommands[packets[near]].ServerTick}/{placed[packets[near]]}"))));
        }
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

        int[] delays = [.. samples.Where(sample => sample.Server is not null).Select(sample => sample.Stamp - sample.Server.GetValueOrDefault())];
        int third = Math.Max(1, delays.Length / 3);
        output.WriteLine("stamp - server tick: " + Counts(delays));

        for (int part = 0; part < 3; part++)
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  third {part + 1}: ") +
                Counts(delays.Skip(part * third).Take(part == 2 ? int.MaxValue : third)));
        }

        // The stamp gaps of 4 or more, by what the server did across them: a server step of 1 is a gap only the stamp has.
        output.WriteLine("stamp steps of 4+, by server step: " + Counts(Enumerable.Range(1, samples.Length - 1)
            .Where(at => samples[at].Stamp - samples[at - 1].Stamp >= 4 && samples[at].Server is not null && samples[at - 1].Server is not null)
            .Select(at => samples[at].Server.GetValueOrDefault() - samples[at - 1].Server.GetValueOrDefault())));
    }

    /// <summary>
    /// Each packet's recorded origin step over the median of the eleven steps around it, grouped by the stamp's step and
    /// by the server tick's: ~0 at stamp 0 and ~2 at stamp 2 means the views were sampled on the stamp's clock; ~1 across
    /// stamp steps, tracking the server step, means the stamps are the uneven part.
    /// </summary>
    private static void ViewSteps(TextWriter output, Sample[] samples)
    {
        ComponentSteps(output, samples, "recorded origin", 0.5, (a, b) => Distance(a.Origin, b.Origin));
        ComponentSteps(output, samples, "recorded view angles", 0.05, (a, b) => Turned(a.Angles, b.Angles));
        ComponentSteps(output, samples, "recorded local angles", 0.05, (a, b) => Turned(a.LocalAngles, b.LocalAngles));
    }

    /// <summary>One component's step per packet over its local median, by stamp step and by server step.</summary>
    private static void ComponentSteps(
        TextWriter output, Sample[] samples, string what, double moves, Func<RecordedView, RecordedView, double> measure)
    {
        double[] step = [.. Enumerable.Range(1, samples.Length - 1).Select(at => measure(samples[at - 1].View, samples[at].View))];
        double[] median = LocalMedians(step);
        int[] moving = [.. Enumerable.Range(0, step.Length).Where(at => median[at] > moves)];

        string Grouped(Func<int, int?> key) => string.Join("; ", moving
            .Where(at => key(at) is not null)
            .GroupBy(at => Math.Min(key(at).GetValueOrDefault(), 3))
            .OrderBy(group => group.Key)
            .Select(group => string.Create(CultureInfo.InvariantCulture,
                $"{group.Key}: n {group.Count()} step/median mean {group.Average(at => step[at] / median[at]):0.000}")));

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{what} step / local median, {moving.Length} moving packet steps"));
        output.WriteLine("  by stamp step: " + Grouped(at => samples[at + 1].Stamp - samples[at].Stamp));
        output.WriteLine("  by server step: " + Grouped(at =>
            samples[at + 1].Server is { } next && samples[at].Server is { } previous ? next - previous : null));

        // A packet whose value did not change while its neighbours' did: in the recording itself, before any blend.
        int[] still = [.. moving.Where(at => step[at] <= 0d)];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  unchanged from the packet before, among moving: {still.Length}") +
            (still.Length > 0 ? ", by server step " + Counts(still.Select(at =>
                samples[at + 1].Server is { } next && samples[at].Server is { } previous ? next - previous : -1)) +
            ", first stamps " + string.Join(' ', still.Take(40).Select(at => samples[at + 1].Stamp.ToString(CultureInfo.InvariantCulture))) : string.Empty));
    }

    private static string Counts(IEnumerable<int> values) => string.Join(' ',
        values.GroupBy(value => value).OrderBy(group => group.Key)
            .Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key}x{group.Count()}")));

    /// <summary>
    /// A synthetic POV demo: packets at <paramref name="ticks"/> (offset to <see cref="LineStart"/>), packet k at the
    /// line's position at <paramref name="time"/>(k), carrying <c>net_Tick</c> <paramref name="server"/>(k) when given.
    /// </summary>
    private static DemoTimeline Line(int[] ticks, Func<int, double> time, Func<int, int?>? server = null)
    {
        List<DemoCommand> commands = [];

        for (int at = 0; at < ticks.Length; at++)
        {
            float t = (float)time(at);
            commands.Add(SyntheticViews.Packet(
                LineStart + ticks[at], (LineSpeed * t, 0f, 0f), (0f, (LineTurn * t) % 360f, 0f), serverTick: server?.Invoke(at)));
        }

        commands.Add(SyntheticViews.Stop(LineStart + ticks[^1]));

        return DemoTimeline.Build(SyntheticViews.Demo(24, commands));
    }

    /// <summary>The ticks of the <c>dem_packet</c>s carrying a view; a synthetic demo's <c>svc_ServerInfo</c> packet at tick 0 carries the default one.</summary>
    private static int[] ViewPackets(DemoTimeline timeline) =>
        [.. timeline.ViewCommands.Where(command => command.Type == DemoCommandType.Packet && !command.View.IsDefault).Select(command => command.Tick)];

    /// <summary>The <c>net_Tick</c> the timeline kept for each of <see cref="ViewPackets"/>, null where a packet carried none.</summary>
    private static int?[] ViewServerTicks(DemoTimeline timeline) =>
        [.. timeline.ViewCommands.Where(command => command.Type == DemoCommandType.Packet && !command.View.IsDefault).Select(command => command.ServerTick)];

    private static void Report(TextWriter output, string title, DemoTimeline timeline, int hz, bool retime)
    {
        output.WriteLine();
        output.WriteLine("== " + title);

        int[] packets = ViewPackets(timeline);
        Histogram(output, packets, timeline.IntervalPerTick);

        // ParseAheadForInterval answers false within 8 ticks of the dem_stop, so the tail plays stepped by design: judged
        // frames are those whose whole tick is past the first packet and 9 or more short of the last.
        List<Frame> played = Play(timeline, hz, packets[0], packets[^1], retime);
        List<Frame> frames = [.. played.Where(frame => Math.Floor(frame.Position) >= packets[0] + 1 && Math.Floor(frame.Position) <= packets[^1] - 9)];
        output.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"frames {frames.Count} judged ({played.Count - frames.Count} at the ends excluded) at {hz} Hz, demo_interplimit/avellimit snaps {frames.Count(frame => frame.Snapped)}" +
            $" at {string.Join(' ', frames.Where(frame => frame.Snapped).Take(30).Select(frame => frame.Position.ToString("0.00", CultureInfo.InvariantCulture)))}"));
        output.WriteLine("  stalls at " + string.Join(' ', Enumerable.Range(1, Math.Max(0, frames.Count - 2))
            .Where(at => frames[at].Move <= 0d && frames[at - 1].Move > Moving && frames[at + 1].Move > Moving)
            .Take(30).Select(at => frames[at].Position.ToString("0.00", CultureInfo.InvariantCulture))));

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
    /// <remarks>
    /// **Both players take the same <c>demo_retimeview</c>**: the unlimited one differs from the viewer's only in the two
    /// limits, so a difference between them is a snap and never the setting.
    /// </remarks>
    private static List<Frame> Play(DemoTimeline timeline, int hz, int firstTick, int lastTick, bool retime)
    {
        DemoViewConVars setting = DemoViewConVars.Defaults with { RetimeView = retime };
        TimelineEyes eyes = new(timeline, new DemoPlayer(timeline) { ConVars = () => setting });
        DemoPlayer unlimited = new(timeline)
        {
            ConVars = () => setting with { InterpLimit = float.PositiveInfinity, AvelLimit = float.PositiveInfinity },
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
