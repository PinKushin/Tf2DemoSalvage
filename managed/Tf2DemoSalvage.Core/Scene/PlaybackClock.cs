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

    /// <summary>How fast playback runs; one is real time, negative runs backwards.</summary>
    /// <remarks>
    /// The engine's own <c>demo_timescale</c>, applied to elapsed time rather than to the tick
    /// rate, so changing it mid-playback does not move the position.
    ///
    /// **Negative is something the real engine cannot do.** TF2 streams a demo forward and every
    /// snapshot is a delta against the one before it, so there is nothing to step back into — the
    /// engine would have to replay from the start to show the previous second. This project decodes
    /// the whole demo into absolute positions before playing any of it, which makes reverse cost
    /// exactly what forward costs.
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

    /// <summary>The exact position, including the part-tick between two of them.</summary>
    /// <remarks>
    /// **What a renderer wants, where <see cref="Tick"/> is what a readout wants.** A frame almost
    /// never lands on a tick boundary, and interpolation is the difference between a rocket that
    /// flies and one that steps — so the fraction the clock is already carrying has to be
    /// reachable rather than truncated on the way out.
    /// </remarks>
    public double Position => _position;

    /// <summary>Whether playback has reached the end of the demo.</summary>
    public bool AtEnd => _position >= _lastTick;

    /// <summary>Whether reverse playback has reached the start.</summary>
    public bool AtStart => _position <= 0;

    /// <summary>Advances by an interval of real time.</summary>
    /// <param name="seconds">How long has passed since the last call.</param>
    /// <remarks>
    /// **Negative elapsed time is refused; a negative TimeScale is not.** Time running backwards is
    /// a bad measurement and would show as a stutter, but a negative scale is reverse playback,
    /// which is deliberate — see <see cref="TimeScale"/>.
    /// </remarks>
    public void Advance(double seconds)
    {
        if (seconds <= 0 || TimeScale == 0)
        {
            return;
        }

        _position = Math.Clamp(
            _position + (seconds * TimeScale / _intervalPerTick), 0, _lastTick);
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
