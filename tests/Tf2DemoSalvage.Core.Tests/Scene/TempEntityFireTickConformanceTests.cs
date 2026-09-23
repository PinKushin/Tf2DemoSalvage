using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `CL_QueueEvent`, read out of `engine.dll` (`0x1801f9bc0`): a temp entity fires its own delay plus
/// `GetClientInterpAmount()` after it arrives, when a demo is playing back (B415).
/// </summary>
/// <remarks>
/// **It fires during the tick its time falls in, not on the next whole tick.** `CClientState::GetTime`
/// (`0x1800a3020`) is `tickcount * interval + m_tickRemainder` outside simulation, so `CL_FireEvents` compares
/// against a clock that runs between ticks. The tick the client is on when it fires is the floor.
/// </remarks>
public sealed class TempEntityFireTickConformanceTests
{
    [Test]
    public void QueueEvent_AnEffectWithNoDelay_FiresInsideTheTickItsWindowEndsIn()
    {
        // 0.1 s at 0.015 s a tick is 6.67 ticks: due partway through tick 106.
        DemoTimeline.FireTick(100, 0f, 0.015d).ShouldBe(106);
    }

    [Test]
    public void QueueEvent_AnEffectWithItsOwnDelay_AddsItToTheWindow()
    {
        // 0.05 s of its own plus 0.1 s is exactly ten ticks, which fires ON the tenth.
        DemoTimeline.FireTick(100, 0.05f, 0.015d).ShouldBe(110);
    }

    /// <remarks>
    /// f12, the owner's 0 / 1 / 66: the scattergun's `CTEFireBullets` arrived in packet 13848 and TF2 played its
    /// impact at 13849. 1/66 s is 1.01 ticks.
    /// </remarks>
    [Test]
    public void QueueEvent_AtACompetitiveInterp_FiresOnTheNextTick()
    {
        DemoTimeline.FireTick(13848, 0f, 0.015d, 1d / 66d).ShouldBe(13849);
    }
}
