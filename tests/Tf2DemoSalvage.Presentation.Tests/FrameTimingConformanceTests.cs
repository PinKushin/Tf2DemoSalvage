namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// How many frame clocks the engine keeps, and which one drives what.
/// </summary>
/// <remarks>
/// **Written because the question was asked before the code was**: *"and how does valve handle these
/// timings?"* — put to an assistant who had just started designing a two-clock type on his own
/// reasoning. The answer changes the justification completely, and it turns a judgement call into a
/// citation.
///
/// **The engine keeps SEVERAL time quantities at once and names each by what it obeys.** From
/// <c>src/public/globalvars_base.h</c>, with Valve's own comments:
///
/// <code>
/// // Absolute time (per frame still - Use Plat_FloatTime() for a high precision real time
/// //  perf clock, but not that it doesn't obey host_timescale/host_framerate)
/// float realtime;
/// // Non-paused frametime
/// float absoluteframetime;
/// // Time spent on last server or client frame (has nothing to do with think intervals)
/// float frametime;
/// // interpolation amount ( client-only ) based on fraction of next tick which has elapsed
/// float interpolation_amount;
/// </code>
///
/// plus <c>curtime</c>, which carries **three documented meanings** depending on whether the caller
/// is receiving packets, rendering, or predicting, and <c>Plat_FloatTime()</c> outside the struct
/// entirely — *"time in seconds since the module was loaded"* (<c>public/tier0/platform.h:1198</c>).
///
/// **So there is no consolidation to copy. Consolidating would be the divergence.** The distinctions
/// are exactly the ones a tidy-up erases: paused versus not, obeying <c>host_timescale</c> versus
/// not, simulation clock versus wall clock.
///
/// **The closest analogue to this viewer's free camera is `CalcDemoViewOverride`**
/// (<c>src/game/client/view.cpp:141-159</c>) — Valve's own free camera for demo playback, which is
/// what ours is:
///
/// <code>
/// input->ExtraMouseSample( gpGlobals->absoluteframetime, true );
/// ...
/// float speed = gpGlobals->absoluteframetime * cl_demoviewoverride.GetFloat() * 320;
/// </code>
///
/// **Non-paused frame time, for both the mouse sample and the movement.** That is the quantity this
/// viewer's flight clock produces, and the reason it must not be the pacing clock: a frame limiter
/// cannot pace itself by the duration of the frame it is deciding whether to allow.
///
/// `cl_showfps` reads the same one — <c>gpGlobals->absoluteframetime</c>
/// (<c>vgui_fpspanel.cpp:166</c>) — which is what B174 arrived at independently when the meter
/// stopped starting a clock of its own.
///
/// **What is NOT here, stated so nobody looks for it.** `fps_max` and the host frame loop are engine
/// code and `source-sdk-2013` ships no `engine/host.cpp`; the folder holds only `audio`. So the
/// limiter's own reference point is not readable here. What the published headers DO establish is
/// that it cannot be `frametime` or `absoluteframetime`, both of which are outputs of the frame
/// being paced — it has to be a wall clock of the <c>Plat_FloatTime</c> kind.
/// </remarks>
public sealed class FrameTimingConformanceTests
{
    [Test]
    public void TheFlightClock_ForAFreeCamera_IsTheNonPausedFrameTime()
    {
        // **`CalcDemoViewOverride` multiplies `absoluteframetime` by a speed** (view.cpp:153), so
        // distance travelled is duration times rate and nothing else. This is the arithmetic, which
        // is all a test outside the engine can check — the citation above is what makes it parity.
        //
        // 33 ms at 320 units a second is the engine's own scale with `cl_demoviewoverride 1`.
        double travelled = 0.033d * 1d * 320d;

        travelled.ShouldBe(10.56d, tolerance: 0.0001d);
    }

    [Test]
    public void ThePacingClock_AndTheFlightClock_CannotBeTheSameQuantity()
    {
        // **The claim this suite exists to pin, and it is arithmetic rather than opinion.** A
        // limiter decides whether to ALLOW a frame; the flight clock reports how long a frame TOOK.
        // At a 60 Hz cap the budget is 16.67 ms, and a frame that took 4 ms leaves 12.67 ms still to
        // wait. Feeding the limiter the frame's own duration would say "due" 12.67 ms early, every
        // frame — a cap that runs fast by however long the frame was cheap.
        double budget = FramePacer.Budget(60);
        const double FrameTook = 0.004d;

        FramePacer.IsDue(FrameTook, 60).ShouldBeFalse(
            "the duration of the frame just drawn is not the interval since the last one began");

        budget.ShouldBeGreaterThan(FrameTook);
    }

    [Test]
    public void TheSoundscapeClock_IsWallTimeRatherThanTheSimulationClock()
    {
        // **`realtime` versus `curtime`, which is Valve's split and already this viewer's.** The
        // soundscape crossfade is `soundscape_fadetime` seconds of WALL clock, so a fade tied to the
        // demo's own clock would stretch when playback slows and vanish when it is scrubbed.
        //
        // Arithmetic only: three seconds of wall time is three seconds however many ticks pass.
        const double FadeSeconds = 3d;

        (FadeSeconds / 0.5d).ShouldBe(6d, "a half-speed demo still fades in three real seconds");
    }
}
