using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The burn clock, reconstructed from the tick <c>TF_COND_BURNING</c> turns on (B336).
/// </summary>
/// <remarks>
/// **This is the assertion B336 shipped without, and the corpus cannot supply it.**
/// `m_flBurnEffectStartTime` is set CLIENT-SIDE by `CTFPlayerShared::OnAddBurning` to
/// `gpGlobals->curtime` when the bit is ADDED (`tf_player_shared.cpp:7306`) and cleared by
/// `OnRemoveBurning` (`:6884`); nothing networks it. So the timeline watches the transition, and
/// testing that needs a recording where the transition tick is KNOWN.
///
/// **Every demo that contains burning is on a map this machine does not have** — `pl_upward_f12`
/// and `cp_steel_f12` — and every demo whose map is installed is a 6s match with no pyro:
/// `cp_process_f12` has zero burning player-frames out of 1,380,278. So the specimen is authored,
/// which is what D38 asks for anyway: the test puts the transition where it wants it and predicts
/// the clock exactly, where a corpus test could only compare two readings of the same file.
///
/// **`OnAddBurning` only starts the clock `if ( !m_pOuter->m_pBurningEffect )`** — a re-ignite
/// while already alight does NOT restart it — and that is why the clock on a real demo runs past
/// the flame's ten-second life. `pl_upward_f12` carries clocks to 12.27 seconds.
/// </remarks>
public sealed class BurnClockTests
{
    /// <summary>Seconds per tick, chosen so the arithmetic below is exact.</summary>
    private const float Interval = 0.015f;

    /// <summary><c>1 &lt;&lt; TF_COND_BURNING</c>.</summary>
    private const int Burning = 1 << PlayerConditions.Burning;

    [Test]
    public void BurningFor_TheTickTheConditionTurnsOn_IsZero()
    {
        float?[] clocks = Clocks((100, 0), (110, Burning));

        clocks[0].ShouldBeNull("not alight on the first snapshot");
        Running(clocks, 1).ShouldBe(
            0f, 1e-4f, "the transition tick is the start, so nothing has elapsed");
    }

    /// <remarks>
    /// **Measured from the transition, not from the frame** — which is the whole point of watching
    /// the bit rather than reading a field. Ten ticks at 0.015 s is 0.15 seconds.
    /// </remarks>
    [Test]
    public void BurningFor_TicksAfterTheTransition_CountsFromIt()
    {
        float?[] clocks = Clocks((100, 0), (110, Burning), (120, Burning), (130, Burning));

        Running(clocks, 1).ShouldBe(0f, 1e-4f);
        Running(clocks, 2).ShouldBe(0.15f, 1e-4f, "ten ticks at 0.015");
        Running(clocks, 3).ShouldBe(0.30f, 1e-4f, "twenty");
    }

    /// <remarks>
    /// **The clock RESETS when the condition clears**, which is `OnRemoveBurning` setting the start
    /// time to 0 — so a second burn is a new clock rather than a continuation. Without this a
    /// player burned twice in a match reads as one very long burn, and the proxy clamps it to zero:
    /// they would draw unburnt the second time.
    /// </remarks>
    [Test]
    public void BurningFor_ASecondBurnAfterTheFirstEnds_StartsAgain()
    {
        float?[] clocks = Clocks(
            (100, Burning), (110, Burning), (120, 0), (130, Burning), (140, Burning));

        Running(clocks, 1).ShouldBe(0.15f, 1e-4f, "still the first burn");
        clocks[2].ShouldBeNull("the condition cleared");
        Running(clocks, 3).ShouldBe(0f, 1e-4f, "a new burn, so a new clock");
        Running(clocks, 4).ShouldBe(
            0.15f, 1e-4f, "counting from the SECOND transition, not the first");
    }

    /// <remarks>
    /// **A re-ignite while already alight does NOT restart it**, which is `OnAddBurning`'s
    /// `if ( !m_pOuter->m_pBurningEffect )`. The bit staying set across a re-ignite is
    /// indistinguishable on the wire from it simply staying set, so this asserts the case that
    /// makes real clocks exceed the flame's life.
    /// </remarks>
    [Test]
    public void BurningFor_TheConditionHeldThroughout_KeepsRunningPastTheFlamesLife()
    {
        // 800 ticks at 0.015 is twelve seconds — past TF_BURNING_FLAME_LIFE, as pl_upward_f12's
        // 12.27-second maximum really is.
        float?[] clocks = Clocks((100, Burning), (900, Burning));

        Running(clocks, 1).ShouldBe(12f, 1e-3f, "the clock does not stop at ten; the PROXY clamps");
    }

    /// <remarks>
    /// **The control on the whole mechanism**: a player who never catches fire has no clock at all,
    /// so every assertion above is about the condition rather than about the passage of time.
    /// </remarks>
    [Test]
    public void BurningFor_APlayerWhoNeverCatchesFire_IsNullThroughout()
    {
        Clocks((100, 0), (110, 0), (120, 0)).ShouldAllBe(clock => clock == null);
    }

    /// <remarks>
    /// **A different condition does not start it**, which is what a mask written as a comparison
    /// would get wrong: `TF_COND_URINE` is bit 24 and burning is bit 22, and an `!= 0` test on the
    /// whole word would call either one burning.
    /// </remarks>
    [Test]
    public void BurningFor_APlayerInADifferentCondition_IsNull()
    {
        Clocks((100, 1 << PlayerConditions.Urine), (110, 1 << PlayerConditions.Zoomed))
            .ShouldAllBe(clock => clock == null);
    }

    /// <summary>One snapshot's clock, asserted to be running first.</summary>
    /// <remarks>
    /// **Null is asserted separately from the value**, rather than compared with a tolerance: a
    /// clock that is absent and one that is zero are different facts — the first means the player
    /// is not alight and the second means they just caught fire — and a comparison that treated
    /// null as zero would make the transition invisible.
    /// </remarks>
    private static float Running(float?[] clocks, int at)
    {
        clocks[at].ShouldNotBeNull($"the clock at snapshot {at} should be running");

        return clocks[at]!.Value;
    }

    /// <summary>The burn clock at each snapshot of an authored demo.</summary>
    private static float?[] Clocks(params (int Tick, int Conditions)[] states) =>
        [
            .. DemoTimeline
                .Build(SyntheticPlayer.DemoOfConditionsOverTicks(Interval, states))
                .Frames
                .Where(frame => frame.Players.Count > 0)
                .Select(frame => frame.Players[0].BurningFor),
        ];
}
