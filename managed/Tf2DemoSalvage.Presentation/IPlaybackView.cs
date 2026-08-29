using System;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The user moved the scrubber.</summary>
/// <param name="tick">Where they moved it to.</param>
/// <remarks>
/// **A named argument type rather than <c>EventHandler&lt;int&gt;</c>**, which CA1003 rejects and
/// which is worse anyway at a call site: `(_, e) => Seek(e.Tick)` says what the number is, and
/// `(_, tick)` only says what someone chose to call the parameter.
/// </remarks>
public sealed class TickEventArgs(int tick) : EventArgs
{
    /// <summary>The tick.</summary>
    public int Tick { get; } = tick;
}

/// <summary>The user started or stopped playback.</summary>
/// <param name="playing">Whether it is now running.</param>
public sealed class PlayingEventArgs(bool playing) : EventArgs
{
    /// <summary>Whether playback is now running.</summary>
    public bool Playing { get; } = playing;
}

/// <summary>The user changed the playback speed.</summary>
/// <param name="speed">The multiplier, 1 being real time and negative running backwards.</param>
public sealed class SpeedEventArgs(double speed) : EventArgs
{
    /// <summary>The speed multiplier.</summary>
    public double Speed { get; } = speed;
}

/// <summary>The moment the scene should now show.</summary>
/// <param name="position">Ticks, including the fraction between them.</param>
/// <remarks>
/// **A double, and the fraction is the point.** Truncating to a whole tick snaps every pose to the
/// last packet and makes the interpolation layer a no-op that still passes all of its own tests, so
/// the type carries what the clock actually knows rather than what is easy to display.
/// </remarks>
public sealed class MomentEventArgs(double position) : EventArgs
{
    /// <summary>The position in ticks, fraction included.</summary>
    public double Position { get; } = position;
}

/// <summary>
/// The transport controls, as the presenter sees them.
/// </summary>
/// <remarks>
/// **Deliberately narrow, per D55's warning about a God Presenter.** One `IViewerView` covering
/// playback, the event list and the render surface would grow a presenter that does all three, and
/// the fix named in advance was several small interfaces per cohesive concern. This is one concern:
/// where playback is, whether it is running, and how fast.
///
/// **Events out, methods in.** The view raises what the user did and is told what to display; it
/// makes no decision about either. A scrub raises <see cref="Scrubbed"/> and does not move the
/// clock; the presenter decides what a scrub means and calls <see cref="ShowTick"/> back.
/// </remarks>
public interface IPlaybackView
{
    /// <summary>The user moved the scrubber to a tick.</summary>
    public event EventHandler<TickEventArgs>? Scrubbed;

    /// <summary>The user started or stopped playback.</summary>
    public event EventHandler<PlayingEventArgs>? PlayPauseToggled;

    /// <summary>The user changed the playback speed, 1 being real time.</summary>
    /// <remarks>Negative runs backwards, which the clock supports and the end checks allow for.</remarks>
    public event EventHandler<SpeedEventArgs>? SpeedChanged;

    /// <summary>Whether the controls show playback as running.</summary>
    /// <remarks>
    /// Settable because the presenter stops playback on its own when the demo reaches an end, and
    /// the controls have to follow. Setting it must NOT raise <see cref="PlayPauseToggled"/>, or
    /// the presenter re-enters itself.
    /// </remarks>
    public bool Playing { get; set; }

    /// <summary>Displays the current tick.</summary>
    /// <param name="tick">The tick to show.</param>
    public void ShowTick(int tick);

    /// <summary>Sizes and enables the controls for a demo of the given length.</summary>
    /// <param name="lastTick">Highest tick in the demo; zero means no demo, and disables them.</param>
    /// <remarks>
    /// **On the interface since 2026-08-29, because the window calling it directly was a bug** — the
    /// third time autoplay's ordering has broken (B223, D118). `MainForm.Apply` called
    /// <c>DemoSystems.Open</c>, which starts playback, and then the transport's own
    /// <c>SetDemoLength</c>, whose last act is <c>Playing = false</c>. That setter deliberately does
    /// not raise <see cref="PlayPauseToggled"/>, so nothing logged it and nothing failed: the viewer
    /// wrote *"playback started at load"* and sat paused for ever.
    ///
    /// Resetting playback is part of what this does, so it belongs on the same side of the
    /// presenter as <see cref="Playing"/> — with one owner deciding the order rather than two
    /// callers agreeing on it.
    /// </remarks>
    public void SetDemoLength(int lastTick);
}

/// <summary>How much real time has passed, so playback can be tested without waiting.</summary>
/// <remarks>
/// **The one dependency that makes the stopwatch discipline testable.** Playback's subtle behaviour
/// is entirely about *when* the elapsed clock is restarted and reset — three separate rules, each
/// with a reason — and a presenter reading <c>Stopwatch</c> directly can only be tested by sleeping,
/// which this project bans outright.
/// </remarks>
public interface IElapsedTime
{
    /// <summary>Seconds since the last <see cref="Restart"/>, or zero when reset.</summary>
    public double Seconds { get; }

    /// <summary>Starts measuring again from zero.</summary>
    public void Restart();

    /// <summary>Stops measuring and reports zero until restarted.</summary>
    public void Reset();
}
