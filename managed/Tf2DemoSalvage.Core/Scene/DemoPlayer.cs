using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Container;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// <c>CDemoPlayer</c>'s view half: the packets read so far, <c>InterpolateViewpoint</c>, and the state it keeps (B56).
/// </summary>
/// <remarks>
/// **One per playing viewer, never on the timeline.** A timeline is shared and locked
/// (<c>docs/memory/a-cached-timeline-samples-for-everyone.md</c>); this is the engine's per-client state — the last
/// packet read, the parse-ahead list (+0x5d0), <c>m_bInterpolateView</c> (+0x63c), <c>m_bResetInterpolation</c>
/// (+0x63d) and the last interpolated tick (+0x554) — and it reads the timeline's stream the way <c>ReadPacket</c>
/// (<c>0x180072ee0</c>) reads the file. The playback clock is <see cref="PlaybackClock"/>, whose fractional position is
/// what this is handed.
///
/// **Not ported:** the three <c>OverrideView</c> hooks <c>InterpolateViewpoint</c> asks after it (<c>0x1800728ab</c>),
/// which answer false unless a demo tool is driving the camera; and timedemo (+0x63e), which never interpolates.
///
/// **Added above it: <c>demo_retimeview</c>** (D238, on by default), which pairs the recorded ORIGIN on the packets'
/// server ticks rather than their arrival stamps (<see cref="ServerTimed"/>). Reading, the parse-ahead, the rewind and
/// the angles stay on the stamp; <c>demo_retimeview 0</c> is the engine's answer bit for bit.
/// </remarks>
public sealed class DemoPlayer
{
    /// <summary><c>ParseAheadForInterval( tick, 8 )</c>, as ReadPacket calls it.</summary>
    private const int ParseAheadTicks = 8;

    /// <summary>How far back the parse-ahead keeps packets: it prunes those before <c>curtick - 0x20</c>.</summary>
    private const int KeptTicks = 32;

    /// <summary>
    /// What +0x554 holds after a <c>dem_synctick</c> or <c>StartPlayback</c> (<c>0x180073bd0</c>): <c>host_tickcount</c>.
    /// </summary>
    /// <remarks>
    /// **The engine's own clock, not a demo tick** — compared against demo ticks it lies past every packet near the
    /// start of playback, so the next interpolation finds no <c>FDEMO_NOINTERP</c> window. Taken as past every tick
    /// here. *Interpolated:* a client whose <c>host_tickcount</c> were smaller than the demo's ticks would see a window.
    /// </remarks>
    private const int HostTickCount = int.MaxValue;

    /// <summary>How many PACKETS either side <see cref="ServerTimed"/> looks for a packet's usual arrival delay.</summary>
    /// <remarks>
    /// A count, not a time: about 5 s at the 65 packets a second the measured POV demos arrive at, and 16 s at
    /// <c>cl_updaterate 20</c>. Long enough that the delay most packets arrive with is the most common one in it (71% of
    /// the subject's, finding 106), short enough to follow a drift between the two machines' clocks over a long match.
    /// </remarks>
    private const int ServerTimedWindowPackets = 333;

    private readonly IReadOnlyList<DemoViewCommand> _commands;

    /// <summary>Each command's tick, the stamp: the axis the engine pairs every view on.</summary>
    private readonly int[] _stamped;

    /// <summary>
    /// Each command's tick on the server's clock, the axis <c>demo_retimeview</c> pairs the recorded origin on — the
    /// stamp wherever <see cref="ServerTimed"/> cannot place it (D238).
    /// </summary>
    private readonly int[] _serverTimed;

    /// <summary>For each command, the latest <c>dem_packet</c> tick at or before it in stream order: non-decreasing, so searchable.</summary>
    private readonly int[] _latestPacketTick;

    private readonly float _interval;
    private readonly int _maxClients;
    private readonly float _interpAmount;

    /// <summary>The parse-ahead list (+0x5d0): indices into the stream, in stream order.</summary>
    private readonly List<int> _packets = [];

    /// <summary>The parse-ahead list itself, for a test that a rewind lands in a fresh read's state.</summary>
    internal IReadOnlyList<int> QueuedPackets => _packets;

    /// <summary>The ticks <c>demo_retimeview</c> pairs the origin on, one per view command — the array the blend reads, for a test and the probe.</summary>
    internal IReadOnlyList<int> ServerTimedTicks => _serverTimed;

    /// <summary>The view commands this player reads and the window it places them with, for the probe to re-run <see cref="ServerTimed"/>'s counter.</summary>
    internal (IReadOnlyList<DemoViewCommand> Commands, int Window) ServerTimedInputs => (_commands, ServerTimedWindowPackets);

    /// <summary>The last command read, or -1.</summary>
    private int _read = -1;

    /// <summary>The last packet read — where the current <c>democmdinfo_t</c> (+0x5f0) came from — or -1.</summary>
    private int _current = -1;

