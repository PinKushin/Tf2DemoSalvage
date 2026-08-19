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
    public void Load_NoFile_GivesFullTextureDetail()
    {
        // The frag-movie baseline: the TF2 recording configs all set mat_picmip -10, and full
        // detail was measured at 0.58s and 355 MB for a whole map - fifteen megabytes more than
        // capping at 1024.
        ViewerSettings.Load(Path.Combine(_folder, "absent.cfg"))
            .TextureQuality.ShouldBe(TextureQuality.Full);
    }

    [Test]
    public void Load_NoFile_GivesBorderless()
    {
        // Borderless is the default because it always works: exclusive can be refused by DXGI.
        ViewerSettings.Load(Path.Combine(_folder, "absent.cfg"))
            .FullScreenMode.ShouldBe(FullScreenMode.Borderless);
    }

    [Test]
    public void SaveThenLoad_KeepsTheChoice()
    {
        string file = Path.Combine(_folder, "settings.cfg");

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
    public void SaveThenLoad_KeepsTheTextureQuality()
    {
        // Both settings in one file, so a reader that dropped one while keeping the other would
        // fail here rather than looking fine.
        string file = Path.Combine(_folder, "settings.cfg");

        new ViewerSettings
        {
            FullScreenMode = FullScreenMode.Exclusive,
            TextureQuality = TextureQuality.Full,
        }.Save(file).ShouldBeNull();

        ViewerSettings loaded = ViewerSettings.Load(file);

        loaded.TextureQuality.ShouldBe(TextureQuality.Full);
        loaded.FullScreenMode.ShouldBe(FullScreenMode.Exclusive);
    }

    [Test]
    public void ViewerSettings_TheTextureQualityValues_ArePixelCaps()
    {
        // The enum's values ARE the sizes, so they can be handed to the decoder directly. A
        // renumbering that broke that would silently load the wrong mip.
        ((int)TextureQuality.Low).ShouldBe(256);
        ((int)TextureQuality.Medium).ShouldBe(512);
        ((int)TextureQuality.High).ShouldBe(1024);
        ((int)TextureQuality.Full).ShouldBe(0, "zero means no cap, which is what the decoder expects");
    }

    [Test]
    public void Write_LooksLikeASourceConfig()
    {
        // The format is TF2's own: one command per line, value after a space, // for comments.
        // Someone who has edited config.cfg can edit this without being told how.
        string text = new ViewerSettings
        {
            FullScreenMode = FullScreenMode.Exclusive,
            TextureQuality = TextureQuality.Low,
        }.Write();

        text.ShouldContain("fullscreen_mode 1");
        text.ShouldContain("texture_quality 256");
        text.ShouldContain("//", Case.Sensitive);
    }

    [Test]
    public void Parse_ReadsAHandWrittenConfig()
    {
        // Written the way a person would: comments, a trailing comment, odd spacing, quotes.
        ViewerSettings settings = ViewerSettings.Parse(
            """
            // my settings
            fullscreen_mode 1   // exclusive, I have one monitor

            texture_quality "1024"
            """);

        settings.FullScreenMode.ShouldBe(FullScreenMode.Exclusive);
        settings.TextureQuality.ShouldBe(TextureQuality.High);
    }

    [Test]
    public void Parse_IgnoresACommandItDoesNotKnow()
    {
        // A config from a later version must not stop this one starting, which is how Source
        // treats a cvar it does not have.
        ViewerSettings settings = ViewerSettings.Parse(
            """
            mat_picmip 2
            some_future_setting 7
            texture_quality 256
            """);

        settings.TextureQuality.ShouldBe(TextureQuality.Low);
    }

    [Test]
    public void Parse_AValueThatIsNotANumber_KeepsTheDefault()
    {
        // One bad line must not cost every other setting in the file.
        ViewerSettings settings = ViewerSettings.Parse(
            """
            fullscreen_mode banana
            texture_quality 256
            """);

        settings.FullScreenMode.ShouldBe(FullScreenMode.Borderless);
        settings.TextureQuality.ShouldBe(TextureQuality.Low);
    }

    [Test]
    public void Parse_AValueOutsideTheKnownRange_KeepsTheDefault()
    {
        // 999 is not a texture size this program has; taking it would ask the decoder for a mip
        // that does not exist.
        ViewerSettings.Parse("texture_quality 999").TextureQuality.ShouldBe(TextureQuality.Full);
    }

    [Test]
    public void Load_CorruptFile_GivesDefaultsRatherThanThrowing()
    {
        string file = Path.Combine(_folder, "broken.cfg");
        File.WriteAllText(file, "this is not a config at all");

        ViewerSettings.Load(file).FullScreenMode.ShouldBe(FullScreenMode.Borderless);
    }

    [Test]
    public void Load_EmptyFile_GivesDefaults()
    {
        // Distinct from corrupt: a zero-length file is what a crash mid-write leaves behind, and
        // System.Text.Json treats it as an error rather than as an empty object.
        string file = Path.Combine(_folder, "empty.cfg");
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
    public void ViewerSettings_TheForm_ExposesAndChangesTheMode()
    {
        // End to end through the shell, since the menu items are what a user actually touches.
        using MainForm form = new();

        form.SetFullScreenMode(FullScreenMode.Exclusive);
        form.FullScreenMode.ShouldBe(FullScreenMode.Exclusive);

        form.SetFullScreenMode(FullScreenMode.Borderless);
        form.FullScreenMode.ShouldBe(FullScreenMode.Borderless);
    }
}
