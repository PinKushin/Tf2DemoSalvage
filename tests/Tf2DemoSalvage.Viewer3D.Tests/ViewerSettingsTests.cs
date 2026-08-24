using System.IO;
using System.Threading;

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
/// <remarks>STA and serial, because this constructs a Windows Form — see B178.</remarks>
[NonParallelizable]
[Apartment(ApartmentState.STA)]
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
    public void SaveThenLoad_KeepsTheViewmodelFieldOfView()
    {
        // **TF2 lets a player change this, so this viewer does too** — the standing rule in
        // docs/findings/13-settings-parity.md. It was nearly shipped as a constant off the back of
        // reading the SDK, which is the shape of miss that rule exists to catch: the number was
        // right and the choice was taken away.
        string file = Path.Combine(_folder, "settings.cfg");

        new ViewerSettings { ViewmodelFieldOfView = 68f }.Save(file).ShouldBeNull();

        ViewerSettings.Load(file).ViewmodelFieldOfView.ShouldBe(68f, 0.01f);
    }

    [Test]
    public void Save_ASettingStillAtItsDefault_IsWrittenCommentedOut()
    {
        // **A default written into a file stops being a default**, and that is a real defect rather
        // than untidiness. Every setting used to be written on the first run, so a config recorded
        // the program's opinions as though they were the user's — and changing a default afterwards
        // reached nobody who had ever run the viewer.
        //
        // Measured, on the owner's machine: the viewmodel field of view was changed from 54 to 70
        // and their config, written earlier, pinned 54. The change appeared to do nothing, and
        // nothing could distinguish "I chose 54" from "54 was written for me before you changed it".
        string file = Path.Combine(_folder, "settings.cfg");

        new ViewerSettings().Save(file).ShouldBeNull();

        string written = File.ReadAllText(file);

        written.ShouldContain(
            $"// {ViewerSettings.ViewmodelFieldOfViewCommand} ",
            Case.Sensitive,
            "a setting nobody chose must stay a default, so it is written as a comment");

        written.ShouldNotContain(
            $"\n{ViewerSettings.ViewmodelFieldOfViewCommand} ",
            Case.Sensitive,
            "an uncommented line would pin the value and stop a later default reaching this user");
    }

    [Test]
    public void Save_ASettingTheUserChose_IsWrittenActive()
    {
        // **The control, and without it the test above passes against "comment out everything".**
        // A chosen value has to survive, which is the whole point of the file.
        string file = Path.Combine(_folder, "settings.cfg");

        new ViewerSettings { ViewmodelFieldOfView = 54f }.Save(file).ShouldBeNull();

        File.ReadAllText(file).ShouldContain(
            $"\n{ViewerSettings.ViewmodelFieldOfViewCommand} 54",
            Case.Sensitive,
            "a value the user chose must be written so that it is read back");

        ViewerSettings.Load(file).ViewmodelFieldOfView.ShouldBe(54f, 0.01f);
    }

    // A third test lived here — save the defaults, reload, expect the default back — and it was
    // deleted after the sabotage check showed it CANNOT FAIL. Writing the value uncommented still
    // reads back the same number, because the default is the same on both sides of the round trip;
    // it only discriminates if the default changes between save and load, which is the one thing a
    // single run cannot arrange. The two tests above measure the actual variable, which is what the
    // file says rather than what a reload happens to produce.

    [Test]
    public void Load_AViewmodelFieldOfViewOutsideTheGamesRange_IsClamped()
    {
        // `ConVar v_viewmodel_fov( "viewmodel_fov", "54", ..., true, 54, true, 70, NULL )` —
        // view.cpp:111. A ConVar with bounds clamps rather than refuses, so a config asking for 90
        // gets 70 in the game and gets 70 here. Refusing it instead would be this viewer
        // disagreeing with a file TF2 itself would accept.
        string file = Path.Combine(_folder, "settings.cfg");

        File.WriteAllText(file, $"{ViewerSettings.ViewmodelFieldOfViewCommand} 90\n");
        ViewerSettings.Load(file).ViewmodelFieldOfView.ShouldBe(70f, 0.01f);

        // The floor too, which is the end a player cannot lower past — and the end a test that
        // only checked the ceiling would leave unmeasured.
        File.WriteAllText(file, $"{ViewerSettings.ViewmodelFieldOfViewCommand} 10\n");
        ViewerSettings.Load(file).ViewmodelFieldOfView.ShouldBe(54f, 0.01f);
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

    [Test]
    public void Parse_AScreenshotFolder_KeepsThePathAsWritten()
    {
        // Quoted, because a Windows path has spaces in it more often than not and Source's own
        // config syntax accepts quotes around a value.
        ViewerSettings settings = ViewerSettings.Parse(
            "screenshot_folder \"D:\\Tf2DemoSalvage\\my shots\"");

        settings.ScreenshotFolder.ShouldBe("D:\\Tf2DemoSalvage\\my shots");

        // The control: absent means null, which is "beside the log", not an empty path that would
        // be created as a folder called "" somewhere unpredictable.
        ViewerSettings.Parse("frame_rate_limit 60").ScreenshotFolder.ShouldBeNull();
    }

    [Test]
    public void Parse_OneCommandOntoExistingSettings_LeavesTheOthersAlone()
    {
        // **This is what makes `+command value` safe as a launch option.** Parsing a single command
        // from the command line starts from whatever the config file already said; without the
        // `onto` argument it would start from the defaults, and passing one setting at startup
        // would silently reset every other one the user had chosen.
        ViewerSettings configured = ViewerSettings.Parse(
            "frame_rate_limit 60\nviewmodel_fov 54\ntexture_quality 256");

        configured.FrameRateLimit.ShouldBe(60);

        ViewerSettings overridden = ViewerSettings.Parse(
            "screenshot_folder \"D:\\shots\"", onto: configured);

        overridden.ScreenshotFolder.ShouldBe("D:\\shots");

        // Everything the config chose survives — this is the assertion the feature turns on.
        overridden.FrameRateLimit.ShouldBe(60);
        overridden.ViewmodelFieldOfView.ShouldBe(54f, 0.01f);
        overridden.TextureQuality.ShouldBe(configured.TextureQuality);
    }
}