    /// <summary><c>m_bInterpolateView</c> (+0x63c), from the last packet's parse-ahead.</summary>
    private bool _interpolateView;

    /// <summary><c>m_bInterpolateView</c>, for a test that a rewind lands in a fresh read's state.</summary>
    internal bool InterpolatesView => _interpolateView;

    /// <summary>Whether a rewind may start near its target rather than at the file's start — see <see cref="Simple"/>.</summary>
    internal bool IsSimple => NotSimpleBecause is null;

    /// <summary>Why the file is not <see cref="IsSimple"/>, naming the first command that breaks the shape; null when it is.</summary>
    internal string? NotSimpleBecause { get; }

    /// <summary>The first command of the trailing run of packets <see cref="Simple"/> found (the body).</summary>
    private readonly int _bodyStart;

    /// <summary><c>m_bResetInterpolation</c> (+0x63d).</summary>
    private bool _resetInterpolation;

    /// <summary>+0x554: the target of the last interpolation.</summary>
    private int _lastInterpolatedTick = HostTickCount;

    /// <summary>The moment last asked about, and its answer — the engine interpolates once a rendered frame.</summary>
    private double _askedTick = double.NaN;
    private RecordedView? _answer;
    private DemoViewConVars _askedConVars;

    /// <summary>A demo player over a decoded demo.</summary>
    /// <param name="timeline">The demo.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> is null.</exception>
    public DemoPlayer(DemoTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        _commands = timeline.ViewCommands;
        _latestPacketTick = new int[_commands.Count];

        int latest = int.MinValue;

        for (int index = 0; index < _commands.Count; index++)
        {
            // Only a dem_packet waits for its tick; a signon is read when reached, and real ones carry the server's tick.
            latest = _commands[index].Type == DemoCommandType.Packet ? Math.Max(latest, _commands[index].Tick) : latest;
            _latestPacketTick[index] = latest;
        }

        NotSimpleBecause = Simple(_commands, out _bodyStart);
        _interval = timeline.IntervalPerTick > 0f ? timeline.IntervalPerTick : PlaybackClock.DefaultIntervalPerTick;
        _maxClients = timeline.MaxClients;
        _interpAmount = (float)timeline.ClientInterpAmount;

        _stamped = new int[_commands.Count];

        for (int index = 0; index < _commands.Count; index++)
        {
            _stamped[index] = _commands[index].Tick;
        }

        _serverTimed = ServerTimed(_commands, ServerTimedWindowPackets);
    }

    /// <summary>Each command's tick on the server's clock: what <c>demo_retimeview</c> interpolates the origin by (D238).</summary>
    /// <param name="commands">The view commands, in stream order.</param>
    /// <param name="window">How many packets either side the usual arrival delay is taken over.</param>
    /// <param name="modeChanges">
    /// If given, receives each command index at which a run's usual delay changes, with the old and new delay: an instrument.
    /// </param>
    /// <returns>
    /// One tick per command. One that cannot be placed starts at its stamp, and like every packet is then raised to the
    /// latest earlier packet's tick when that is later — so a fallback can sit above its own stamp, never before an
    /// earlier packet.
    /// </returns>
    /// <remarks>
    /// **Why there is a second clock at all** (finding 106). A multiplayer recording stamps each <c>dem_packet</c> with
    /// <c>CDemoRecorder::GetRecordingTick</c> (<c>0x180071f80</c>): <c>TIME_TO_TICKS( net_time ) - m_nStartTick</c>, the
    /// client's wall clock when the packet was written, so network jitter puts two in one tick and none in the next. The
    /// <c>net_Tick</c> inside is the server's own, and steps by one in 2315 of 2362 pairs on the subject.
    ///
    /// **The placement: server tick plus the usual delay.** A run is consecutive <c>dem_packet</c>s that each carry a
    /// <c>net_Tick</c>, the server's stepping up; a signon, synctick, stop, a packet without one, a server tick that
    /// steps back, or a delay that steps by more than <see cref="ParseAheadTicks"/> ends it. That last is a clock step,
    /// not jitter — the 2013 specimen's delay steps six times, by 12, 13 and once 28 ticks (finding 106) — and each side
    /// of it has its own usual delay. Within a run each packet's delay is <c>stamp - server</c>, and its tick is its server tick plus
    /// the most common delay among the <paramref name="window"/> packets either side (the smaller on a tie), the one in
    /// force kept until another leads it by a tenth of the window. Where the
    /// stamps are regular every delay is that one and the tick IS the stamp, bit for bit; a packet that arrived late by a
    /// tick moves back by one.
    ///
    /// **Two guards, and neither invents a time.** A placement more than <see cref="ParseAheadTicks"/> from the stamp is
    /// not arrival jitter (a stall, a server tick that jumped) and starts from its stamp — the parse-ahead window is all
    /// the reader would queue past it. And no tick is ever placed before an earlier packet's while the stamps do not step
    /// back: it is raised to it, so a pair never reorders — which can lift a fallback, or a packet with no
    /// <c>net_Tick</c>, above its own stamp when the packet before it was placed after its own. Where the stamps
    /// do step back the engine's own queue clears, and so does this guard.
    /// </remarks>
    internal static int[] ServerTimed(
        IReadOnlyList<DemoViewCommand> commands, int window, List<(int Index, long From, long To)>? modeChanges = null)
    {
        int[] ticks = new int[commands.Count];

        for (int index = 0; index < commands.Count; index++)
        {
            ticks[index] = commands[index].Tick;
        }

        for (int start = 0; start < commands.Count;)
        {
            if (!Placeable(commands[start]))
            {
                start++;
                continue;
            }

            int end = start + 1;

            while (end < commands.Count && Placeable(commands[end]) &&
                   commands[end].ServerTick.GetValueOrDefault() > commands[end - 1].ServerTick.GetValueOrDefault() &&
                   Math.Abs(Delay(commands[end]) - Delay(commands[end - 1])) <= ParseAheadTicks)
            {
                end++;
            }

            Place(commands, start, end, window, ticks, modeChanges);
            start = end;
        }

        // Never reordered: raised to the latest earlier tick, until the stamps themselves step back.
        int latest = int.MinValue;
        int lastStamp = int.MinValue;

        for (int index = 0; index < commands.Count; index++)
        {
            if (!commands[index].IsPacket)
            {
                continue;
            }

            latest = commands[index].Tick < lastStamp ? int.MinValue : latest;
            ticks[index] = Math.Max(ticks[index], latest);
            latest = ticks[index];
            lastStamp = commands[index].Tick;
        }

        return ticks;
    }

