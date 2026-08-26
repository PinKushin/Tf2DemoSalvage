using System;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The viewer's frame clocks: when a frame may begin, and how long the last one took.</summary>
/// <remarks>
/// **Two clocks, and Valve is why** — not a judgement call. The engine keeps several time quantities
/// live at once and names each by what it obeys (<c>public/globalvars_base.h</c>): <c>realtime</c>,
/// which follows <c>host_timescale</c>; <c>Plat_FloatTime()</c>, which deliberately does not;
/// <c>frametime</c>; <c>absoluteframetime</c>, the same but non-paused; and <c>curtime</c>, whose
/// meaning changes three ways depending on whether the caller is receiving packets, rendering or
/// predicting. There is no consolidation in the engine to copy, and the distinctions are exactly the
/// ones a tidy-up erases. `FrameTimingConformanceTests` carries the citations.
///
/// **The nearest analogue to this viewer's free camera is `CalcDemoViewOverride`**
/// (<c>game/client/view.cpp:141-159</c>) — the engine's own free camera for demo playback. It flies
/// by <c>gpGlobals->absoluteframetime</c>, which is what <see cref="Drew"/> reports. `cl_showfps`
/// reads the same quantity (<c>vgui_fpspanel.cpp:166</c>), which is where B174 arrived on its own
/// when the meter stopped starting a third clock.
///
/// **They cannot be one clock**, and the reason is arithmetic rather than taste: a limiter decides
/// whether to ALLOW a frame, while the flight clock reports how long a frame TOOK. At a 60 Hz cap a
/// frame that cost 4 ms still leaves 12.67 ms to wait; pacing by the frame's own duration would call
/// every frame due early, by however long that frame happened to be cheap.
///
/// **Both were fields of `MainForm`** (B188, D90) — `_lastFrameAt` and `_flyWatch` — and neither
/// one's documentation mentioned the other. One argued its case against `FramePacer`, the other
/// against the playback clock. Nobody had compared them, which is what made "were the clocks
/// consolidated?" a fair question with no answer in the code. They are still two; the difference is
/// that the relationship is now stated in one place.
///
/// **The decision is here, the ACT stays with the window.** <see cref="WaitFor"/> answers sleep,
/// yield or neither; calling <c>Thread.Sleep</c> is the message loop's business. That split is
/// <see cref="FramePacer"/>'s and this move preserves it.
/// </remarks>
public sealed class FrameClock
{
    /// <summary>Since the last frame was ALLOWED to begin. The pacing reference.</summary>
    private readonly IElapsedTime _sinceAllowed;

    /// <summary>Since the last frame started DRAWING. The flight and meter reference.</summary>
    private readonly IElapsedTime _sinceDrawn;

    /// <summary>Whether a frame has ever been allowed, so the first one need not wait.</summary>
    private bool _allowedOne;

    /// <summary>Whether a frame has ever been drawn, so the first reports no duration.</summary>
    private bool _drewOne;

    /// <summary>Creates a clock over two independent time sources.</summary>
    /// <param name="sinceAllowed">Measures from when a frame was last allowed.</param>
    /// <param name="sinceDrawn">Measures from when a frame last started drawing.</param>
    /// <exception cref="ArgumentNullException">Either source is null.</exception>
    public FrameClock(IElapsedTime sinceAllowed, IElapsedTime sinceDrawn)
    {
        ArgumentNullException.ThrowIfNull(sinceAllowed);
        ArgumentNullException.ThrowIfNull(sinceDrawn);

        _sinceAllowed = sinceAllowed;
        _sinceDrawn = sinceDrawn;
    }

    /// <summary>How long the last drawn frame took, in seconds. Unclamped.</summary>
    /// <remarks>
    /// **Unclamped, and this is the one rule worth stating twice.** The reading used to pass through
    /// the free camera's stall clamp, so the worst frame could never be reported as worse than
    /// 100 ms — the ceiling. The owner's report was "everything freezes for a half a second to maybe
    /// a second" and the log for those exact seconds said `longest 100 ms`: the clamp showing
    /// through, not a measurement. The clamp lives with FLIGHT, which is what it was always for.
    /// </remarks>
    public double LastFrameSeconds { get; private set; }

    /// <summary>Whether enough time has passed to draw another frame, stamping if it has.</summary>
    /// <param name="framesPerSecond">The cap, or zero and below for none.</param>
    /// <returns>Whether to draw now.</returns>
    /// <remarks>
    /// **The cap has to be applied here, because asking for vertical sync does not work.** The swap
    /// chain presents with a sync interval of one and the viewer was still measured at about 600
    /// frames a second: a driver forcing vsync off globally outranks the present call. So the only
    /// ceiling that holds is one this program keeps itself.
    ///
    /// **This does not affect what is drawn, only how often.** The animation cycle is advanced from
    /// DEMO time — the tick and the demo's own interval — never from frame time, so a demo looks
    /// identical at 24 frames a second and at 300. That separation is the thing GoldSrc got wrong.
    ///
    /// **Uncapped, the pacing mark is not stamped at all**, which is faithful to what this replaced
    /// and is recorded rather than quietly fixed: the mark then goes stale for as long as the cap is
    /// off. Harmless today, because <see cref="WaitFor"/> is unreachable while uncapped — and
    /// precisely the kind of thing that stops being harmless when a third reader appears.
    /// </remarks>
    public bool IsDue(int framesPerSecond)
    {
        if (framesPerSecond <= 0)
        {
            return true;
        }

        if (!_allowedOne)
        {
            _allowedOne = true;
            _sinceAllowed.Restart();

            return true;
        }

        if (!FramePacer.IsDue(_sinceAllowed.Seconds, framesPerSecond))
        {
            return false;
        }

        _sinceAllowed.Restart();

        return true;
    }

    /// <summary>What to do while waiting for the next frame.</summary>
    /// <param name="framesPerSecond">The cap.</param>
    /// <returns>Sleep, yield, or neither.</returns>
    public FrameWait WaitFor(int framesPerSecond) =>
        FramePacer.WaitFor(_sinceAllowed.Seconds, framesPerSecond);

    /// <summary>Records that a frame is starting to draw, and reports the last one's duration.</summary>
    /// <returns>Seconds the previous frame took; zero for the first.</returns>
    /// <remarks>
    /// **Zero for the first frame rather than "since the program started".** The camera flies by this
    /// duration, so a first frame reporting several seconds would fling it across the map.
    /// </remarks>
    public double Drew()
    {
        LastFrameSeconds = _drewOne ? _sinceDrawn.Seconds : 0d;

        _drewOne = true;
        _sinceDrawn.Restart();

        return LastFrameSeconds;
    }
}
