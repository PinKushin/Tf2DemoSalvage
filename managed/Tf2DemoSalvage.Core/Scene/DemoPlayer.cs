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
/// packet read, <c>m_bInterpolateView</c> (+0x63c), <c>m_bResetInterpolation</c> (+0x63d) and the last interpolated
/// tick (+0x554) — and it reads the timeline's stream the way <c>ReadPacket</c> (<c>0x180072ee0</c>) reads the file.
///
/// The playback clock is <see cref="PlaybackClock"/>; this is handed its fractional position.
/// </remarks>
public sealed class DemoPlayer
{
    private readonly IReadOnlyList<DemoViewCommand> _commands;

    /// <summary>The last command read, or -1.</summary>
    private int _read = -1;

    /// <summary>The last packet read — where the current <c>democmdinfo_t</c> (+0x5f0) came from — or -1.</summary>
    private int _current = -1;

    /// <summary>A demo player over a decoded demo.</summary>
    /// <param name="timeline">The demo.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeline"/> is null.</exception>
    public DemoPlayer(DemoTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        _commands = timeline.ViewCommands;
    }

    /// <summary>The watcher's <c>demo_*</c> ConVars.</summary>
    public DemoViewConVars ConVars { get; init; } = DemoViewConVars.Defaults;

    /// <summary><c>CDemoPlayer::InterpolateViewpoint</c> (<c>0x180072180</c>) at a moment of playback.</summary>
    /// <param name="tick">The playback position in demo ticks, the fraction included.</param>
    /// <returns>
    /// The view <c>IPrediction</c> hands the local player — <c>SetViewOrigin</c>, <c>SetViewAngles</c>,
    /// <c>SetLocalViewAngles</c> — or null while the current <c>democmdinfo_t</c> is still the default, when the engine
    /// sets nothing.
    /// </returns>
    public RecordedView? InterpolateViewpoint(double tick)
    {
        int playbackTick = (int)Math.Floor(tick);

        ReadPackets(playbackTick);

        RecordedView current = _current >= 0 ? _commands[_current].View : default;

        return current.IsDefault ? null : Viewpoint(current.Origin, current.Angles, current.LocalAngles);
    }

    /// <summary>Reads every command due by a playback tick, as <c>CL_ReadPackets</c> calls <c>ReadPacket</c>.</summary>
    /// <remarks>
    /// **A <c>dem_packet</c> waits for its tick; a signon, synctick or stop is read when reached** (the <c>0x1ca</c>
    /// mask at <c>0x180072ee0</c>). A tick behind the packet already read is a rewind, which the engine can only do by
    /// reloading the demo and skipping forward (<c>SkipToTick</c>, <c>0x180073b10</c>), so this starts over.
    /// </remarks>
    private void ReadPackets(int playbackTick)
    {
        if (_current >= 0 && _commands[_current].Tick > playbackTick)
        {
            _read = -1;
            _current = -1;
        }

        while (_read + 1 < _commands.Count &&
               (_commands[_read + 1].Type != DemoCommandType.Packet || _commands[_read + 1].Tick <= playbackTick))
        {
            _read++;

            if (_commands[_read].IsPacket)
            {
                _current = _read;
            }
        }
    }

    /// <summary>The <c>democmdinfo_t</c> outinfo <c>InterpolateViewpoint</c> fills: flags zero, the three originals set.</summary>
    private static RecordedView Viewpoint(
        (float X, float Y, float Z) origin,
        (float Pitch, float Yaw, float Roll) angles,
        (float Pitch, float Yaw, float Roll) localAngles) =>
        new() { ViewOrigin = origin, ViewAngles = angles, LocalViewAngles = localAngles };
}
