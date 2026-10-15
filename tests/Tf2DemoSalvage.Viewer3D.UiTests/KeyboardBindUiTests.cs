using System;
using System.Collections.Generic;
using System.IO;

using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>A KEYBOARD key bound in the watcher's own config runs its bind (B547).</summary>
/// <remarks>
/// **Only mouse buttons and the free camera's flight keys reached the console**, so `bind F7 "toggle cl_showpos"` did
/// nothing while the same bind on MOUSE3 worked. The engine's `Key_Event` hands every key to its binding, and a unit
/// test of the session cannot see whether the window's `ProcessCmdKey` ever gets there - real keys can.
///
/// Its own viewer: a temp `tf` folder carrying only the install marker and `cfg/autoexec.cfg`, named by
/// <c>+cl_game_folder</c>, so the config is the test's and the owner's real one is never read. No demo is opened; the
/// observation is the ConVar's set line in the log, which the menu route writes the same way.
/// </remarks>
[TestFixture]
public sealed class KeyboardBindUiTests
{
    private const string Autoexec =
        """
        bind "F7" "toggle cl_showpos"
        alias sp_on "cl_showpos 1; alias sp_tog sp_off"
        alias sp_off "cl_showpos 0; alias sp_tog sp_on"
        alias sp_tog sp_on
        bind "F6" "sp_tog"
        bind "F8" "toggle cl_showpos"
        bind "ALT" "toggle cl_showpos"
        """;

    private string _root = string.Empty;

    [SetUp]
    public void CreateRoot() =>
        _root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "tf2ds-ui-keybinds-" + Guid.NewGuid().ToString("N"))).FullName;

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
    public void PressRealKey_KeysBoundInAutoexec_RunTheirBindsAndTheConfigWinsTheViewersF8()
    {
        string tf = Directory.CreateDirectory(Path.Combine(_root, "tf")).FullName;
        File.WriteAllBytes(Path.Combine(tf, SteamInstall.Recogniser), []);
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(tf, "cfg")).FullName, "autoexec.cfg"), Autoexec);
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
            tf);

        Retry.WhileFalse(() => viewer.Count("binds applied") > 0, TimeSpan.FromSeconds(30), throwOnTimeout: true);

        // A plain bind: `toggle` on a keyboard key.
        viewer.PressRealKey(VirtualKeyShort.F7);
        WaitFor(viewer, On,1, "F7's `toggle cl_showpos` never ran");

        // An alias that redefines itself: off on this press, on again on the next.
        viewer.PressRealKey(VirtualKeyShort.F6);
        WaitFor(viewer, On,2, "F6's alias chain never ran its first half");
        viewer.PressRealKey(VirtualKeyShort.F6);
        WaitFor(viewer, Off,1, "F6's alias did not redefine itself");

        // F8 is the viewer's frame-rate default; the config bound it, so the config's bind runs instead (D101).
        viewer.PressRealKey(VirtualKeyShort.F8);
        WaitFor(viewer, On,3, "F8's config bind never ran");

        // A lone ALT bound: its bind runs, and its release must not leave the menu strip in menu mode (B542), which
        // would eat the F7 after it.
        viewer.PressRealKey(VirtualKeyShort.ALT);
        WaitFor(viewer, Off,2, "ALT's bind never ran");
        viewer.PressRealKey(VirtualKeyShort.F7);
        WaitFor(viewer, On,4, "F7 after ALT never ran: the release left the menu in menu mode");

        // The View menu follows a key's set (the D218 menu sync on open).
        viewer.IsMenuItemChecked(MainForm.ViewMenuName, PositionItemName).ShouldBeTrue("the menu must show the key's set");
    }

    /// <summary>`SettingsPresenter.SetConVar`'s line for a run-time set - the one log route, shared with MenuSyncUiTests.</summary>
    private const string On = "cl_showpos 1 (set at run time)";

    /// <summary>The same line for the readout going off.</summary>
    private const string Off = "cl_showpos 0 (set at run time)";

    /// <summary>The View menu's position item, by its accessible name.</summary>
    private const string PositionItemName = "Position";

    private static void WaitFor(ViewerApplication viewer, string line, int count, string message)
    {
        Retry.WhileFalse(() => viewer.Count(line) >= count, TimeSpan.FromSeconds(10), throwOnTimeout: false);
        viewer.Count(line).ShouldBe(count, message);
    }
}
