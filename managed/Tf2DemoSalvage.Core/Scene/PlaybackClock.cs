using System;

namespace Tf2DemoSalvage.Core.Scene;

/// <summary>
/// Turns elapsed real time into a demo tick.
/// </summary>
/// <remarks>
/// **The rate comes from the demo.** <c>svc_ServerInfo</c> states seconds per tick, and it is not
/// always TF2's usual 0.015: it is a server setting, so a box left at its default runs a different
/// rate from one someone configured, and that is a property of the server rather than of the era.
/// A demo replayed at the wrong rate is wrong in a way that reads as a slow or fast server rather
/// than as a defect, which is why nothing here has a rate baked into it and the fallback is named
/// rather than implied.
///
/// **A fractional position, because a frame is not a tick.** At sixty frames a second each frame is
/// 1.11 ticks at TF2's rate; truncating every frame loses a tenth of a tick sixty times a second
/// and the demo runs measurably slow. The remainder is carried.
///
/// Pure arithmetic with no timer inside it: a clock that owned its own timing could only be tested
/// by waiting, and waiting in a test is exactly what this project bans. The caller supplies elapsed
/// seconds from whatever clock it has.
/// </remarks>
public sealed class PlaybackClock
{
    /// <summary>Seconds per tick when the demo never said.</summary>
    /// <remarks>
    /// <c>DEFAULT_TICK_INTERVAL</c> from Valve's <c>src/public/const.h</c>, whose comment is
    /// "15 msec is the default".
    /// </remarks>
    public const float DefaultIntervalPerTick = 0.015f;

    private readonly double _intervalPerTick;
    private readonly int _lastTick;

    private double _position;

    /// <summary>Creates a clock for one demo.</summary>
    /// <param name="intervalPerTick">Seconds per tick, or zero to use the engine default.</param>
    /// <param name="lastTick">The demo's final tick; playback stops there.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lastTick"/> is negative.</exception>
    public PlaybackClock(float intervalPerTick, int lastTick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastTick);

        _intervalPerTick = intervalPerTick > 0f ? intervalPerTick : DefaultIntervalPerTick;
        _lastTick = lastTick;
    }

    /// <summary>How fast playback runs; one is real time.</summary>
    /// <remarks>
    /// The engine's own <c>demo_timescale</c>. Applied to elapsed time rather than to the tick
    /// rate, so changing it mid-playback does not move the position.
    /// </remarks>
    public double TimeScale { get; set; } = 1.0;

    /// <summary>The tick to show.</summary>
    /// <remarks>
    /// **Floor, not Valve's <c>TIME_TO_TICKS</c>, and the difference is the question being asked.**
    /// That macro is <c>(int)(0.5f + dt / TICK_INTERVAL)</c> — it rounds, because it converts a
    /// duration into a number of ticks. A playback position wants the tick that has actually
    /// occurred, which is what the engine's own loop plays packets against; rounding would show a
    /// tick before its time by up to half an interval.
    ///
    /// The relationship itself is Valve's: <c>TICKS_TO_TIME(t)</c> is <c>interval_per_tick * t</c>,
    /// so a position is time divided by the interval.
    /// </remarks>
    public int Tick => (int)_position;

    /// <summary>Whether playback has reached the end of the demo.</summary>
    public bool AtEnd => _position >= _lastTick;

    /// <summary>Advances by an interval of real time.</summary>
    /// <param name="seconds">How long has passed since the last call.</param>
    /// <remarks>
    /// Negative time is refused rather than played backwards: a clock that ran back on a bad
    /// measurement would look like a stutter, and the demo can be scrubbed instead.
    /// </remarks>
    public void Advance(double seconds)
    {
        if (seconds <= 0 || TimeScale <= 0)
        {
            return;
        }

        _position = Math.Min(_lastTick, _position + (seconds * TimeScale / _intervalPerTick));
    }

    /// <summary>Jumps to a tick, discarding any part-tick already accumulated.</summary>
    /// <param name="tick">Where to go; clamped to the demo.</param>
    /// <remarks>
    /// **The remainder is dropped deliberately.** Carrying it across a seek makes the first tick
    /// after a scrub arrive early, which shows up as playback that does not quite line up with the
    /// bar the user just dragged.
    /// </remarks>
    public void Seek(int tick) => _position = Math.Clamp(tick, 0, _lastTick);
}