    private static bool Placeable(DemoViewCommand command) =>
        command.Type == DemoCommandType.Packet && command.ServerTick is not null;

    /// <summary>One run's placement: each server tick plus the commonest delay in the window around it.</summary>
    private static void Place(
        IReadOnlyList<DemoViewCommand> commands, int start, int end, int window, int[] ticks,
        List<(int Index, long From, long To)>? modeChanges)
    {
        // Delays in long: a server tick is 32 bits off the wire, and the difference of two need not fit an int.
        Dictionary<long, int> counts = [];
        int entered = start;
        int left = start;
        long previous = long.MinValue;

        for (int index = start; index < end; index++)
        {
            for (; entered < end && entered <= index + window; entered++)
            {
                long delay = Delay(commands[entered]);
                counts[delay] = counts.GetValueOrDefault(delay) + 1;
            }

            // A delay that left the window leaves the table, so it holds at most the window's distinct delays.
            for (; left < index - window; left++)
            {
                long delay = Delay(commands[left]);

                if (--counts[delay] == 0)
                {
                    counts.Remove(delay);
                }
            }

            long usual = long.MaxValue;
            int most = 0;

            foreach ((long delay, int count) in counts)
            {
                if (count > most || (count == most && delay < usual))
                {
                    usual = delay;
                    most = count;
                }
            }

            // **Hysteresis** (Opus review, round 4; measured). Two delays near a tie trade the lead as the window slides,
            // and each trade moves every later placement by a tick: rgl changed its usual delay 252 times, 231 of them
            // back again within a window, 92 in the worst window. The delay in force keeps the place until a challenger
            // leads it by a tenth of the window.
            if (counts.TryGetValue(previous, out int held) && held + (window / 10) >= most)
            {
                usual = previous;
            }

            long placed = commands[index].ServerTick.GetValueOrDefault() + usual;

            ticks[index] = Math.Abs(placed - commands[index].Tick) <= ParseAheadTicks ? (int)placed : commands[index].Tick;

            // An instrument (Opus review, round 4): where the usual delay changes within a run, every later placement
            // moves by the difference. Recorded at the index the new delay first applies, never recomputed elsewhere.
            if (index > start && usual != previous)
            {
                modeChanges?.Add((index, previous, usual));
            }

            previous = usual;
        }
    }

    private static long Delay(DemoViewCommand command) => (long)command.Tick - command.ServerTick.GetValueOrDefault();

