using System.IO;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Preferences that survive a restart.
/// </summary>
/// <remarks>
/// **Reading is silent on failure and writing is not.** A settings file is a convenience, so a
/// missing or corrupt one must not stop the viewer opening a demo — but a preference that silently
/// fails to stick is worse than one that says so, because the user repeats the change forever.
/// </remarks>
public sealed class ViewerSettingsTests
{
    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2salvage-tests", Path.GetRandomFileName());
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void RemoveFolder()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Disposable temp folder; a lock must not fail an otherwise passing test.
        }
    }

    [Test]
    public void Load_NoFile_GivesBorderless()
    {
        // Borderless is the default because it always works: exclusive can be refused by DXGI.
        ViewerSettings.Load(Path.Combine(_folder, "absent.json"))
            .FullScreenMode.ShouldBe(FullScreenMode.Borderless);
    }

    [Test]
    public void SaveThenLoad_KeepsTheChoice()
    {
        string file = Path.Combine(_folder, "settings.json");

        new ViewerSettings { FullScreenMode = FullScreenMode.Exclusive }.Save(file).ShouldBeNull();

        ViewerSettings.Load(file).FullScreenMode.ShouldBe(FullScreenMode.Exclusive);
    }

    [Test]
    public void Save_CreatesTheFolder()
    {
        // First run on a new machine: nothing under LocalApplicationData exists yet.
        string file = Path.Combine(_folder, "nested", "deeper", "settings.json");

        new ViewerSettings { FullScreenMode = FullScreenMode.Exclusive }.Save(file).ShouldBeNull();

        File.Exists(file).ShouldBeTrue();
    }

    [Test]
    public void Load_CorruptFile_GivesDefaultsRatherThanThrowing()
    {
        string file = Path.Combine(_folder, "broken.json");
        File.WriteAllText(file, "{not json");

        ViewerSettings.Load(file).FullScreenMode.ShouldBe(FullScreenMode.Borderless);
    }

    [Test]
    public void Load_EmptyFile_GivesDefaults()
    {
        // Distinct from corrupt: a zero-length file is what a crash mid-write leaves behind, and
        // System.Text.Json treats it as an error rather than as an empty object.
        string file = Path.Combine(_folder, "empty.json");
        File.WriteAllText(file, string.Empty);

        ViewerSettings.Load(file).FullScreenMode.ShouldBe(FullScreenMode.Borderless);
    }

    [Test]
    public void Save_ToAnImpossiblePath_ReportsRatherThanThrows()
    {
        // The caller has the status line, so the failure is returned to it. Throwing here would
        // take down a menu click; swallowing would leave the user changing the setting forever.
        string? failure = new ViewerSettings().Save(Path.Combine(_folder, "\0bad", "settings.json"));

        failure.ShouldNotBeNullOrEmpty();
    }

    [Test]
    public void TheFormExposesAndChangesTheMode()
    {
        // End to end through the shell, since the menu items are what a user actually touches.
        using MainForm form = new();

        form.SetFullScreenMode(FullScreenMode.Exclusive);
        form.FullScreenMode.ShouldBe(FullScreenMode.Exclusive);

        form.SetFullScreenMode(FullScreenMode.Borderless);
        form.FullScreenMode.ShouldBe(FullScreenMode.Borderless);
    }
}
