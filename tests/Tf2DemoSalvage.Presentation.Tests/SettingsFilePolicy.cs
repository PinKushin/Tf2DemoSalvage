using System;
using System.IO;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>Points the settings file at a temp file before any test in this assembly runs.</summary>
/// <remarks>
/// **No unit test reads or writes the owner's settings file** (B779 gate). A `SettingsPresenter` built without a path
/// saves to <see cref="ViewerSettings.Path"/> — the user's own `settings.cfg` under LocalApplicationData — and B779's
/// presenter rows that chose a HUD did exactly that: they overwrote the owner's file with a test's record and
/// `hud_chosen "custom/after"`, which a later `MainForm` in `Viewer3D.Tests` read and failed on. The UI tests already move
/// the file per launch (<see cref="ViewerSettings.PathVariable"/>, D210); this does the same for the whole assembly, so
/// a test that forgets a path writes here and not there. Per assembly, as `ALSOFT_DRIVERS` is in `Audio.Tests`: a
/// process-wide variable set by one fixture would leave the others' order to decide.
/// </remarks>
[SetUpFixture]
public sealed class SettingsFilePolicy
{
    /// <summary>Moves the settings file to a fresh temp file for this run.</summary>
    [OneTimeSetUp]
    public void UseATempSettingsFile() =>
        Environment.SetEnvironmentVariable(
            ViewerSettings.PathVariable,
            Path.Combine(Path.GetTempPath(), "tf2salvage-tests", "presentation-" + Guid.NewGuid().ToString("N") + ".cfg"));
}
