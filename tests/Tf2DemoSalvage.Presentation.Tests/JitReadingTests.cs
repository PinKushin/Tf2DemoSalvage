using System;

namespace Tf2DemoSalvage.Presentation.Tests;

[TestFixture]
public sealed class JitReadingTests
{
    [Test]
    public void Since_TwoReadings_GivesMethodsCompiledAndTimeSpentCompiling()
    {
        JitReading start = new(Methods: 1000, Compiling: TimeSpan.FromMilliseconds(250));
        JitReading end = new(Methods: 1342, Compiling: TimeSpan.FromMilliseconds(1250.5));

        end.Since(start).ShouldBe("jit: 342 methods compiled, 1000.5 ms compiling");
    }

    /// <remarks>
    /// **The process-wide count always moves on a parallel host** (review), so "it moved" proves nothing. The control is this
    /// thread's own count: whatever it compiled between the readings, the process-wide reading must include.
    /// </remarks>
    [Test]
    public void Now_AfterThisThreadCompilesAMethod_CountsAtLeastWhatThisThreadCompiled()
    {
        // The process window [before, after] holds the thread's window inside it, so whatever the thread counted is counted.
        JitReading before = JitReading.Now();
        long threadBefore = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true);
        Func<int, int> fresh = static x => x * 3 + 1;
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareDelegate(fresh);
        _ = fresh(2);
        long threadCompiled = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: true) - threadBefore;
        JitReading after = JitReading.Now();

        threadCompiled.ShouldBeGreaterThan(0, "the control: this thread compiled the lambda");
        (after.Methods - before.Methods).ShouldBeGreaterThanOrEqualTo(threadCompiled);
    }
}
