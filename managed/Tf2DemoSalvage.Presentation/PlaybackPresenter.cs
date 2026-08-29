using System;
using System.Diagnostics;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Presentation;

/// <summary>
/// Drives playback: what the transport controls mean, and where the demo has got to.
/// </summary>
/// <remarks>
/// **Lifted out of `MainForm` on 2026-08-22 (D62), where it was four methods and three fields
/// entangled with a WinForms control.** None of it could be tested without constructing a form and
/// a real `Stopwatch`, which is why behaviour this careful had no tests at all — every rule below
/// was written from a bug and none of them was pinned.
///
/// **The stopwatch discipline is the whole of the difficulty**, and it is three separate rules:
///
/// - **Starting play restarts the clock.** Real time passed while paused is not playback time, and
///   feeding it in on the first tick jumps the demo forward by however long the user was reading
///   the map.
/// - **Pausing resets it**, so the same gap cannot be collected while stopped.
/// - **Changing speed restarts it, but only while playing**, so the frame straddling the change is
///   not counted at the new speed.
///
/// **A stall is not elapsed playback time**, either. Loading a map or dragging the window by its
/// title bar stops the loop for a while, and handing that whole gap to the clock teleports the demo.
/// Capping the step turns a hitch into a brief slowdown, which is what an engine does with its
/// frame time.
/// </remarks>
public sealed class PlaybackPresenter
{
    /// <summary>Longest step handed to the clock in one frame.</summary>
    /// <remarks>
    /// 100 ms, matching what the form used. The number is a judgement rather than a measurement:
    /// long enough that no real frame reaches it, short enough that a hitch becomes a slowdown
    /// rather than a jump.
    /// </remarks>
    public const double MaximumFrameSeconds = 0.1;

    private readonly IPlaybackView _view;
    private readonly IElapsedTime _elapsed;

    private PlaybackClock? _clock;

    /// <summary>Wires a presenter to its view.</summary>
    /// <param name="view">The transport controls.</param>
    /// <param name="elapsed">Where real time comes from; a stopwatch in production.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public PlaybackPresenter(IPlaybackView view, IElapsedTime elapsed)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(elapsed);

        _view = view;
        _elapsed = elapsed;

