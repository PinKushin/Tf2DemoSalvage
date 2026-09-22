using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `CL_QueueEvent`, read out of `engine.dll` (`0x1801f9bc0`): a temp entity fires its own delay plus
/// `GetClientInterpAmount()` after it arrives, when a demo is playing back (B415).
/// </summary>
public sealed class TempEntityFireTickConformanceTests
{
    [Test]
    public void QueueEvent_AnEffectWithNoDelay_IsOneInterpolationWindowLate()
    {
        // 0.1 s at 0.015 s a tick is 6.67 ticks, and an event fires on the first tick at or past its time: 7.
        DemoTimeline.FireTick(100, 0f, 0.015d).ShouldBe(107);
    }

    [Test]
    public void QueueEvent_AnEffectWithItsOwnDelay_AddsItToTheWindow()
    {
        // 0.05 s of its own plus 0.1 s is exactly ten ticks, which fires ON the tenth.
        DemoTimeline.FireTick(100, 0.05f, 0.015d).ShouldBe(110);
    }
}
