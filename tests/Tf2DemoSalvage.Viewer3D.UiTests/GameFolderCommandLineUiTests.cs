using System;
using System.Collections.Generic;
using System.IO;

using FlaUI.Core.Tools;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary><c>+cl_game_folder</c> on the command line reaches install detection (D210 follow-up).</summary>
/// <remarks>
/// Its own viewer, in the same no-install state as <see cref="GameFolderUiTests"/>: Steam discovery
/// pointed at an empty folder, no <c>TF2_FOLDER</c>, a temp settings file that is never written. The
/// only thing naming the folder is the launch option, so the startup line can only name it if the
/// live cvar, not the file, is what detection reads.
/// </remarks>
[TestFixture]
public sealed class GameFolderCommandLineUiTests
{
    private string _root = string.Empty;

    [SetUp]
    public void CreateRoot() =>
        _root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "tf2ds-ui-gamefolder-cli-" + Guid.NewGuid().ToString("N"))).FullName;

    [TearDown]
    public void RemoveRoot()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException failure)
        {
            TestContext.Out.WriteLine("temp tree not removed: " + failure.Message);
        }
    }

    [Test]
    public void Startup_ClGameFolderOnTheCommandLine_IsTheFolderDetectionFinds()
    {
        string tf = Directory.CreateDirectory(Path.Combine(_root, "tf")).FullName;
        File.WriteAllBytes(Path.Combine(tf, SteamInstall.Recogniser), []);
        string settings = Path.Combine(_root, "settings.cfg");
        Directory.CreateDirectory(Path.Combine(_root, "emptysteam"));

        using ViewerApplication viewer = ViewerApplication.Launch(
            new Dictionary<string, string?>
            {
                [ViewerSettings.PathVariable] = settings,
                [SteamInstall.SteamRootVariable] = Path.Combine(_root, "emptysteam"),
                [SteamInstall.OverrideVariable] = null,
            },
            askForGameFolder: false,
            "+" + ViewerSettings.GameFolderCommand,
            tf);

        Retry.WhileFalse(() => viewer.Count(MainForm.GameFolderLog) > 0, TimeSpan.FromSeconds(30), throwOnTimeout: true);
        viewer.LastLine(MainForm.GameFolderLog).ShouldNotBeNull().TrimEnd().ShouldEndWith(MainForm.GameFolderLog + tf);
        File.Exists(settings).ShouldBeFalse("a launch option is for one run and must not be saved");
    }
}
