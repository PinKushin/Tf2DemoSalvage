using System;
using System.Diagnostics;
using System.Globalization;

namespace Tf2DemoSalvage.SdkReference;

/// <summary>How a process asked to go actually went.</summary>
public enum ProcessExitOutcome
{
    /// <summary>It was gone within the first wait.</summary>
    Exited,

    /// <summary>It was still running after the first wait, and went once its tree was killed.</summary>
    Killed,
}

/// <summary>
/// Waits for a process to exit, kills its tree if it has not, and never reports it gone when it is not.
/// </summary>
/// <remarks>
/// **`WaitForExit(timeout)` answers false when the process is still alive, and that answer was being
/// dropped.** The UI suite killed a stray viewer, waited, and logged "killed a stray viewer" whatever
/// the wait said, so a viewer still holding the desktop and <c>obj/</c> was reported dead and the next
/// run failed with no explanation. This type is the one place that reads the answer.
///
/// **A survivor throws <see cref="TimeoutException"/>, deliberately not
/// <see cref="InvalidOperationException"/>**: callers already catch that one to mean "it exited
/// between being listed and being killed", which would swallow exactly the case this exists to
/// report.
/// </remarks>
public static class ProcessExit
{
    /// <summary>Waits for <paramref name="process"/>, killing its tree if it outlives the wait.</summary>
    /// <param name="process">The process asked to go.</param>
    /// <param name="timeout">How long each of the two waits may take.</param>
    /// <returns>Whether it exited by itself or had to be killed.</returns>
    /// <exception cref="TimeoutException">It was still running after the kill and a second wait.</exception>
    public static ProcessExitOutcome AwaitOrKill(Process process, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(process);

        return AwaitOrKill(process.Id, process.WaitForExit, () => process.Kill(entireProcessTree: true), timeout);
    }

    /// <summary>The same, with the process reduced to its two operations so it can be tested without one.</summary>
    /// <param name="processId">The id, for the message when it will not go.</param>
    /// <param name="waitForExit">Waits up to the given time; true when the process has exited.</param>
    /// <param name="killTree">Kills the process and everything it started.</param>
    /// <param name="timeout">How long each of the two waits may take.</param>
    /// <returns>Whether it exited by itself or had to be killed.</returns>
    /// <exception cref="TimeoutException">It was still running after the kill and a second wait.</exception>
    public static ProcessExitOutcome AwaitOrKill(
        int processId, Func<TimeSpan, bool> waitForExit, Action killTree, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(waitForExit);
        ArgumentNullException.ThrowIfNull(killTree);

        if (waitForExit(timeout))
        {
            return ProcessExitOutcome.Exited;
        }

        killTree();

        if (waitForExit(timeout))
        {
            return ProcessExitOutcome.Killed;
        }

        throw new TimeoutException(string.Create(
            CultureInfo.InvariantCulture,
            $"process {processId} was still running {timeout.TotalSeconds:0} s after its tree was killed"));
    }
}
