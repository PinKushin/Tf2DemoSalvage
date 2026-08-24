using System;
using System.Globalization;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// What TF2's own frame rate meter shows, written down before implementing one.
/// </summary>
/// <remarks>
/// **Every claim below is read from <c>src/game/client/vgui_fpspanel.cpp</c>**, which is published
/// in `source-sdk-2013`. The panel is client code rather than engine code, so this is the whole
/// mechanism and not an inference from watching it — the smoothing weight, the watermarks, the
/// colour thresholds and the two format strings are all literal there.
///
/// **The cvar is `cl_showfps`, declared at line 27:**
///
/// <code>
/// static ConVar cl_showfps( "cl_showfps", "0", FCVAR_ALLOWED_IN_COMPETITIVE,
///                           "Draw fps meter at top of screen (1 = fps, 2 = smooth fps)" );
/// </code>
///
/// It ships in retail `client.dll` — checked by scanning the binary, per
/// `docs/memory/binaries-answer-what-the-sdk-cannot.md` — so this is a name a player can type
/// today, and matching it is parity rather than homage (D79).
///
/// **Why a conformance suite rather than just unit tests.** The owner asked for this because he
/// cannot tell three different stutters apart: *"i have no idea what fps we are rendering at and
/// cant tell stutter in the demo from stutter in the decode, from stutter in fps"*. An instrument
/// built to answer that has to be trustworthy, and the cheapest way to make it trustworthy is to
/// copy one whose behaviour is already known to the person reading it. Anyone who has run
/// `cl_showfps 2` in TF2 knows what the three numbers mean; a meter of our own invention would have
/// to be learned, and its oddities would be indistinguishable from the stutter it is measuring.
/// </remarks>
public sealed class FpsMeterConformanceTests
{
    /// <summary>A frame time that divides exactly, so the expected values need no tolerance.</summary>
    private const double SixtyHertz = 1d / 60d;

    /// <summary>
    /// The panel skips its first paint, because it has no previous time to subtract.
    /// </summary>
    /// <remarks>
    /// <c>InitAverages</c> sets <c>m_lastRealTime = -1</c>, and <c>Paint</c> guards the whole draw
    /// with <c>if ( m_lastRealTime != -1.0f )</c> before assigning it at the end. So the frame on
    /// which the meter is switched on shows nothing at all.
    ///
    /// **Faithfully reproduced rather than smoothed over, because the alternative is a lie.** The
    /// first frame's "duration" is however long the meter was off, which for a viewer left paused
    /// on a map could be minutes. Showing 0 fps for one frame would be a measurement of nothing,
    /// and it would arrive exactly when the user is looking.
    /// </remarks>
    [Test]
    public void Sample_TheFirstFrameAfterBeingShown_ReportsNothing()
    {
        FpsMeter meter = new() { Mode = 1 };

        meter.Sample(SixtyHertz).ShouldBeNull();
        meter.Sample(SixtyHertz).ShouldNotBeNull();
    }

    /// <summary>
    /// Mode one divides one by the frame time and truncates.
    /// </summary>
    /// <remarks>
    /// <c>nFps = static_cast&lt;int&gt;( 1.0f / realFrameTime );</c> — a C cast, so it truncates
    /// toward zero rather than rounding. At 1/60 s that is exactly 60; at a frame a hair longer it
    /// is 59 rather than 60, which is why a meter that rounds reads differently from TF2's on the
    /// same machine.
    /// </remarks>
    [Test]
    public void Sample_InModeOne_IsOneOverTheFrameTimeTruncated()
    {
        FpsMeter meter = new() { Mode = 1 };

        meter.Sample(SixtyHertz);

        // 1 / 0.016 = 62.5, and the cast takes 62 rather than 63.
        meter.Sample(0.016d)!.Value.Fps.ShouldBe(62);
    }

