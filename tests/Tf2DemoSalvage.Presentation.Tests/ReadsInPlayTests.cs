using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>`--measure`'s main-thread read list (B595 part 6): armed at the window's start, disarmed by its report.</summary>
public sealed class ReadsInPlayTests
{
    [Test]
    public void Report_AfterAReadWhileArmed_WritesItThenDisarms()
    {
        ReadsInPlay reads = new();
        List<string> written = [];

        try
        {
            reads.Arm();
            GameArchives.ThreadReadObserver.ShouldNotBeNull("the control: arming installs the observer")("a.vmt", 0);
            reads.Report(written.Add);

            written.ShouldHaveSingleItem().ShouldStartWith("    read in play: a.vmt 0.00 ms <- ");
            GameArchives.ThreadReadObserver.ShouldBeNull("left armed, every later read keeps a stack trace nobody prints");

            reads.Report(written.Add);
            written.Count.ShouldBe(1, "a report forgets what it wrote");
        }
        finally
        {
            GameArchives.ThreadReadObserver = null;
        }
    }
}
