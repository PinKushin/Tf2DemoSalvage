using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A view that records what it was told and raises what it is asked to.</summary>
/// <remarks>
/// **This class is the argument for MVP, in eleven lines of state.** D54 chose the pattern because
/// "a Presenter test needs a fake `IView` that records what was called and nothing else — no
/// WinForms runtime, no STA thread, no actual window spun up". This is that fake.
///
/// **<see cref="Playing"/> deliberately does NOT raise <see cref="PlayPauseToggled"/>.** The real
/// transport control has the same rule, and it is load-bearing: the presenter sets `Playing = false`
/// when the demo reaches an end, and a setter that raised the event would re-enter the presenter's
/// own handler. Modelling that faithfully is the difference between a fake and a prop.
/// </remarks>
internal sealed class FakePlaybackView : IPlaybackView
{
    /// <inheritdoc />
    public event EventHandler<TickEventArgs>? Scrubbed;

    /// <inheritdoc />
    public event EventHandler<PlayingEventArgs>? PlayPauseToggled;

    /// <inheritdoc />
    public event EventHandler<SpeedEventArgs>? SpeedChanged;

    /// <inheritdoc />
    public bool Playing { get; set; }

    /// <summary>Every tick the presenter asked to be displayed, in order.</summary>
    public List<int> ShownTicks { get; } = [];

    /// <summary>The last tick shown, or -1 when none has been.</summary>
    public int LastShownTick => ShownTicks.Count > 0 ? ShownTicks[^1] : -1;

    /// <inheritdoc />
    public void ShowTick(int tick) => ShownTicks.Add(tick);

    /// <summary>The length last set, or -1 when none has been.</summary>
    public int DemoLength { get; private set; } = -1;

    /// <inheritdoc />
    /// <remarks>
    /// **Clears <see cref="Playing"/>, because the real control does and that is the bug this fake
    /// has to be able to reproduce** (B223). `TransportBar.SetDemoLength` ends with
    /// <c>Playing = false</c>; a fake that only recorded the number would let a test pass against
    /// the exact ordering that shipped — the "wrong instrument" case, where the measurement is not
    /// faithful to the variable.
    /// </remarks>
    public void SetDemoLength(int lastTick)
    {
        DemoLength = lastTick;
        Playing = false;
    }

    /// <summary>Acts as the user dragging the scrubber.</summary>
    public void Scrub(int tick) => Scrubbed?.Invoke(this, new TickEventArgs(tick));

    /// <summary>Acts as the user pressing play or pause.</summary>
    /// <remarks>
    /// Sets the property first and then raises, which is the order a real control follows: the
    /// button is already down by the time anyone is told about it.
    /// </remarks>
    public void PressPlayPause(bool playing)
    {
        Playing = playing;
        PlayPauseToggled?.Invoke(this, new PlayingEventArgs(playing));
    }

    /// <summary>Acts as the user choosing a speed.</summary>
    public void ChooseSpeed(float speed) => SpeedChanged?.Invoke(this, new SpeedEventArgs(speed));
}

/// <summary>Elapsed time a test controls exactly.</summary>
/// <remarks>
/// **The reason this exists is that the project bans sleeping in tests**, and every rule worth
/// testing in <see cref="PlaybackPresenter"/> is about WHEN the elapsed clock is restarted or
/// reset. With a real <c>Stopwatch</c> the only way to observe those rules is to wait, which turns
/// a deterministic check into a probabilistic one.
/// </remarks>
/// <remarks>
/// **This models STOPPED as well as elapsed, and it did not, which made it blind to a shipped
/// bug.** `Seconds` used to be a plain settable property, so the fake reported time passing even
/// after `Reset` had stopped it. The real <c>StopwatchTime</c> wraps a `Stopwatch`, whose `Elapsed`
/// does not advance while it is not running — and that difference is the whole mechanism of the
/// autoplay regression: the flag said playing, the clock was never started, and `Advance` saw zero
/// elapsed for ever.
///
/// A test written against the old fake could not fail on that no matter how it was phrased, because
/// the fake's elapsed time did not depend on the state the bug turned on. That is the "wrong
/// instrument" case from the testing standards: the measurement was not faithful to the variable.
/// </remarks>
internal sealed class FakeElapsedTime : IElapsedTime
{
    private double _seconds;

    /// <summary>Whether the clock is running, as a real stopwatch tracks.</summary>
    private bool _running;

    /// <inheritdoc />
    /// <remarks>
    /// Zero while stopped, whatever the test set. Setting it is how a test makes time pass, but
    /// only a started clock reports any.
    /// </remarks>
    public double Seconds
    {
        get => _running ? _seconds : 0d;
        set => _seconds = value;
    }

    /// <summary>How many times <see cref="Restart"/> was called.</summary>
    public int Restarts { get; private set; }

    /// <summary>How many times <see cref="Reset"/> was called.</summary>
    public int Resets { get; private set; }

    /// <inheritdoc />
    public void Restart()
    {
        Restarts++;
        _seconds = 0;
        _running = true;
    }

    /// <inheritdoc />
    public void Reset()
    {
        Resets++;
        _seconds = 0;
        _running = false;
    }
}
