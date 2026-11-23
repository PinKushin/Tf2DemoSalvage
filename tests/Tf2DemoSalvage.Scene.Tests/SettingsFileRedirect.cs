using System;
using System.IO;

namespace Tf2DemoSalvage.TestSettings;

/// <summary>
/// Moves the viewer's settings file to a temp file for a whole test assembly, and deletes it after (B779 gate, review 32).
/// </summary>
/// <remarks>
/// **No test writes the owner's settings file.** A `SettingsPresenter` built without a path saves to
/// `ViewerSettings.Path`, the user's own `settings.cfg` under LocalApplicationData, and `new MainForm()` reads it. B779's
/// presenter rows that chose a HUD overwrote the owner's file with a test's record, and a later `MainForm` test failed on
/// it. The UI tests move the file per launch (`ViewerSettings.PathVariable`, D210). This does the same for every unit test
/// assembly that can reach `ViewerSettings`, through the same variable.
///
/// **One file, linked into each assembly** (as the shared fixtures here are), with a two-line `[SetUpFixture]` subclass in
/// each assembly's root namespace, because a set-up fixture covers only its own assembly and namespace. That subclass
/// sits beside a test pinning that `ViewerSettings.Path` is the redirected file, so deleting the subclass reddens it.
///
/// Per run, not per test: a test that saves must still give its own path, as `ViewerPresenterTests` does, or its save
/// is seen by the tests after it in the same run.
/// </remarks>
public abstract class SettingsFileRedirect
{
    /// <summary>The folder every run's settings file lives in.</summary>
    public static string Folder => Path.Combine(Path.GetTempPath(), "tf2salvage-tests");

    /// <summary>Points the settings file at a fresh temp file for this run.</summary>
    [OneTimeSetUp]
    public void RedirectTheSettingsFile() =>
        Environment.SetEnvironmentVariable(
            Scene.ViewerSettings.PathVariable,
            Path.Combine(Folder, GetType().Assembly.GetName().Name + "-" + Guid.NewGuid().ToString("N") + ".cfg"));

    /// <summary>Deletes this run's settings file, if a test wrote one, and lifts the redirect.</summary>
    /// <remarks>
    /// **The variable is cleared too** (review 32, Gemini): it is process-wide, and a runner that hosts assemblies one after
    /// another would otherwise hand the next one a path that no longer exists.
    /// </remarks>
    [OneTimeTearDown]
    public void DeleteTheRunsSettingsFile()
    {
        if (Environment.GetEnvironmentVariable(Scene.ViewerSettings.PathVariable) is { Length: > 0 } file && File.Exists(file))
        {
            File.Delete(file);
        }

        Environment.SetEnvironmentVariable(Scene.ViewerSettings.PathVariable, null);
    }
}