    /// <summary>
    /// Mode two is an exponential moving average with a weight of one tenth.
    /// </summary>
    /// <remarks>
    /// <code>
    /// const float NewWeight  = 0.1f;
    /// float NewFrame = 1.0f / realFrameTime;
    /// ...
    /// m_AverageFPS *= ( 1.0f - NewWeight );
    /// m_AverageFPS += ( ( NewFrame ) * NewWeight );
    /// </code>
    ///
    /// **Seeded from the first sample rather than from zero**, which is the branch above it:
    /// <c>if ( m_AverageFPS &lt; 0.0f ) { m_AverageFPS = NewFrame; ... }</c>. An average that
    /// started at zero would climb for the first second and read low exactly while somebody is
    /// watching it settle.
    ///
    /// So: seed at 100, then one 50 fps frame gives 100*0.9 + 50*0.1 = 95.
    /// </remarks>
    [Test]
    public void Sample_InModeTwo_WeightsTheNewFrameByOneTenth()
    {
        FpsMeter meter = new() { Mode = 2 };

        meter.Sample(0.01d);              // the skipped first frame
        meter.Sample(0.01d)!.Value.Fps.ShouldBe(100, "the first reading seeds the average");
        meter.Sample(0.02d)!.Value.Fps.ShouldBe(95, "100 * 0.9 + 50 * 0.1");
    }

    /// <summary>
    /// The bracketed pair is the worst and best single frame, not a window.
    /// </summary>
    /// <remarks>
    /// <code>
    /// int NewFrameInt = (int)NewFrame;
    /// if( NewFrameInt &lt; m_low ) m_low = NewFrameInt;
    /// if( NewFrameInt &gt; m_high ) m_high = NewFrameInt;
    /// </code>
    ///
    /// **Nothing ever decays them.** They are only reset by <c>InitAverages</c>, which runs when the
    /// panel goes from hidden to shown — so they are watermarks for as long as the meter has been
    /// on, not for the last second. That is precisely what makes them useful here: the low is the
    /// worst frame since you turned it on, which is the number B163 needs and which an average
    /// cannot show.
    ///
    /// Note they track the INSTANTANEOUS rate, not the average — so the pair can bracket a number
    /// the average never reaches.
    /// </remarks>
    [Test]
    public void Sample_InModeTwo_TracksTheWorstAndBestSingleFrame()
    {
        FpsMeter meter = new() { Mode = 2 };

        meter.Sample(0.01d);
        meter.Sample(0.01d);              // seeds average, low and high at 100
        meter.Sample(0.1d);               // one 10 fps frame
        FpsReading settled = meter.Sample(0.005d)!.Value;   // one 200 fps frame

        settled.Low.ShouldBe(10);
        settled.High.ShouldBe(200);

        // And the average is nowhere near either of them, which is the whole point of showing all
        // three: 100 -> 91 -> 101.9.
        settled.Fps.ShouldBe(101);
    }

    /// <summary>
    /// Hiding and showing the meter forgets the watermarks.
    /// </summary>
    /// <remarks>
    /// <c>ShouldDraw</c> calls <c>InitAverages</c> only on the transition into being drawn:
    ///
    /// <code>
    /// if ( !m_bLastDraw )
    /// {
    ///     m_bLastDraw = true;
    ///     InitAverages();
    /// }
    /// </code>
    ///
    /// **A control that must survive, and this is the one the test would be blind without.** With
    /// only the reset asserted, an implementation that reset on EVERY sample would pass — the
    /// watermarks would be right for one frame and useless for ever after. So a bystander meter
    /// that is never hidden must keep its low across the same sequence.
    /// </remarks>
    [Test]
    public void Sample_AfterBeingHiddenAndShown_ForgetsTheWatermarks()
    {
        FpsMeter hidden = new() { Mode = 2 };
        FpsMeter bystander = new() { Mode = 2 };

        foreach (FpsMeter meter in new[] { hidden, bystander })
        {
            meter.Sample(0.01d);
            meter.Sample(0.01d);
            meter.Sample(0.1d);           // a 10 fps frame, recorded as the low by both
        }

        hidden.Mode = 0;
        hidden.Mode = 2;

        hidden.Sample(0.01d).ShouldBeNull("showing it again skips a frame, as switching it on does");
        hidden.Sample(0.01d)!.Value.Low.ShouldBe(100, "the 10 fps frame was forgotten");

        bystander.Sample(0.01d)!.Value.Low.ShouldBe(10, "a meter left on keeps its worst frame");
    }

