using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// How far behind the present a demo is drawn — the engine's own arithmetic.
/// </summary>
/// <remarks>
/// **<c>C_BaseEntity::GetInterpolationAmount</c>, <c>client/c_baseentity.cpp:5920</c>**, on the
/// branch that names demo playback outright:
///
/// <code>
///   // Always fully interpolate during multi-player or during demo playback, if the recorded
///   // demo was recorded locally.
///   const bool bPlayingDemo = engine-&gt;IsPlayingDemo();
///   const bool bPlayingMultiplayer = !bPlayingDemo &amp;&amp; ( gpGlobals-&gt;maxClients &gt; 1 );
///   const bool bPlayingNonLocallyRecordedDemo =
///       bPlayingDemo &amp;&amp; !engine-&gt;IsPlayingDemoALocallyRecordedDemo();
///   if ( bPlayingMultiplayer || bPlayingNonLocallyRecordedDemo )
///   {
///       return AdjustInterpolationAmount( this,
///           TICKS_TO_TIME( TIME_TO_TICKS( GetClientInterpAmount() ) + serverTickMultiple ) );
///   }
/// </code>
///
/// **The macros**, <c>shared/shareddefs.h:14</c>:
///
/// <code>
///   #define TICK_INTERVAL      (gpGlobals-&gt;interval_per_tick)
///   #define TIME_TO_TICKS( dt )  ( (int)( 0.5f + (float)(dt) / TICK_INTERVAL ) )
///   #define TICKS_TO_TIME( t )   ( TICK_INTERVAL *( t ) )
/// </code>
///
/// **The interp amount**, <c>client/cdll_bounded_cvars.cpp:127</c> — the larger of the two cvars,
/// which at TF2's defaults (<c>cl_interp</c> 0.1, <c>cl_interp_ratio</c> 2,
/// <c>cl_updaterate</c> 66) is <c>MAX( 0.1, 0.0303 )</c> = 0.1:
///
/// <code>
///   return MAX( cl_interp-&gt;GetFloat(),
///               cl_interp_ratio-&gt;GetFloat() / ( ... pUpdateRate-&gt;GetFloat() ) );
/// </code>
///
/// **So the delay is <c>TIME_TO_TICKS(interp) + 1</c> ticks, and the <c>+ 1</c> is the point of
/// this file.** `serverTickMultiple` is 1 except when the server simulates on alternate ticks, and
/// `AdjustInterpolationAmount` only raises the figure for NPCs (`cl_interp_npcs`), which TF2 does
/// not field — and it applies the same `+ 1` when it does.
///
/// This project used seven, derived correctly from 0.1 s at 66.67 ticks and then stopping one term
/// early: the rounding is `TIME_TO_TICKS`, and the engine adds a tick after it. Every interpolated
/// entity was therefore drawn a tick nearer the present than the engine draws it.
/// </remarks>
public sealed class InterpolationDelayConformanceTests
{
    /// <summary>TF2's tick interval: 66.67 a second, and every demo in the corpus uses it.</summary>
    private const double Tf2Interval = 0.015d;

    [Test]
    public void DelayTicks_AtTf2sRateAndDefaultInterp_IsEight()
    {
        // TIME_TO_TICKS(0.1) = (int)(0.5 + 0.1/0.015) = (int)7.166 = 7, then + 1.
        ScenePropTrack.DelayTicksFor(Tf2Interval, interpolation: 0.1d).ShouldBe(8);
    }

    /// <remarks>
    /// **The rounding is to NEAREST, not down** — `(int)( 0.5f + dt / TICK_INTERVAL )` — so an
    /// interp amount that lands just under a tick boundary still rounds up before the `+ 1`.
    /// Truncating instead would be a tick short here and correct at 0.1, which is exactly the kind
    /// of input where a wrong reading passes its own test.
    /// </remarks>
    [Test]
    public void DelayTicks_WhenTheInterpRoundsUp_TakesTheNearestTick()
    {
        // 0.0975 / 0.015 = 6.5 exactly, so 0.5 + 6.5 = 7.0 -> 7, + 1 = 8.
        ScenePropTrack.DelayTicksFor(Tf2Interval, interpolation: 0.0975d).ShouldBe(8);

        // 0.09 / 0.015 = 6.0, so 0.5 + 6.0 = 6.5 -> 6, + 1 = 7. Truncation would agree here and
        // disagree above, which is why both cases are asserted.
        ScenePropTrack.DelayTicksFor(Tf2Interval, interpolation: 0.09d).ShouldBe(7);
    }

    /// <remarks>
    /// **A competitive config is the case this must not get wrong.** `cl_interp 0` with
    /// `cl_interp_ratio 2` at a 66 update rate gives 2/66 = 0.0303 s, which is two ticks plus the
    /// engine's one — a player who sets that is drawn much closer to the present, and a viewer
    /// hardcoding 0.1 would place them four ticks late.
    /// </remarks>
    [Test]
    public void DelayTicks_WithACompetitiveInterp_IsThree()
    {
        // 0.0303 / 0.015 = 2.02, so 0.5 + 2.02 = 2.52 -> 2, + 1 = 3.
        ScenePropTrack.DelayTicksFor(Tf2Interval, interpolation: 2d / 66d).ShouldBe(3);
    }

    /// <remarks>
    /// **The tick rate is an input, not a constant**, which is the half of this that TF2 itself
    /// cannot exercise: every demo in the corpus is 66.67, so a 33-tick recording is the only
    /// thing that would tell the two apart. Asserted anyway, because the arithmetic is the
    /// engine's and a viewer given such a demo should not silently double its delay.
    /// </remarks>
    [Test]
    public void DelayTicks_OnAThirtyThreeTickServer_IsFewerTicksForTheSameTime()
    {
        // 0.1 / 0.03 = 3.33, so 0.5 + 3.33 = 3.83 -> 3, + 1 = 4.
        ScenePropTrack.DelayTicksFor(0.03d, interpolation: 0.1d).ShouldBe(4);

        // The same wall-clock delay either way, which is the point of deriving from the interval:
        // eight ticks at 0.015 and four at 0.03 are both about an eighth of a second.
        (8 * Tf2Interval).ShouldBe(0.12d, 0.0001d);
        (4 * 0.03d).ShouldBe(0.12d, 0.0001d);
    }

    /// <remarks>
    /// A demo that states no interval must not produce a nonsense delay: the header is not always
    /// trustworthy (`docs/memory/a-header-written-last-is-absent.md`), so a zero or negative
    /// interval falls back to TF2's rate rather than dividing by it.
    /// </remarks>
    [Test]
    public void DelayTicks_WithNoStatedInterval_FallsBackToTf2sRate()
    {
        ScenePropTrack.DelayTicksFor(0d, interpolation: 0.1d).ShouldBe(8);
        ScenePropTrack.DelayTicksFor(-1d, interpolation: 0.1d).ShouldBe(8);
    }
}
