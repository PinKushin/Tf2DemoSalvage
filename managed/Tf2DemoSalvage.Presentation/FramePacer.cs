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
}
