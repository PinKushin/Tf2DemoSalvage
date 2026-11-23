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

    /// <summary>
    /// The pin each assembly runs: the settings file is a temp file **named for that assembly**, never the owner's — so a
    /// variable another assembly left set in a shared runner cannot pass it (review 33, Grok).
    /// </summary>
    /// <param name="policy">The assembly's own `SettingsFilePolicy` type.</param>
    public static void ShouldBeRedirectedFor(Type policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        string owners = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tf2DemoSalvage", "settings.cfg");
        string path = Scene.ViewerSettings.Path;

        path.ShouldNotBe(owners);
        Path.GetDirectoryName(path).ShouldBe(Folder);
        Path.GetFileName(path).ShouldStartWith(policy.Assembly.GetName().Name + "-");
    }

    /// <summary>The file this run redirected to, or null before the set-up.</summary>
    private string? _created;

    /// <summary>The variable's value before the redirect, restored after.</summary>
    private string? _before;

    /// <summary>Points the settings file at a fresh temp file for this run, remembering what it replaced.</summary>
    [OneTimeSetUp]
    public void RedirectTheSettingsFile()
    {
        _before = Environment.GetEnvironmentVariable(Scene.ViewerSettings.PathVariable);
        _created = Path.Combine(Folder, GetType().Assembly.GetName().Name + "-" + Guid.NewGuid().ToString("N") + ".cfg");
        Environment.SetEnvironmentVariable(Scene.ViewerSettings.PathVariable, _created);
    }

    /// <summary>Deletes the file this run created, if a test wrote it, and puts the variable back as it was.</summary>
    /// <remarks>
    /// **This run's file, not whatever the variable names now, and the prior value restored** (review 33 item 6; review 32,
    /// Gemini): the variable is process-wide, so a runner hosting assemblies one after another would otherwise hand the next
    /// one a path that no longer exists, and a test that moved the variable would have its file deleted.
    /// </remarks>
    [OneTimeTearDown]
    public void DeleteTheRunsSettingsFile()
    {
        if (_created is { } file && File.Exists(file))
        {
            File.Delete(file);
        }

        Environment.SetEnvironmentVariable(Scene.ViewerSettings.PathVariable, _before);
    }
}
