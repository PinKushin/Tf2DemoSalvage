using System;
using System.Threading;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>The engine's queued material system (`mat_queue_mode`), as `materialsystem.dll` has it (B581, D228).</summary>
/// <remarks>
/// **Written before the queue existed**, citing what the engine does rather than what was built:
///
/// - the three thread modes, `public/materialsystem/imaterialsystem.h:225-230`:
///   `MATERIAL_SINGLE_THREADED, MATERIAL_QUEUED_SINGLE_THREADED, MATERIAL_QUEUED_THREADED`;
/// - the cvar's help text, read from TF2's `bin/x64/materialsystem.dll` (finding 98 section 1);
/// - how `CMaterialSystem::EndFrame` (`0x180014bc0`) maps the cvar to a mode, and how `AllowThreading`
///   (`0x180010f90`) sets the default from `m_nPhysicalProcessors` (finding 99 section 1);
/// - one `MatQueue` thread (`0x1800130f0`), and a `Lock` (`0x180018000`) that waits for the in-flight job
///   (finding 99 sections 2 and 5).
/// </remarks>
public sealed class MaterialQueueConformanceTests
{
    [Test]
    public void ThreadMode_Values_AreTheEnginesEnumOrder()
    {
        ((int)MaterialThreadMode.SingleThreaded).ShouldBe(0);
        ((int)MaterialThreadMode.QueuedSingleThreaded).ShouldBe(1);
        ((int)MaterialThreadMode.QueuedThreaded).ShouldBe(2);
    }

    [Test]
    public void HelpText_IsTheShippedRegistrations() =>
        MaterialQueueMode.HelpText.ShouldBe(
            "The queue/thread mode the material system should use: -1=default, 0=synchronous single thread, 2=queued multithreaded");

    [Test]
    public void Command_IsMatQueueMode() => MaterialQueueMode.Command.ShouldBe("mat_queue_mode");

    /// <remarks>`EndFrame`: -2 → 2, 1 → 0 (queued-single is never used on PC), 0 and 2 pass through.</remarks>
    [TestCase(-2, 1, MaterialThreadMode.QueuedThreaded)]
    [TestCase(1, 8, MaterialThreadMode.SingleThreaded)]
    [TestCase(0, 8, MaterialThreadMode.SingleThreaded)]
    [TestCase(2, 8, MaterialThreadMode.QueuedThreaded)]
    [TestCase(2, 1, MaterialThreadMode.QueuedThreaded)]
    public void Resolve_AnExplicitValue_IsEndFramesMapping(int cvar, int physicalCores, MaterialThreadMode expected) =>
        MaterialQueueMode.Resolve(cvar, physicalCores).ShouldBe(expected);

    /// <remarks>Any other negative value is the default `AllowThreading` stored: threaded on 2+ PHYSICAL cores.</remarks>
    [TestCase(-1, 1, MaterialThreadMode.SingleThreaded)]
    [TestCase(-1, 2, MaterialThreadMode.QueuedThreaded)]
    [TestCase(-7, 16, MaterialThreadMode.QueuedThreaded)]
    public void Resolve_TheDefault_IsThreadedOnTwoPhysicalCores(int cvar, int physicalCores, MaterialThreadMode expected) =>
        MaterialQueueMode.Resolve(cvar, physicalCores).ShouldBe(expected);

    [Test]
    public void Submit_InModeZero_RunsOnTheCallingThread()
    {
        using QueuedRenderThread queue = new() { Mode = MaterialThreadMode.SingleThreaded };
        int ranOn = -1;

        queue.Submit(() => ranOn = Environment.CurrentManagedThreadId);

        ranOn.ShouldBe(Environment.CurrentManagedThreadId);
    }

    [Test]
    public void Submit_InModeTwo_RunsOnTheQueuesOneThread()
    {
        using QueuedRenderThread queue = new() { Mode = MaterialThreadMode.QueuedThreaded };
        int first = -1;
        int second = -1;

        queue.Submit(() => first = Environment.CurrentManagedThreadId);
        queue.Lock();
        queue.Submit(() => second = Environment.CurrentManagedThreadId);
        queue.Lock();

        first.ShouldNotBe(Environment.CurrentManagedThreadId);
        first.ShouldBe(queue.RenderThreadId);
        second.ShouldBe(first);
    }

    /// <remarks>`Lock` is a full drain: it does not return while the in-flight frame is still being drawn.</remarks>
    [Test]
    public void Lock_WithAFrameInFlight_ReturnsOnlyAfterItFinished()
    {
        using QueuedRenderThread queue = new() { Mode = MaterialThreadMode.QueuedThreaded };
        using ManualResetEventSlim gate = new();
        bool finished = false;

        queue.Waiting = _ => gate.Set();
        queue.Submit(() =>
        {
            gate.Wait();
            finished = true;
        });

        queue.Lock();

        finished.ShouldBeTrue();
        queue.InFlight.ShouldBeFalse();
    }

