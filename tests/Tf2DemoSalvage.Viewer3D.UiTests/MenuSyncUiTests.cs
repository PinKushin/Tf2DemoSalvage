using System;
using System.Collections.Generic;
using System.IO;

using FlaUI.Core.Input;
using FlaUI.Core.Tools;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// A bound button that sets a ConVar without the menu shows on the menu the next time it opens (D215, D218 review):
/// the window answers a menu's opening with `ViewerMenu.Sync`, which no unit test reaches.
/// </summary>
/// <remarks>
/// **A mouse button, not a key.** A keyboard key bound to a console command does not reach the console in the viewer:
/// only mouse buttons (`PointerDown`) and the held flight keys do (the input gap B542). MOUSE3 reaches it.
/// </remarks>
[TestFixture]
public sealed class MenuSyncUiTests
{
    private string _root = string.Empty;

    [SetUp]
    public void CreateRoot() =>
        _root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "tf2ds-ui-menusync-" + Guid.NewGuid().ToString("N"))).FullName;

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
    public void ViewMenu_OpenedAfterABoundToggle_FollowsThePositionReadout()
    {
        // A tf folder of our own, so the config is the test's and never the owner's.
        string tf = Directory.CreateDirectory(Path.Combine(_root, "tf")).FullName;
        File.WriteAllBytes(Path.Combine(tf, SteamInstall.Recogniser), []);
        string cfg = Directory.CreateDirectory(Path.Combine(tf, "cfg")).FullName;
        File.WriteAllText(Path.Combine(cfg, "autoexec.cfg"), "bind \"MOUSE3\" \"toggle cl_showpos\"\n");
        Directory.CreateDirectory(Path.Combine(_root, "emptysteam"));

        using ViewerApplication viewer = ViewerApplication.Launch(
            new Dictionary<string, string?>
            {
                [ViewerSettings.PathVariable] = Path.Combine(_root, "settings.cfg"),
                [SteamInstall.SteamRootVariable] = Path.Combine(_root, "emptysteam"),
                [SteamInstall.OverrideVariable] = null,
            },
            askForGameFolder: false,
            "+" + ViewerSettings.GameFolderCommand,
            tf,
            "+cl_showpos",
            "0");

        Retry.WhileFalse(() => viewer.Count("binds applied") > 0, TimeSpan.FromSeconds(30), throwOnTimeout: true,
            timeoutMessage: "the test's own autoexec.cfg was never read");

        // Real input: the binding reaching the viewer is the subject (D215).
        Press(viewer, "cl_showpos 1 (set at run time)");
        viewer.IsMenuItemChecked(MainForm.ViewMenuName, "Position").ShouldBeTrue(
            "the button set cl_showpos 1 and the menu was built unchecked; opening View must re-read it");

        Press(viewer, "cl_showpos 0 (set at run time)");
        viewer.IsMenuItemChecked(MainForm.ViewMenuName, "Position").ShouldBeFalse(
            "the second press set cl_showpos 0");
    }

    /// <summary>Middle-clicks the viewport and waits for the set it makes, so the menu is read after it.</summary>
    private static void Press(ViewerApplication viewer, string set)
    {
        int before = viewer.Count(set);
        viewer.RealClick("Viewport", MouseButton.Middle);
        Retry.WhileFalse(() => viewer.Count(set) > before, TimeSpan.FromSeconds(10), throwOnTimeout: true,
            timeoutMessage: $"the bound button never set it: no \"{set}\" in the log");
    }
}