    /// <summary>Whether a bounded rewind provably lands in a fresh read's state (batch-33 review 2): the conservative shape.</summary>
    /// <remarks>
    /// **The shape, and why each part is needed.** The commands this reader sees are signons and packets (both read, both
    /// parse ahead), and the synctick and stop that end a parse-ahead (<see cref="ParseAheadForInterval"/>). The file is
    /// simple when it is an opening, then a body:
    ///
    /// - **the body is every command after the opening, all <c>dem_packet</c>, ticks never stepping back**, with only
    ///   <c>dem_stop</c> after it — no mid-file signon, synctick or stop, whose parse-ahead differs;
    /// - **the opening ends in a synctick or stop**, so no opening parse-ahead reaches into the body.
    ///
    /// **What the opening leaves in the list** is then only opening commands, and the list's ticks never step back (a
    /// lower append clears it). The body's first append — the second body packet, <c>B1</c> — either clears them (one
    /// ticked past <c>B1</c>, as a modern match's 146,124 signons are) or finds them all at or below <c>B1</c>'s tick, at
    /// the list's front and already read. A bounded start <c>F</c> at or past <c>B1</c> has <c>B1 &lt;= F &lt; L - 40</c>
    /// for the last packet read <c>L</c>, so the prune at <c>L</c> removes every one of them, and the list after <c>L</c>
    /// holds only body packets newer than <c>L - 32</c>, which a start at <c>F</c> builds too (<see cref="Restart"/>). The
    /// era specimens open with signons ticked inside the packets' span (158 on the 2007 one, 6,041 on the 2013 one), which
    /// is the second case. Anything else rewinds from the file's start, the engine's own reload, correct by construction.
    /// </remarks>
    private static string? Simple(IReadOnlyList<DemoViewCommand> commands, out int body)
    {
        int end = commands.Count;

        while (end > 0 && commands[end - 1].Type == DemoCommandType.Stop)
        {
            end--;
        }

        body = end;

        while (body > 0 && commands[body - 1].Type == DemoCommandType.Packet)
        {
            body--;
        }

        // Two packets at least, or there is nothing to bound; and an opening, if any, closed by a marker.
        if (end - body < 2)
        {
            return Because($"the trailing run of packets is {end - body} long", body, commands);
        }

        if (body > 0 && commands[body - 1].Type is not (DemoCommandType.SyncTick or DemoCommandType.Stop))
        {
            return Because($"the trailing run of packets follows a {commands[body - 1].Type}", body - 1, commands);
        }

        for (int index = body + 1; index < end; index++)
        {
            if (commands[index].Tick < commands[index - 1].Tick)
            {
                return Because("a packet steps back in tick", index, commands);
            }
        }

        return null;
    }

