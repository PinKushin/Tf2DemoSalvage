using System;
using System.Globalization;

using Tf2DemoSalvage.Core.Net;

namespace Tf2DemoSalvage.Audio;

/// <summary>A `SND_DELAY` start's time on the server's clock, as `CL_DispatchSound` hands it on (B732).</summary>
/// <param name="Time">
/// The sound's delay + the last server tick's time − (ticks this frame − 1) × the interval (engine.dll FUN_18008caf0,
/// 0x18008cd65-0x18008cd89).
/// </param>
/// <param name="IntervalPerTick">The tick interval, which the clock's latch steps back by one of.</param>
/// <param name="Shift">
/// The user's `snd_delay_sound_shift`, read at each start as the engine reads the ConVar's value (`DAT_1804a58c8 + 0x54`,
/// 0x18002b19d); null for its default.
/// </param>
public readonly record struct ServerSoundTime(float Time, float IntervalPerTick, float? Shift = null);

/// <summary>
/// <c>S_ComputeDelayForSoundtime</c> for <c>CLOCK_SYNC_SERVER</c>: how long after now, on the mixer's clock, a sound timed on
/// the server's should start (B732). The rules, with their addresses, are <c>ServerSoundClockConformanceTests</c>'s remarks.
/// </summary>
/// <remarks>
/// engine.dll <c>FUN_18002b110</c>, build 11191711. The latch is a pair of globals per clock (<c>DAT_1804a7570</c>,
/// <c>DAT_1804a7578</c>), cleared by <c>FUN_180030850</c>; its owner (<c>AudioOutput</c>) clears it as the mixer does, when
/// no live channel was started with a delay.
/// </remarks>
public sealed class ServerSoundClock
{
    /// <summary>`snd_delay_sound_shift`'s default, added to every delay this answers.</summary>
    public static readonly float Shift = float.Parse(EngineConVars.ByName("snd_delay_sound_shift").Default, CultureInfo.InvariantCulture);

    /// <summary>The server time latched against <see cref="_paintedBase"/>, less one tick; 0 when unlatched.</summary>
    private float _soundTimeBase;

    /// <summary>The mixer's painted time when the latch was taken.</summary>
    private long _paintedBase;

    /// <summary>Whether a sound has latched the clock and nothing has dropped it since.</summary>
    public bool Latched => _soundTimeBase != 0f;

    /// <summary>The delay before a sound timed on the server's clock starts.</summary>
    /// <param name="time">Its server time.</param>
    /// <param name="painted">The mixer's painted time now, in samples; the engine's is an int, which wraps after 13.5 hours.</param>
    /// <param name="rate">The mixer's rate.</param>
    /// <returns>The delay in seconds, the shift added; 0 when the two clocks are out of step, which unlatches them.</returns>
    public float Delay(ServerSoundTime time, long painted, int rate)
    {
        // An exact compare, as `ucomiss` against 0 at 0x18002b138; the latch at 0x18002b140-0x18002b14c.
        if (_soundTimeBase == 0f)
        {
            _paintedBase = painted;
            _soundTimeBase = time.Time - time.IntervalPerTick;
        }

        float since = time.Time - _soundTimeBase;
        float delay = ((int)(rate * since) - painted + _paintedBase) / (float)rate;

        // 0x18002b17a-0x18002b19b: a negative distance from the base, or more than 0.2 either way, unlatches.
        if (since >= 0f && Math.Abs(delay) <= 0.2f)
        {
            return delay + (time.Shift ?? Shift);
        }

        Reset();

        return 0f;
    }

    /// <summary>Unlatches the clock: the next sound latches it afresh (<c>FUN_180030850</c>).</summary>
    public void Reset()
    {
        _soundTimeBase = 0f;
        _paintedBase = 0;
    }
}
