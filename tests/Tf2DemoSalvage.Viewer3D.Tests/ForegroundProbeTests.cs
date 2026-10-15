using System;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>Whether the APPLICATION is active, as `snd_mute_losefocus` asks — not whether a child has keyboard focus.</summary>
public sealed class ForegroundProbeTests
{
    [Test]
    public void IsOurs_AForegroundWindowOfThisProcess_IsActive() =>
        ForegroundProbe.IsOurs((uint)Environment.ProcessId).ShouldBeTrue("one of our own dialogs keeps the application active");

    [Test]
    public void IsOurs_AnotherProcessOrNone_IsNotActive()
    {
        ForegroundProbe.IsOurs((uint)Environment.ProcessId + 1).ShouldBeFalse();
        ForegroundProbe.IsOurs(0).ShouldBeFalse("no foreground window at all");
    }
}
