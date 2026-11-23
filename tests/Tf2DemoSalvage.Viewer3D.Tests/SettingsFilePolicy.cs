using System;
using System.IO;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>Points the settings file at a temp file before any test in this assembly runs.</summary>
/// <remarks>
/// **A `MainForm` built here reads no owner's settings** (B779 gate). `new MainForm()` loads
/// <see cref="ViewerSettings.Path"/>, the user's own `settings.cfg`, so `HudMenu_WithNoHudsInstalled_HasOnlyTf2Default`
/// failed on a machine whose file chose a HUD — a file Presentation tests had written (`SettingsFilePolicy` there). The
/// UI tests already move the file per launch (<see cref="ViewerSettings.PathVariable"/>, D210); this does the same for
/// the whole assembly, so these tests read a file that starts absent: the defaults.
/// </remarks>
[SetUpFixture]
public sealed class SettingsFilePolicy
{
    /// <summary>Moves the settings file to a fresh temp file for this run.</summary>
    [OneTimeSetUp]
    public void UseATempSettingsFile() =>
        Environment.SetEnvironmentVariable(
            ViewerSettings.PathVariable,
            Path.Combine(Path.GetTempPath(), "tf2salvage-tests", "viewer3d-" + Guid.NewGuid().ToString("N") + ".cfg"));
}