    /// <summary>
    /// Passing through mode one destroys the watermarks, without the meter ever being hidden.
    /// </summary>
    /// <remarks>
    /// **This one was written wrong first, and the source corrected it** — worth keeping, because
    /// the wrong version is the intuitive one. Mode one assigns <c>m_AverageFPS = -1;</c> on every
    /// paint, in the <c>else</c> branch beside the smoothing. Returning to mode two therefore takes
    /// the seeding branch, and that branch does not only seed the average:
    ///
    /// <code>
    /// if ( m_AverageFPS &lt; 0.0f )
    /// {
    ///     m_AverageFPS = NewFrame;
    ///     m_high = (int)m_AverageFPS;
    ///     m_low = (int)m_AverageFPS;
    /// }
    /// </code>
    ///
    /// So the pair is reset by any path that clears the average, and <c>InitAverages</c> is not the
    /// only such path. A run of <c>cl_showfps 1</c> in the middle of a session silently throws away
    /// the worst frame the meter had seen — which matters here, because that number is the one B163
    /// wants and somebody toggling modes to read the line differently would lose it without being
    /// told.
    /// </remarks>
    [Test]
    public void Sample_AfterPassingThroughModeOne_ReseedsBothTheAverageAndTheWatermarks()
    {
        FpsMeter meter = new() { Mode = 2 };

        meter.Sample(0.01d);
        meter.Sample(0.01d);
        meter.Sample(0.1d);               // low is now 10

        meter.Mode = 1;
        meter.Sample(0.02d);              // 50 fps, instantaneous, and the average is parked at -1

        meter.Mode = 2;
        FpsReading back = meter.Sample(0.02d)!.Value;

        back.Fps.ShouldBe(50, "the average was re-seeded from this frame, not blended with 91");
        back.Low.ShouldBe(50, "the seeding branch resets the pair as well");
        back.High.ShouldBe(50);
    }

    /// <summary>
    /// The meter reports nothing at all when it is off.
    /// </summary>
    /// <remarks>
    /// <c>ShouldDraw</c> returns false for <c>cl_showfps 0</c>, and the panel is hidden. Asserted
    /// because the viewer calls this from the frame loop unconditionally, so "off" has to be free
    /// rather than merely invisible.
    /// </remarks>
    [Test]
    public void Sample_WhenTheModeIsZero_ReportsNothing()
    {
        FpsMeter meter = new() { Mode = 0 };

        meter.Sample(SixtyHertz).ShouldBeNull();
        meter.Sample(SixtyHertz).ShouldBeNull();
    }

    /// <summary>
    /// A frame of no duration is not measured.
    /// </summary>
    /// <remarks>
    /// <c>if ( cl_showfps.GetInt() &amp;&amp; realFrameTime &gt; 0.0 )</c> — the guard exists because
    /// dividing by it is the next line. Ours can see a zero for a different reason: a stopwatch read
    /// twice inside one tick of its resolution.
    /// </remarks>
    [Test]
    public void Sample_AFrameOfNoDuration_ReportsNothing()
    {
        FpsMeter meter = new() { Mode = 1 };

        meter.Sample(SixtyHertz);
        meter.Sample(0d).ShouldBeNull();
        meter.Sample(-1d).ShouldBeNull();
    }

    /// <summary>
    /// The colour thresholds are sixty and fifty on anything modern.
    /// </summary>
    /// <remarks>
    /// <c>GetFPSColor</c> picks its thresholds from the hardware level:
    ///
    /// <code>
    /// if ( IsPC() &amp;&amp; g_pMaterialSystemHardwareConfig->GetDXSupportLevel() >= 95 )
    /// {
    ///     nFPSThreshold1 = 60;
    ///     nFPSThreshold2 = 50;
    /// }
    /// </code>
    ///
    /// **We are always in that branch**, so the lower pair (30/25 for DX9, 20/15 below) is not
    /// reproduced: this viewer is Direct3D 11 and there is no setting that makes it pretend
    /// otherwise — the one explicit exception in `docs/findings/13-settings-parity.md`.
    ///
    /// Note the yellow: the branch sets only the green channel and leaves red at 255, so it is
    /// (255, 255, 0) rather than an amber of someone's choosing.
    /// </remarks>
    [TestCase(300, 0, 255, 0)]
    [TestCase(60, 0, 255, 0)]
    [TestCase(59, 255, 255, 0)]
    [TestCase(50, 255, 255, 0)]
    [TestCase(49, 255, 0, 0)]
    [TestCase(1, 255, 0, 0)]
    public void Colour_ForARate_MatchesGetFpsColor(int fps, int red, int green, int blue)
    {
        FpsMeter.ColourFor(fps).ShouldBe(((byte)red, (byte)green, (byte)blue));
    }