    /// <remarks>The engine's `EndFrame` waits on the previous job before handing off the next: one frame in flight.</remarks>
    [Test]
    public void Submit_WithAFrameInFlight_WaitsForItBeforeHandingOff()
    {
        using QueuedRenderThread queue = new() { Mode = MaterialThreadMode.QueuedThreaded };
        using ManualResetEventSlim gate = new();
        int order = 0;
        int firstEnded = 0;
        int secondBegan = 0;

        queue.Waiting = _ => gate.Set();
        queue.Submit(() =>
        {
            gate.Wait();
            firstEnded = Interlocked.Increment(ref order);
        });
        queue.Submit(() => secondBegan = Interlocked.Increment(ref order));
        queue.Lock();

        firstEnded.ShouldBe(1);
        secondBegan.ShouldBe(2);
    }

    /// <remarks>A screenshot reads back the swap chain, which the engine allows only in mode 0
    /// (`viewpostprocess.cpp:1479-1486`); here the read-back drains first, as `Lock` does.</remarks>
    [Test]
    public void Lock_CalledByName_ReportsTheCallerThatWaited()
    {
        using QueuedRenderThread queue = new() { Mode = MaterialThreadMode.QueuedThreaded };
        using ManualResetEventSlim gate = new();
        string? waited = null;

        queue.Waiting = caller =>
        {
            waited = caller;
            gate.Set();
        };
        queue.Submit(gate.Wait);

        queue.Lock("CaptureNextFrame");

        waited.ShouldBe("CaptureNextFrame");
        queue.TakeWaits().ShouldContain(wait => wait.Caller == "CaptureNextFrame" && wait.Ticks >= 0);
    }

    /// <remarks>Bounded, so a broken guard FAILS rather than hanging the run: the inner `Lock` would wait on its own
    /// frame for ever. Not disposed on failure, since `Dispose` drains and would hang the same way; the thread is a
    /// background one and does not hold the process.</remarks>
    [Test]
    public void Lock_OnTheRenderThread_DoesNotWaitForItself()
    {
        QueuedRenderThread queue = new() { Mode = MaterialThreadMode.QueuedThreaded };
        using ManualResetEventSlim returned = new();

        queue.Submit(() =>
        {
            queue.Lock();
            returned.Set();
        });

        returned.Wait(TimeSpan.FromSeconds(10)).ShouldBeTrue();
        queue.Dispose();
    }

    /// <remarks>A draw that throws must not kill the thread and leave the game thread waiting for ever.</remarks>
    [Test]
    public void Lock_AfterADrawThrew_RethrowsOnTheGameThread()
    {
        using QueuedRenderThread queue = new() { Mode = MaterialThreadMode.QueuedThreaded };

        queue.Submit(static () => throw new InvalidOperationException("drawn badly"));

        Should.Throw<InvalidOperationException>(() => queue.Lock()).Message.ShouldBe("drawn badly");
        queue.Lock();
    }

    /// <remarks>Leaving mode 2 drains first (`0x180016050`, "Forcing queued mode off!").</remarks>
    [Test]
    public void Mode_SetToZeroWithAFrameInFlight_DrainsFirst()
    {
        using QueuedRenderThread queue = new() { Mode = MaterialThreadMode.QueuedThreaded };
        using ManualResetEventSlim gate = new();
        bool finished = false;

        queue.Waiting = _ => gate.Set();
        queue.Submit(() =>
        {
            gate.Wait();
            finished = true;
        });

        queue.Mode = MaterialThreadMode.SingleThreaded;

        finished.ShouldBeTrue();
    }

    [Test]
    public void Dispose_WithAFrameInFlight_DrainsFirst()
    {
        QueuedRenderThread queue = new() { Mode = MaterialThreadMode.QueuedThreaded };
        using ManualResetEventSlim gate = new();
        bool finished = false;

        queue.Waiting = _ => gate.Set();
        queue.Submit(() =>
        {
            gate.Wait();
            finished = true;
        });

        queue.Dispose();

        finished.ShouldBeTrue();
    }

    /// <remarks>The render thread's own time, carried from where it ran, for `--measure`'s column.</remarks>
    [Test]
    public void TakeRenderTicks_AfterAQueuedDraw_IsPositiveThenZero()
    {
        using QueuedRenderThread queue = new() { Mode = MaterialThreadMode.QueuedThreaded };

        queue.Submit(static () => Thread.SpinWait(1000));
        queue.Lock();

        queue.TakeRenderTicks().ShouldBeGreaterThan(0);
        queue.TakeRenderTicks().ShouldBe(0);
    }
}