    private static string Because(string reason, int index, IReadOnlyList<DemoViewCommand> commands) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"{reason}: command {index} of {commands.Count}{(index < commands.Count ? $", tick {commands[index].Tick}" : string.Empty)}");

    /// <summary>The watcher's <c>demo_*</c> ConVars, asked on every interpolation.</summary>
    /// <remarks>
    /// **Asked, not captured** (B450): <c>InterpolateViewpoint</c> (<c>0x180072180</c>) reads each ConVar per call,
    /// so a config change while the demo plays applies on the next frame.
    /// </remarks>
    public Func<DemoViewConVars> ConVars { get; init; } = () => DemoViewConVars.Defaults;

    /// <summary><c>ResetDemoInterpolation</c> (vtable +0xa0, <c>0x180073990</c>): the next interpolation snaps.</summary>
    /// <remarks>
    /// Reached in the engine only through <c>IVEngineClient::ResetDemoInterpolation</c> (<c>cdll_int.h:522</c>), from
    /// client code; neither <c>SkipToTick</c> nor <c>StartPlayback</c> sets it, so a seek here does not either.
    /// </remarks>
    public void ResetDemoInterpolation()
    {
        _resetInterpolation = true;
        _askedTick = double.NaN;
    }

    /// <summary><c>CDemoPlayer::InterpolateViewpoint</c> (<c>0x180072180</c>) at a moment of playback.</summary>
    /// <param name="tick">The playback position in demo ticks, the fraction included.</param>
    /// <returns>
    /// The view <c>IPrediction</c> hands the local player — <c>SetViewOrigin</c>, <c>SetViewAngles</c>,
    /// <c>SetLocalViewAngles</c> — or null while the current <c>democmdinfo_t</c> is still the default, when the engine
    /// sets nothing.
    /// </returns>
    /// <remarks>
    /// **Asked twice for one moment, it answers once.** The engine runs it once per rendered frame and the camera and
    /// the recorder's body both read what it set; here both ask, so a repeated moment returns the first answer rather
    /// than re-running with the last tick already advanced.
    /// </remarks>
    public RecordedView? InterpolateViewpoint(double tick)
    {
        // The same stored moment, bit for bit, under the same ConVars — a question about repetition, not about
        // nearness. The ConVars are part of the key so a change made while paused applies on the next call (B450).
        DemoViewConVars conVars = ConVars();

        if (BitConverter.DoubleToInt64Bits(tick) == BitConverter.DoubleToInt64Bits(_askedTick) && conVars.Equals(_askedConVars))
        {
            return _answer;
        }

        // GetPlaybackTick() and host_remainder: the whole tick and the part of the next one already run.
        int playbackTick = (int)Math.Floor(tick);
        float hostRemainder = (float)((tick - playbackTick) * _interval);

        ReadPackets(playbackTick);

        _askedTick = tick;
        _askedConVars = conVars;
        _answer = Interpolate(playbackTick, hostRemainder, conVars);

        return _answer;
    }

    /// <summary>Whether <see cref="InterpolateViewpoint"/> at this playback tick answers a view rather than null.</summary>
    /// <param name="playbackTick">The whole playback tick.</param>
    /// <returns>True when the last <c>dem_packet</c> read by it carries a non-default <c>democmdinfo_t</c>.</returns>
    /// <remarks>
    /// **Side-effect free**, for the seek scan (D233): it reads the command list and none of the reader's state. Both
    /// branches of <see cref="Interpolate"/> answer non-null exactly when the current packet's view is not the default,
    /// and <see cref="ReadPackets"/> leaves the current packet the last one before <see cref="FirstPacketPast"/> — the
    /// same index both routes ask, so they agree on a demo whose packet ticks run backwards (batch-31 review 2).
    /// </remarks>
    public bool ViewsAt(int playbackTick)
    {
        for (int index = FirstPacketPast(playbackTick) - 1; index >= 0; index--)
        {
            if (_commands[index].IsPacket)
            {
                return !_commands[index].View.IsDefault;
            }
        }

        return false;
    }

    /// <summary>The first <c>dem_packet</c>, in stream order, whose tick is past <paramref name="playbackTick"/>; the count if none.</summary>
    /// <remarks>
    /// **Where a read from the start stops**: <see cref="ReadPackets"/> reads every command until a packet waits for a
    /// later tick. Packet ticks are not monotonic in every file (<see cref="ParseAheadForInterval"/>'s backwards case),
    /// so this searches their running maximum, which is, and whose first excess over the tick is that same packet.
    /// </remarks>
    private int FirstPacketPast(int playbackTick)
    {
        int low = 0;
        int high = _latestPacketTick.Length;

        while (low < high)
        {
            int middle = (low + high) >>> 1;

            if (_latestPacketTick[middle] <= playbackTick)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private RecordedView? Interpolate(int playbackTick, float hostRemainder, DemoViewConVars conVars)
    {
        RecordedView current = _current >= 0 ? _commands[_current].View : default;
        bool hasView = !current.IsDefault;

        int target = playbackTick;

        if (_maxClients == 1)
        {
            target -= conVars.LegacyRollback ? 1 + (int)((_interpAmount / _interval) + 0.5f) : 1;
        }

        RecordedView? outinfo = null;

        if (!_interpolateView || !conVars.InterpolateView)
        {
            if (hasView)
            {
                outinfo = Viewpoint(current.Origin, current.Angles, current.LocalAngles);
            }
        }
        else if (hasView)
        {
            outinfo = Between(current, target, hostRemainder, conVars);
        }

        _lastInterpolatedTick = target;

        return outinfo;
    }

    /// <summary>The interpolating branch: the pair, the fraction, the two snaps and the blend.</summary>
    /// <remarks>
    /// **With <c>demo_retimeview</c> the ORIGIN has its own pair, on <see cref="_serverTimed"/>** (D238); the angles and
    /// everything else keep the engine's. The recorder writes the two from different clocks (<c>0x1800748b0</c>): the
    /// origin is the local player's (<c>IPrediction::GetViewOrigin</c>), which the measured views follow on the server's
    /// tick, and the view angles are <c>cl.viewangles</c> (<c>0x18053f4e4</c>), the client's own at the moment of writing
    /// — the stamp's clock, which is why they already pace evenly (finding 106). The origin takes the stamp's pair
    /// wherever the stamp's took its cut branch or found nothing, and wherever a queued cut lies between the two pairs'
    /// outermost ends, so every frame a cut can reach is the engine's; a target past every queued placement holds the
    /// last placed packet. Off, both pairs are one and the arithmetic is the engine's, operation for operation.
    /// </remarks>
    private RecordedView Between(RecordedView current, int target, float hostRemainder, DemoViewConVars conVars)
    {
        QueuedPair angles = Pair(current, target, _stamped);
        QueuedPair origins = angles;
        bool serverAxis = conVars.RetimeView && !angles.Cut && angles.PrevAt >= 0;

        if (serverAxis)
        {
            origins = Pair(current, target, _serverTimed);

            // A packet placed before its stamp can leave the target past every queued placement: after a stamp gap wider
            // than the parse-ahead, its successor is queued only once it is read. The origin holds that last placed
            // packet until then — the stamp's pair would put it back behind where the server's had already shown it.
            // **And where the target lies BEFORE every queued placement** (the first frames on a packet placed after its
            // stamp, as at the start of playback or a seek landing on one) there is nothing to hold: the origin is the
            // engine's blend, and when the server's pair appears it can step by up to one tick of motion
            // (InterpolateViewpoint_FirstFramesOnAPacketPlacedAfterItsStamp_StepNoMoreThanATickOfMotion).
            if (origins.PrevAt < 0)
            {
                int last = _packets.Count - 1;

                serverAxis = last >= 0 && _serverTimed[_packets[last]] <= target;
                origins = serverAxis
                    ? new(Queued(last, _serverTimed), Queued(last, _serverTimed), false, last, last)
                    : angles;
            }

            // **Around a cut the origin follows the engine's pair** (Fable and Opus reviews of 4c4b247b and b7a8a751).
            // A cut placed early on the server's axis — or lifted above its stamp — is reached by the origin's pair on a
            // different frame from the angles'; checking only the cut branch and the pair's two ends missed a cut two
            // ticks early and the hold above. So wherever any queued cut lies between the two pairs' outermost ends, the
            // origin takes the stamp's pair: every frame a cut can reach on either axis is the engine's.
            if (serverAxis && CutAmong(angles, origins))
            {
                serverAxis = false;
                origins = angles;
            }
        }

        (float angleDt, float angleFraction) = Fraction(angles.Prev.Tick, angles.Next.Tick, target, hostRemainder);
        (float originDt, float originFraction) = Fraction(origins.Prev.Tick, origins.Next.Tick, target, hostRemainder);

        // **The snap's interval.** The engine measures current-to-next over its pair's interval, and its pair starts at
        // the current packet or before it. On the server's axis the pair can start PAST the current one — a packet
        // placed before its stamp is not read yet — and over that one interval the distance from the current packet
        // spans two, which doubled the speed and snapped a rocket jump. So the interval runs from the earlier of the
        // pair's start and the current packet's placement: the engine's own wherever the pair starts at or before it.
        float speedDt = serverAxis
            ? (origins.Next.Tick - Math.Min(origins.Prev.Tick, _serverTimed[_current])) * _interval
            : originDt;

        (float X, float Y, float Z) from = origins.Prev.View.Origin;
        (float X, float Y, float Z) to = origins.Next.View.Origin;

        float originSpeed = 0f;
        float angularSpeed = 0f;

        if (speedDt > 0f)
        {
            (float X, float Y, float Z) now = current.Origin;
            float dx = to.X - now.X;
            float dy = to.Y - now.Y;
            float dz = to.Z - now.Z;

            originSpeed = MathF.Sqrt((dy * dy) + (dx * dx) + (dz * dz)) * (1f / speedDt);
        }

        if (angleDt > 0f)
        {
            angularSpeed = AngularSpeed(angles.Prev.View.LocalAngles, angles.Next.View.LocalAngles, 1d / angleDt);
        }

        if (originSpeed > conVars.InterpLimit || angularSpeed > conVars.AvelLimit || _resetInterpolation)
        {
            _resetInterpolation = false;

            return Viewpoint(current.Origin, current.Angles, current.LocalAngles);
        }

        return Viewpoint(
            (((to.X - from.X) * originFraction) + from.X, ((to.Y - from.Y) * originFraction) + from.Y, ((to.Z - from.Z) * originFraction) + from.Z),
            ScenePropTrack.SlerpAngles(angles.Prev.View.Angles, angles.Next.View.Angles, angleFraction),
            ScenePropTrack.SlerpAngles(angles.Prev.View.LocalAngles, angles.Next.View.LocalAngles, angleFraction));
    }

    /// <summary>The pair's interval in seconds and the clamped fraction of it the target has run (<c>0x180072440..0x18007248a</c>).</summary>
    private (float Dt, float Fraction) Fraction(int prev, int next, int target, float hostRemainder)
    {
        float dt = (next - prev) * _interval;
        float inverse = 1f / dt;
        float fraction = (((target - prev) * _interval) + hostRemainder) * inverse;

        // MAXSS then MINSS (0x180072469, 0x18007248a): a NaN takes the source operand, zero.
        fraction = fraction > 0f ? fraction : 0f;
        fraction = fraction < 1f ? fraction : 1f;

        return (dt, fraction);
    }

    /// <summary>The largest local-angle turn rate over the three axes (<c>0x180072573..0x1800725ba</c>).</summary>
    /// <remarks>Each difference is in floats; the division is a double multiply by <c>1.0 / dt</c>, narrowed back.</remarks>
    private static float AngularSpeed(
        (float Pitch, float Yaw, float Roll) from, (float Pitch, float Yaw, float Roll) to, double inverse)
    {
        float fastest = 0f;

        foreach ((float a, float b) in new[] { (from.Pitch, to.Pitch), (from.Yaw, to.Yaw), (from.Roll, to.Roll) })
        {
            float turned = AngleNormalize(AngleNormalizePositive(b) - AngleNormalizePositive(a));
            float speed = (float)(MathF.Abs(turned) * inverse);

            // MAXSS: a NaN keeps the running maximum.
            fastest = speed > fastest ? speed : fastest;
        }

        return fastest;
    }

    /// <summary>
    /// <c>0x180071fd0</c>: the queued pair with <c>prev.tick &lt;= target &lt; next.tick</c>, or the pair ending at the
    /// first <c>FDEMO_NOINTERP</c> packet in <c>(lastInterpolatedTick, target]</c>.
    /// </summary>
    /// <remarks>
    /// **Both start as <see cref="RecordedView.Reset"/> copies of the current view at tick -1**, which is what a search
    /// that finds nothing leaves them: the current view's originals, with a zero interval.
    ///
    /// **On either axis**: <paramref name="ticks"/> is <see cref="_stamped"/>, the engine's, or <see cref="_serverTimed"/>
    /// for <c>demo_retimeview</c>'s origin (D238). <c>Cut</c> says the cut branch answered.
    /// </remarks>
    private QueuedPair Pair(RecordedView current, int target, int[] ticks)
    {
        (int, RecordedView) none = (-1, current.Reset());

        if (_packets.Count < 2)
        {
            return new(none, none, false, -1, -1);
        }

        int cut = -1;

        for (int index = 0; index + 1 < _packets.Count; index++)
        {
            int a = _packets[index];
            int b = _packets[index + 1];

            if (ticks[a] <= target && target < ticks[b])
            {
                return cut < 0
                    ? new((ticks[a], _commands[a].View), (ticks[b], _commands[b].View), false, index, index + 1)
                    : new(Queued(cut, ticks), Queued(cut + 1, ticks), true, cut, cut + 1);
            }

            if (cut < 0 && _lastInterpolatedTick < ticks[b] && ticks[b] <= target && _commands[b].View.IsCut)
            {
                cut = index;
            }
        }

        return new(none, none, false, -1, -1);
    }

    /// <summary>A pair from <see cref="Pair"/>: its two ends, whether the cut branch answered, and the ends' places in the queue (-1 for none).</summary>
    private readonly record struct QueuedPair(
        (int Tick, RecordedView View) Prev, (int Tick, RecordedView View) Next, bool Cut, int PrevAt, int NextAt);

    /// <summary>Whether a queued <c>FDEMO_NOINTERP</c> packet lies anywhere from the first to the last end of two pairs.</summary>
    private bool CutAmong(QueuedPair one, QueuedPair other)
    {
        int from = Math.Min(one.PrevAt, other.PrevAt);
        int to = Math.Max(one.NextAt, other.NextAt);

        for (int position = Math.Max(0, from); position <= to; position++)
        {
            if (_commands[_packets[position]].View.IsCut)
            {
                return true;
            }
        }

        return false;
    }

    private (int Tick, RecordedView View) Queued(int position, int[] ticks) =>
        (ticks[_packets[position]], _commands[_packets[position]].View);

    /// <summary>Reads every command due by a playback tick, as <c>CL_ReadPackets</c> calls <c>ReadPacket</c>.</summary>
    /// <remarks>
    /// **A <c>dem_packet</c> waits for its tick; a signon, synctick or stop is read when reached** (the <c>0x1ca</c>
    /// mask at <c>0x180072ee0</c>). A tick behind the packet already read is a rewind, which the engine can only do by
    /// reloading and skipping forward (<c>SkipToTick</c>, <c>0x180073b10</c>). Only the last
    /// <see cref="KeptTicks"/> + <see cref="ParseAheadTicks"/> ticks of reading reach the list, so the skip starts
    /// that far back rather than at the beginning, and lands in the same state.
    ///
    /// **A rewind is a packet read that a read from the start would not reach** (`_current` at or past
    /// <see cref="FirstPacketPast"/>). With monotonic ticks that is exactly `_commands[_current].Tick &gt; playbackTick`;
    /// on a demo whose ticks run backwards the current packet's own tick can be at or below the target while a later-
    /// ticked packet before it was already read, and only the reload lands where the engine's reload does.
    /// </remarks>
    private void ReadPackets(int playbackTick)
    {
        if (_current >= 0 && _current >= FirstPacketPast(playbackTick))
        {
            Restart(playbackTick);
        }

        while (_read + 1 < _commands.Count &&
               (_commands[_read + 1].Type != DemoCommandType.Packet || _commands[_read + 1].Tick <= playbackTick))
        {
            ReadPacket(++_read);
        }
    }

    /// <summary><c>StartPlayback</c> then the skip's reads, from the last packet the list can no longer hold.</summary>
    /// <remarks>
    /// **Searched back from <see cref="FirstPacketPast"/>, where a read from the start stops** — not forward until the
    /// first command of ANY type past the target, which a real POV demo's signons (ticked 146,124 on etf2l-12025, at
    /// index 0) made the very first, so every rewind re-read from the start (batch-31 review 2). The start is the last
    /// packet before that stop whose tick is older than the list keeps; with none, the start of the file.
    ///
    /// **Older than the LAST PACKET READ keeps, not the target** (batch-33 review): the list is pruned against the tick of
    /// the packet being read, and across a gap that packet can be 40 ticks behind the target — a start counted from the
    /// target then began AT that packet, and the list lost the predecessors a fresh read still holds (60-tick gap, every
    /// target inside it). With ticks that never step back that start lands in a fresh read's list: every packet before it
    /// is older than <c>last - 32</c>, at the list's front, and pruned by the last read.
    ///
    /// **Bounded only for a <see cref="IsSimple"/> file; anything else re-reads from the start** (owner and lead,
    /// batch-33 review 2: "stop chasing edge cases; make the bound conservative"). Three reviews each found a shape the
    /// bound got wrong — a gap, a backward run, a backward run after a synctick, signons with no synctick — and a fourth
    /// listed more; a reload from the start is the engine's own `SkipToTick` and right by construction, so it is the
    /// answer for every shape the bound has not been proved on. `m_bInterpolateView` is cleared with the rest of the
    /// reader's state, as a fresh player has it.
    /// </remarks>
    private void Restart(int playbackTick)
    {
        _packets.Clear();
        _current = -1;
        _interpolateView = false;
        _lastInterpolatedTick = HostTickCount;

        int stop = FirstPacketPast(playbackTick);
        int from = 0;

        // The last packet a read to the stop reads — its own tick, not the running maximum, which an opening packet
        // ticked above the body sets.
        int last = stop - 1;

        while (last > _bodyStart && !_commands[last].IsPacket)
        {
            last--;
        }

        if (IsSimple && last > _bodyStart)
        {
            int oldest = _commands[last].Tick - KeptTicks - ParseAheadTicks;

            // Past the body's first packet, or the opening may leave a command a fresh read still holds (see `Simple`).
            for (int index = last; index > _bodyStart; index--)
            {
                if (_commands[index].IsPacket && _commands[index].Tick < oldest)
                {
                    from = index;
                    break;
                }
            }
        }

        _read = from - 1;
    }

    /// <summary>Commands read so far, rewinds' re-reads included: what a rewind costs, for a test and the rewind probe.</summary>
    /// <remarks>Internal: an instrument, not production surface; Core.Tests and the probe tool see internals.</remarks>
    internal long CommandsRead { get; private set; }

    /// <summary>What <c>ReadPacket</c> does with one command.</summary>
    private void ReadPacket(int index)
    {
        CommandsRead++;
        DemoViewCommand command = _commands[index];

        if (command.Type == DemoCommandType.SyncTick)
        {
            _lastInterpolatedTick = HostTickCount;
        }
        else if (command.IsPacket)
        {
            _current = index;
            _interpolateView = ParseAheadForInterval(index, command.Tick, ParseAheadTicks);
        }
    }

    /// <summary><c>ParseAheadForInterval</c> (<c>0x180072af0</c>): can the view interpolate from this packet.</summary>
    /// <remarks>
    /// Prunes queued packets older than <c>curtick - 32</c> that have been read, then queues every packet after this
    /// one up to and including the first more than <paramref name="interval"/> ticks on — true. A
    /// <c>dem_synctick</c>, a <c>dem_stop</c> or the end of the file before that is false. A packet whose tick runs
    /// backwards clears the queue first.
    /// </remarks>
    private bool ParseAheadForInterval(int current, int curtick, int interval)
    {
        while (_packets.Count > 0 && _commands[_packets[0]].Tick < curtick - KeptTicks && _packets[0] <= current)
        {
            _packets.RemoveAt(0);
        }

        for (int index = current + 1; index < _commands.Count; index++)
        {
            DemoViewCommand command = _commands[index];

            if (!command.IsPacket)
            {
                return false;
            }

            if (!_packets.Contains(index))
            {
                if (_packets.Count > 0 && _commands[_packets[^1]].Tick > command.Tick)
                {
                    _packets.Clear();
                }

                _packets.Add(index);
            }

            if (command.Tick - curtick > interval)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary><c>AngleNormalizePositive</c> (<c>mathlib_base.cpp:3524</c>, <c>0x180276410</c>).</summary>
    private static float AngleNormalizePositive(float angle)
    {
        angle %= 360f;

        // COMISS then JNC: below zero, or unordered.
        return angle >= 0f ? angle : angle + 360f;
    }

    /// <summary><c>AngleNormalize</c> (<c>mathlib_base.cpp:3508</c>, <c>0x1802763d0</c>).</summary>
    private static float AngleNormalize(float angle)
    {
        angle %= 360f;

        if (angle > 180f)
        {
            angle -= 360f;
        }

        if (angle < -180f)
        {
            angle += 360f;
        }

        return angle;
    }

    /// <summary>The <c>democmdinfo_t</c> outinfo <c>InterpolateViewpoint</c> fills: flags zero, the three originals set.</summary>
    private static RecordedView Viewpoint(
        (float X, float Y, float Z) origin,
        (float Pitch, float Yaw, float Roll) angles,
        (float Pitch, float Yaw, float Roll) localAngles) =>
        new() { ViewOrigin = origin, ViewAngles = angles, LocalViewAngles = localAngles };
}
