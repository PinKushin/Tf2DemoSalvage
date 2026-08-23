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
internal sealed class FakeElapsedTime : IElapsedTime
{
    /// <inheritdoc />
    public double Seconds { get; set; }

    /// <summary>How many times <see cref="Restart"/> was called.</summary>
    public int Restarts { get; private set; }

    /// <summary>How many times <see cref="Reset"/> was called.</summary>
    public int Resets { get; private set; }

    /// <inheritdoc />
    public void Restart()
    {
        Restarts++;
        Seconds = 0;
    }

    /// <inheritdoc />
    public void Reset()
    {
        Resets++;
        Seconds = 0;
    }
}
