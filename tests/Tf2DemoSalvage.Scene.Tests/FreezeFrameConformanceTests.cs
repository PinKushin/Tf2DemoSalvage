namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The engine's freeze frame, <c>CViewRender::FreezeFrame</c> and <c>RenderView</c>'s branch (B738).</summary>
/// <remarks><c>viewrender.cpp:1989-2017</c>, <c>:2059</c>, <c>:2218-2240</c>; every time is <c>gpGlobals->curtime</c>.</remarks>
public sealed class FreezeFrameConformanceTests
{
    [Test]
    public void Next_TheFrameFreezeFrameWasCalledOn_TakesThenHoldsUntilTheTimeIsUp()
    {
        FreezeFrame freeze = new();

        freeze.Freeze(4f, curTime: 10d);

        freeze.Next(10d).ShouldBe(FreezeFrameDraw.Take, "the frame that called it is drawn and copied");
        freeze.Next(10.1d).ShouldBe(FreezeFrameDraw.Hold);
        freeze.Next(13.99d).ShouldBe(FreezeFrameDraw.Hold);
        freeze.Next(14d).ShouldBe(FreezeFrameDraw.Normal, "until > curtime, strictly");
    }

    [Test]
    public void Next_WithNoFreezeFrameCalled_IsNormal()
    {
        new FreezeFrame().Next(10d).ShouldBe(FreezeFrameDraw.Normal);
    }

    [Test]
    public void Freeze_CalledAgainWhileHolding_ExtendsTheHoldAndTakesNoNewFrame()
    {
        FreezeFrame freeze = new();
        freeze.Freeze(4f, curTime: 10d);
        freeze.Next(10d);

        freeze.Freeze(3f, curTime: 12d);

        freeze.Next(12d).ShouldBe(FreezeFrameDraw.Hold, "a running freeze is lengthened, not retaken");
        freeze.Next(16.99d).ShouldBe(FreezeFrameDraw.Hold);
        freeze.Next(17d).ShouldBe(FreezeFrameDraw.Normal);
    }

    [Test]
    public void Release_WhileHolding_DrawsTheViewAgainAtOnce()
    {
        FreezeFrame freeze = new();
        freeze.Freeze(4f, curTime: 10d);
        freeze.Next(10d);

        freeze.Release();

        freeze.Next(11d).ShouldBe(FreezeFrameDraw.Normal);
    }

    [Test]
    public void Release_BeforeTheFrameIsTaken_TakesNothing()
    {
        FreezeFrame freeze = new();
        freeze.Freeze(4f, curTime: 10d);

        freeze.Release();

        freeze.Next(10d).ShouldBe(FreezeFrameDraw.Normal);
    }

    [Test]
    public void Next_WithTimeStoodStill_HoldsForAsLongAsItStands()
    {
        // A paused demo's curtime does not move, so the hold never runs out while paused.
        FreezeFrame freeze = new();
        freeze.Freeze(4f, curTime: 10d);
        freeze.Next(10d);

        for (int frame = 0; frame < 1000; frame++)
        {
            freeze.Next(11d).ShouldBe(FreezeFrameDraw.Hold);
        }
    }
}
