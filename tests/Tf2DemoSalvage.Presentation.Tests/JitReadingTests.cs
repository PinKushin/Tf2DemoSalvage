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

    [Test]
    public void Now_AfterCompilingAMethod_CountsAtLeastOneMore()
    {
        // The control that the instrument is live: a method compiled between two readings must show.
        JitReading before = JitReading.Now();
        Func<int, int> fresh = static x => x * 3 + 1;
        System.Runtime.CompilerServices.RuntimeHelpers.PrepareDelegate(fresh);
        _ = fresh(2);

        (JitReading.Now().Methods - before.Methods).ShouldBeGreaterThan(0);
    }
}
