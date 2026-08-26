namespace Tf2DemoSalvage.Presentation;

/// <summary>What to do while waiting for the next frame to come due.</summary>
public enum FrameWait
{
    /// <summary>Nothing: there is no limit, so the next frame is already due.</summary>
    None,

    /// <summary>Spin, because less than a scheduler quantum remains.</summary>
    Yield,

    /// <summary>Sleep, because there is enough time left to give the CPU back.</summary>
    Sleep,
}

/// <summary>When the next frame is due, and what to do until it is.</summary>
/// <remarks>
/// **This was `MainForm.FrameIsDue` and `MainForm.WaitForTheNextFrame`** (B208). Pacing is engine
/// behaviour — the engine's own is `fps_max`, *"Frame rate limiter, cannot be set while connected to
/// a server"* (read from `bin/engine.dll`) — and a window's part in it is only to own the clock and
/// call the primitive.
///
/// **The two callers derived `1d / framesPerSecond` independently**, which is the same quantity
/// written twice: they had to agree and nothing made them.
///
/// **`Thread.Sleep` and `Thread.Yield` are deliberately NOT here.** This type decides; the message
/// pump acts. That keeps the threading primitive beside the pump where it belongs, and it is what
/// lets every case below be tested without a test ever sleeping — which matters, because the
/// standing rule against `Thread.Sleep` is about tests that wait on a clock rather than a condition.
///
/// **The sleep in the caller is a genuine exception to that rule, and it is named as one.** A frame
/// limiter's sleep is not synchronisation — it is not waiting for anything to become true, it is
/// giving the CPU back for a known duration. The engine does the same.
/// </remarks>
public static class FramePacer
{
    /// <summary>How long a `Thread.Sleep(1)` actually takes to return, near enough.</summary>
    /// <remarks>
    /// **`Thread.Sleep(1)` does not return in 1 ms.** Windows' default timer granularity is about
    /// 15.6 ms, so the call returns after a whole tick of it.
    ///
    /// **Measured, not reasoned:** a limiter built on sleep alone capped at about 64 frames a second
    /// whatever it was asked for — a limit of 300 produced 63 to 66. That measurement is why this
    /// threshold exists rather than being a tidy constant: above roughly 62 fps the budget is
    /// shorter than a single sleep, so the whole wait has to be spun or the cap becomes a floor.
    /// </remarks>
    public const double SleepGranularitySeconds = 0.016;

    /// <summary>How long one frame is allowed to take.</summary>
    /// <param name="framesPerSecond">The limit; zero or below means no limit.</param>
    /// <returns>Seconds per frame, or zero when there is no limit.</returns>
    public static double Budget(int framesPerSecond) =>
        framesPerSecond <= 0 ? 0d : 1d / framesPerSecond;

    /// <summary>Whether the next frame may be drawn yet.</summary>
    /// <param name="sinceLastFrame">Seconds since the last frame was drawn.</param>
    /// <param name="framesPerSecond">The limit; zero or below means no limit.</param>
    /// <returns>True when the frame is due.</returns>
    public static bool IsDue(double sinceLastFrame, int framesPerSecond) =>
        framesPerSecond <= 0 || sinceLastFrame >= Budget(framesPerSecond);

    /// <summary>What to do while the next frame is not yet due.</summary>
    /// <param name="sinceLastFrame">Seconds since the last frame was drawn.</param>
    /// <param name="framesPerSecond">The limit; zero or below means no limit.</param>
    /// <returns>Whether to sleep, spin, or neither.</returns>
    public static FrameWait WaitFor(double sinceLastFrame, int framesPerSecond)
    {
        if (framesPerSecond <= 0)
        {
            return FrameWait.None;
        }

        return Budget(framesPerSecond) - sinceLastFrame > SleepGranularitySeconds
            ? FrameWait.Sleep
            : FrameWait.Yield;
    }

    /// <summary>How long to give the CPU back because nobody is looking, in milliseconds.</summary>
    /// <param name="hasFocus">Whether the viewer's window has focus.</param>
    /// <param name="milliseconds">The configured sleep; below zero is treated as none.</param>
    /// <returns>Milliseconds to sleep before the next frame, or zero.</returns>
    /// <remarks>
    /// **The engine's own <c>engine_no_focus_sleep</c>, which ships at 50 and is `FCVAR_ARCHIVE`**
    /// (B209). It is a DURATION in milliseconds rather than a flag, so the shipped game runs at
    /// roughly 20 frames a second while alt-tabbed. This viewer rendered at its full rate.
    ///
    /// **Independent of the frame limiter, deliberately, because it is a different question.**
    /// <see cref="WaitFor"/> asks "is the next frame due yet" and its answer is a spin above about
    /// 62 fps; this asks "is anyone watching", and the answer is a real sleep whatever the limit is.
    /// Folding the two together would make an unfocused window spin at 300 fps to stay under its
    /// cap, which is precisely the battery this is meant to stop burning.
    ///
    /// **A sleep here does not break the no-sleep rule** for the same reason
    /// <see cref="SleepGranularitySeconds"/> does not: it is not waiting for anything to become
    /// true, it is handing the CPU back for a known duration. The engine does the same.
    /// </remarks>
    public static int NoFocusSleep(bool hasFocus, int milliseconds) =>
        hasFocus || milliseconds <= 0 ? 0 : milliseconds;
}