        _view.Scrubbed += OnScrubbed;
        _view.PlayPauseToggled += OnPlayPauseToggled;
        _view.SpeedChanged += OnSpeedChanged;
    }

    /// <summary>The moment to draw, in ticks, including the fraction between them.</summary>
    /// <remarks>
    /// **The fractional position, not the whole tick.** Truncating here snaps every pose to the
    /// last packet and makes the interpolation layer a no-op that still passes all its own tests.
    /// </remarks>
    public event EventHandler<MomentEventArgs>? MomentChanged;

    /// <summary>Whether a demo is loaded and can be played.</summary>
    public bool HasDemo => _clock is not null;

    /// <summary>Takes the clock for a newly loaded demo.</summary>
    /// <param name="clock">The demo's clock, or null when one is unloaded.</param>
    public void Load(PlaybackClock? clock)
    {
        _clock = clock;

        _view.Playing = false;
        _elapsed.Reset();

        if (clock is not null)
        {
            _view.ShowTick(clock.Tick);
        }
    }

    /// <summary>Starts playback, as pressing Play does.</summary>
    /// <remarks>
    /// **Because assigning the view's flag is not the same thing, and the difference shipped as a
    /// bug.** `TransportBar.Playing`'s setter deliberately does not raise `PlayPauseToggled` — it
    /// would make this presenter re-enter its own handler through the control it had just updated —
    /// so setting the property relabels the button and tells the clock nothing. The elapsed timer
    /// stays stopped, `Advance` returns on a non-positive elapsed for ever, and the viewer sits at
    /// tick zero insisting it is playing.
    ///
    /// The owner met exactly that: *"the ui says its playing but the demo is not actually playing,
    /// no ticks go by, i have to 'pause' which does nothing then hit play again to get it
    /// started"*. Pause-then-play works because the button goes through the user path, which raises.
    ///
    /// **So there has to be a way to say "play" that is not a property assignment**, and it belongs
    /// here rather than in the host: the presenter owns the clock (D62), and the host cannot start
    /// one it cannot see. `MainForm` had reinvented it as an assignment, and the comment beside that
    /// assignment records the same fault being found and fixed once before.
    ///
    /// Silent when nothing is loaded, because the startup path calls it before it knows whether a
    /// timeline was decoded.
    /// </remarks>
    /// <summary>Where playback currently is, in ticks, or null when no demo is open.</summary>
    /// <remarks>
    /// **Exposed so nobody else has to hold the clock** (D98 follow-up). `MainForm` kept its own
    /// `PlaybackClock?` field — the same object this already had, handed to both by
    /// `DemoSystems.Open` — which is two references to one piece of state and the usual way two
    /// answers to one question appear.
    ///
    /// **The fraction is kept**, as everywhere else: truncating to a whole tick snaps every pose to
    /// the last packet and makes the interpolation layer a no-op that still passes its own tests.
    /// </remarks>
    public double? Position => _clock?.Position;

    /// <summary>Move playback to a tick.</summary>
    /// <param name="tick">Where to go; the clock clamps it to the demo's length.</param>
    /// <remarks>
    /// **Does nothing when no demo is open**, rather than refusing. A seek arriving before a demo
    /// is loaded is ordinary — a launch option asks for one — and the clock is what knows the
    /// bounds, so clamping stays there rather than being duplicated by every caller.
    /// </remarks>
    public void Seek(int tick) => _clock?.Seek(tick);

    /// <summary>Sizes the controls for a demo of the given length.</summary>
    /// <param name="lastTick">Highest tick in the demo; zero disables playback.</param>
    /// <remarks>
    /// **A forward, and it exists so that nobody reaches past the presenter to the control** (D62,
    /// D118). The window used to call the transport itself, immediately after the call that starts
    /// autoplay — and since sizing the controls also clears the playing flag, that silently undid
    /// it. One owner of the order is the fix; a second caller agreeing to go first is not.
    ///
    /// Deliberately NOT folded into <see cref="Load"/>, though the two are always called together
    /// on the success path: a demo whose schema failed to decode has a LENGTH from its header and
    /// no clock at all, and folding them would leave that demo's scrub bar dead. That case is
    /// ordinary here — decoding is what this project is still finishing.
    /// </remarks>
    public void SetDemoLength(int lastTick) => _view.SetDemoLength(lastTick);

    public void Play()
    {
        if (_clock is null)
        {
            return;
        }

        _view.Playing = true;
        _elapsed.Restart();
    }

    /// <summary>Moves playback on by however long has passed since the last frame.</summary>
    /// <remarks>
    /// Called once per frame by the host's render loop. Does nothing unless playing, so the caller
    /// need not ask first.
    /// </remarks>
    public void Advance()
    {
        if (!_view.Playing || _clock is not { } clock)
        {
            return;
        }

        double seconds = _elapsed.Seconds;

        if (seconds <= 0)
        {
            return;
        }

        _elapsed.Restart();

        clock.Advance(Math.Min(seconds, MaximumFrameSeconds));

        _view.ShowTick(clock.Tick);
        MomentChanged?.Invoke(this, new MomentEventArgs(clock.Position));

        // **Whichever end it is travelling towards.** Stopping only at the end would leave reverse
        // playback spinning against tick zero while still claiming to play.
        if ((clock.TimeScale > 0 && clock.AtEnd) || (clock.TimeScale < 0 && clock.AtStart))
        {
            _view.Playing = false;
            _elapsed.Reset();
        }
    }

    private void OnScrubbed(object? sender, TickEventArgs e)
    {
        if (_clock is not { } clock)
        {
            return;
        }

        // A scrub is a seek: the clock takes the new position outright and drops whatever part-tick
        // it had accumulated, or the next tick after a drag arrives early.
        clock.Seek(e.Tick);

        MomentChanged?.Invoke(this, new MomentEventArgs(e.Tick));
    }

    private void OnPlayPauseToggled(object? sender, PlayingEventArgs e)
    {
        if (e.Playing && _clock is not null)
        {
            _elapsed.Restart();
        }
        else
        {
            _elapsed.Reset();
        }
    }

    private void OnSpeedChanged(object? sender, SpeedEventArgs e)
    {
        if (_clock is not { } clock)
        {
            return;
        }

        // **The scale multiplies elapsed time, not the tick rate**, which is how Valve's own replay
        // editor does it — `replayperformanceeditor.cpp` multiplies its elapsed by `host_timescale`.
        // Scaling the rate instead moves the current position the instant the speed changes,
        // because the position is measured in ticks.
        clock.TimeScale = e.Speed;

        if (_view.Playing)
        {
            _elapsed.Restart();
        }
    }
}

/// <summary>Real elapsed time, from a <see cref="Stopwatch"/>.</summary>
/// <remarks>
/// The production implementation, and deliberately trivial: everything worth testing lives in the
/// presenter, which is why this exists at all.
/// </remarks>
public sealed class StopwatchTime : IElapsedTime
{
    private readonly Stopwatch _watch = new();

    /// <inheritdoc />
    public double Seconds => _watch.Elapsed.TotalSeconds;

    /// <inheritdoc />
    public void Restart() => _watch.Restart();

    /// <inheritdoc />
    public void Reset() => _watch.Reset();
}