    /// <summary>
    /// Mode one's line is the rate and the map, and mode two's adds the pair and the milliseconds.
    /// </summary>
    /// <remarks>
    /// The two format strings, verbatim:
    ///
    /// <code>
    /// "%3i fps on %s"
    /// "%3i fps (%3i, %3i) %.1f ms on %s"
    /// </code>
    ///
    /// **The map name carries its extension**, because it is
    /// <c>V_GetFileName( engine->GetLevelName() )</c> and <c>V_GetFileName</c> is
    /// <c>V_UnqualifiedFileName</c> — it strips the directory and keeps the rest. So TF2 shows
    /// `cp_process_f12.bsp`, not `cp_process_f12`.
    ///
    /// <c>%3i</c> is right-aligned in three columns, which is what stops the line juddering
    /// sideways as the rate crosses 100 — worth keeping for exactly the reason the meter exists.
    /// </remarks>
    [Test]
    public void Text_InEitherMode_MatchesTheFormatStringsInTheFpsPanel()
    {
        FpsMeter one = new() { Mode = 1 };
        one.Sample(0.01d);

        one.Sample(0.02d)!.Value.Text("cp_process_f12.bsp")
            .ShouldBe(" 50 fps on cp_process_f12.bsp");

        FpsMeter two = new() { Mode = 2 };
        two.Sample(0.01d);
        two.Sample(0.01d);
        two.Sample(0.02d);

        // Average 95, low 50, high 100, and this frame took 20 ms.
        two.Sample(0.02d)!.Value.Text("cp_process_f12.bsp")
            .ShouldBe(" 90 fps ( 50, 100) 20.0 ms on cp_process_f12.bsp");
    }

    /// <summary>
    /// The milliseconds are this frame's, not the average's.
    /// </summary>
    /// <remarks>
    /// <c>float frameMS = realFrameTime * 1000.0f;</c> — the raw frame time, taken before any
    /// smoothing. So the number beside a steady average jumps around, and that is the intent: it is
    /// the only unsmoothed thing on the line.
    /// </remarks>
    [Test]
    public void Sample_InModeTwo_ReportsThisFramesMillisecondsRatherThanTheAverages()
    {
        FpsMeter meter = new() { Mode = 2 };

        meter.Sample(0.01d);
        meter.Sample(0.01d);

        meter.Sample(0.25d)!.Value.FrameMilliseconds.ShouldBe(250d, 0.001d);
    }

    /// <summary>
    /// The rate is computed in single precision, as the engine computes it.
    /// </summary>
    /// <remarks>
    /// **This is the kind of detail that decides whether a parity claim is real.** Valve's average
    /// is a `float`, so it accumulates single-precision error over a long run; ours accumulating in
    /// `double` would drift away from the number TF2 shows on the same machine, slowly and
    /// invisibly.
    ///
    /// A thousand alternating frames is enough for the two to separate: the assertion is that our
    /// average matches a `float` replay of Valve's recurrence exactly, bit for bit, rather than
    /// merely closely.
    /// </remarks>
    [Test]
    public void Sample_OverAThousandFrames_MatchesASinglePrecisionReplayExactly()
    {
        FpsMeter meter = new() { Mode = 2 };

        meter.Sample(0.01d);

        float expected = -1f;

        for (int at = 0; at < 1000; at++)
        {
            double seconds = at % 2 == 0 ? 0.013d : 0.007d;

            // Valve's recurrence, in Valve's precision.
            float frame = 1f / (float)seconds;

            if (expected < 0f)
            {
                expected = frame;
            }
            else
            {
                expected *= 1f - 0.1f;
                expected += frame * 0.1f;
            }

            meter.Sample(seconds);
        }

        meter.Sample(0.013d);

        // Replayed one more time for the final sample, so the comparison is against a full replay.
        float last = 1f / 0.013f;
        expected *= 1f - 0.1f;
        expected += last * 0.1f;

        meter.Average.ToString("R", CultureInfo.InvariantCulture)
            .ShouldBe(expected.ToString("R", CultureInfo.InvariantCulture));
    }
}
