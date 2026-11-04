using System;
using System.Diagnostics;

namespace Tf2DemoSalvage.Presentation;

/// <summary>The one origin startup is timed from: the process's own start (B707).</summary>
public static class ProcessClock
{
    /// <summary>How long this process has been running.</summary>
    /// <returns>The time since it started.</returns>
    public static TimeSpan SinceStart()
    {
        using Process self = Process.GetCurrentProcess();

        return DateTime.UtcNow - self.StartTime.ToUniversalTime();
    }
}
