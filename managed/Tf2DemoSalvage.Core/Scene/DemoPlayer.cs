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

    private readonly IReadOnlyList<DemoViewCommand> _commands;
    private readonly float _interval;
    private readonly int _maxClients;
    private readonly float _interpAmount;

    /// <summary>The parse-ahead list (+0x5d0): indices into the stream, in stream order.</summary>
    private readonly List<int> _packets = [];

    /// <summary>The last command read, or -1.</summary>
    private int _read = -1;

    /// <summary>The last packet read — where the current <c>democmdinfo_t</c> (+0x5f0) came from — or -1.</summary>
    private int _current = -1;

    /// <summary><c>m_bInterpolateView</c> (+0x63c), from the last packet's parse-ahead.</summary>
    private bool _interpolateView;

    /// <summary><c>m_bResetInterpolation</c> (+0x63d).</summary>
    private bool _resetInterpolation;

    /// <summary>+0x554: the target of the last interpolation.</summary>
    private int _lastInterpolatedTick = HostTickCount;

    /// <summary>The moment last asked about, and its answer — the engine interpolates once a rendered frame.</summary>
    private double _askedTick = double.NaN;
    private RecordedView? _answer;

    /// <summary>A demo player over a decoded demo.</summary>
    /// <param name="timeline">The demo.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> is null.</exception>
    public DemoPlayer(DemoTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        _commands = timeline.ViewCommands;
        _interval = timeline.IntervalPerTick > 0f ? timeline.IntervalPerTick : PlaybackClock.DefaultIntervalPerTick;
        _maxClients = timeline.MaxClients;
        _interpAmount = (float)timeline.ClientInterpAmount;
    }

    /// <summary>The watcher's <c>demo_*</c> ConVars.</summary>
    public DemoViewConVars ConVars { get; init; } = DemoViewConVars.Defaults;

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
        // The same stored moment, bit for bit — a question about repetition, not about nearness.
        if (BitConverter.DoubleToInt64Bits(tick) == BitConverter.DoubleToInt64Bits(_askedTick))
        {
            return _answer;
        }

        // GetPlaybackTick() and host_remainder: the whole tick and the part of the next one already run.
        int playbackTick = (int)Math.Floor(tick);
        float hostRemainder = (float)((tick - playbackTick) * _interval);

        ReadPackets(playbackTick);

        _askedTick = tick;
        _answer = Interpolate(playbackTick, hostRemainder);

        return _answer;
    }

    private RecordedView? Interpolate(int playbackTick, float hostRemainder)
    {
        RecordedView current = _current >= 0 ? _commands[_current].View : default;
        bool hasView = !current.IsDefault;

        int target = playbackTick;

        if (_maxClients == 1)
        {
            target -= ConVars.LegacyRollback ? 1 + (int)((_interpAmount / _interval) + 0.5f) : 1;
        }

        RecordedView? outinfo = null;

        if (!_interpolateView || !ConVars.InterpolateView)
        {
            if (hasView)
            {
                outinfo = Viewpoint(current.Origin, current.Angles, current.LocalAngles);
            }
        }
        else if (hasView)
        {
            outinfo = Between(current, target, hostRemainder);
        }

        _lastInterpolatedTick = target;

        return outinfo;
    }

    /// <summary>The interpolating branch: the pair, the fraction, the two snaps and the blend.</summary>
    private RecordedView Between(RecordedView current, int target, float hostRemainder)
    {
        ((int Tick, RecordedView View) prev, (int Tick, RecordedView View) next) = Pair(current, target);

        float dt = (next.Tick - prev.Tick) * _interval;
        float inverse = 1f / dt;
        float fraction = (((target - prev.Tick) * _interval) + hostRemainder) * inverse;

        // MAXSS then MINSS (0x180072469, 0x18007248a): a NaN takes the source operand, zero.
        fraction = fraction > 0f ? fraction : 0f;
        fraction = fraction < 1f ? fraction : 1f;

        (float X, float Y, float Z) from = prev.View.Origin;
        (float X, float Y, float Z) to = next.View.Origin;

        float originSpeed = 0f;
        float angularSpeed = 0f;

        if (dt > 0f)
        {
            (float X, float Y, float Z) now = current.Origin;
            float dx = to.X - now.X;
            float dy = to.Y - now.Y;
            float dz = to.Z - now.Z;

            originSpeed = MathF.Sqrt((dy * dy) + (dx * dx) + (dz * dz)) * inverse;
            angularSpeed = AngularSpeed(prev.View.LocalAngles, next.View.LocalAngles, 1d / dt);
        }

        if (originSpeed > ConVars.InterpLimit || angularSpeed > ConVars.AvelLimit || _resetInterpolation)
        {
            _resetInterpolation = false;

            return Viewpoint(current.Origin, current.Angles, current.LocalAngles);
        }

        return Viewpoint(
            (((to.X - from.X) * fraction) + from.X, ((to.Y - from.Y) * fraction) + from.Y, ((to.Z - from.Z) * fraction) + from.Z),
            ScenePropTrack.SlerpAngles(prev.View.Angles, next.View.Angles, fraction),
            ScenePropTrack.SlerpAngles(prev.View.LocalAngles, next.View.LocalAngles, fraction));
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
    /// </remarks>
    private ((int Tick, RecordedView View) Prev, (int Tick, RecordedView View) Next) Pair(RecordedView current, int target)
    {
        (int, RecordedView) none = (-1, current.Reset());

        if (_packets.Count < 2)
        {
            return (none, none);
        }

        int cut = -1;

        for (int index = 0; index + 1 < _packets.Count; index++)
        {
            DemoViewCommand a = _commands[_packets[index]];
            DemoViewCommand b = _commands[_packets[index + 1]];

            if (a.Tick <= target && target < b.Tick)
            {
                return cut < 0
                    ? ((a.Tick, a.View), (b.Tick, b.View))
                    : (Queued(cut), Queued(cut + 1));
            }

            if (cut < 0 && _lastInterpolatedTick < b.Tick && b.Tick <= target && b.View.IsCut)
            {
                cut = index;
            }
        }

        return (none, none);
    }

    private (int Tick, RecordedView View) Queued(int position) =>
        (_commands[_packets[position]].Tick, _commands[_packets[position]].View);

    /// <summary>Reads every command due by a playback tick, as <c>CL_ReadPackets</c> calls <c>ReadPacket</c>.</summary>
    /// <remarks>
    /// **A <c>dem_packet</c> waits for its tick; a signon, synctick or stop is read when reached** (the <c>0x1ca</c>
    /// mask at <c>0x180072ee0</c>). A tick behind the packet already read is a rewind, which the engine can only do by
    /// reloading and skipping forward (<c>SkipToTick</c>, <c>0x180073b10</c>). Only the last
    /// <see cref="KeptTicks"/> + <see cref="ParseAheadTicks"/> ticks of reading reach the list, so the skip starts
    /// that far back rather than at the beginning, and lands in the same state.
    /// </remarks>
    private void ReadPackets(int playbackTick)
    {
        if (_current >= 0 && _commands[_current].Tick > playbackTick)
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
    private void Restart(int playbackTick)
    {
        _packets.Clear();
        _current = -1;
        _lastInterpolatedTick = HostTickCount;

        int from = 0;

        for (int index = 0; index < _commands.Count && _commands[index].Tick <= playbackTick; index++)
        {
            if (_commands[index].IsPacket && _commands[index].Tick < playbackTick - KeptTicks - ParseAheadTicks)
            {
                from = index;
            }
        }

        _read = from - 1;
    }

    /// <summary>What <c>ReadPacket</c> does with one command.</summary>
    private void ReadPacket(int index)
    {
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
