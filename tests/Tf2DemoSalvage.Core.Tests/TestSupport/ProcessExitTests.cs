using System;
using System.Collections.Generic;

using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Core.Tests.TestSupport;

/// <summary>
/// That a process asked to go is reported as gone only when it really has.
/// </summary>
/// <remarks>
/// **Written for a defect, not for coverage.** The UI suite killed a stray viewer, waited ten
/// seconds, dropped the wait's answer, and logged "killed a stray viewer" either way, so a viewer
/// still holding the desktop and <c>obj/</c> was reported dead. The process is replaced by two
/// delegates, so nothing here starts a process, sleeps, or needs a desktop.
/// </remarks>
public sealed class ProcessExitTests
{
    private const int Id = 4242;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Test]
    public void AwaitOrKill_ProcessExitsWithinTheWait_ReturnsExitedWithoutKilling()
    {
        List<TimeSpan> waits = [];
        int kills = 0;

        ProcessExitOutcome outcome = ProcessExit.AwaitOrKill(
            Id,
            wait => { waits.Add(wait); return true; },
            () => kills++,
            Timeout);

        outcome.ShouldBe(ProcessExitOutcome.Exited);
        kills.ShouldBe(0);
        waits.ShouldBe([Timeout]);
    }

    [Test]
    public void AwaitOrKill_ProcessStillRunningAfterTheWait_KillsItOnceAndReturnsKilled()
    {
        Queue<bool> answers = new([false, true]);
        int kills = 0;

        ProcessExitOutcome outcome = ProcessExit.AwaitOrKill(
            Id,
            _ => answers.Dequeue(),
            () => kills++,
            Timeout);

        outcome.ShouldBe(ProcessExitOutcome.Killed);
        kills.ShouldBe(1);
        answers.ShouldBeEmpty("the kill was not followed by a second wait");
    }

    [Test]
    public void AwaitOrKill_ProcessSurvivesTheKill_ThrowsTimeoutNamingTheProcessId()
    {
        int kills = 0;

        TimeoutException thrown = Should.Throw<TimeoutException>(() => ProcessExit.AwaitOrKill(
            Id,
            _ => false,
            () => kills++,
            Timeout));

        kills.ShouldBe(1);
        thrown.Message.ShouldContain("4242");
    }
}
