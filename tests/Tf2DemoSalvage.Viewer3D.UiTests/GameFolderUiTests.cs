using System;
using System.Collections.Generic;
using System.IO;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

using Tf2DemoSalvage.Scene;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>D210's folder picker: asked at startup when TF2 is not found, and File &gt; TF2 folder later.</summary>
/// <remarks>
/// **Each test launches its OWN viewer, never the shared session's**, in a state where discovery
/// genuinely fails on any machine: <c>TF2VIEW_STEAM_ROOT</c> points Steam discovery at an empty temp
/// folder, <c>TF2_FOLDER</c> is removed, and <c>TF2VIEW_SETTINGS</c> is a temp file — so the owner's
/// TF2 install and his real <c>settings.cfg</c> are neither read nor written, and the test passes in
/// any order and on CI. Its control is the viewer's own startup line saying discovery found nothing,
/// asserted before any dialog is expected; without it, a machine whose TF2 leaked through would skip
/// the picker and the test would be measuring nothing.
///
/// **Menus are clicked, never expanded through UIA**, which strands WinForms in keyboard menu mode
/// after a modal dialog (<c>docs/memory/a-uia-expanded-menu-eats-later-keys.md</c>).
/// </remarks>
[TestFixture]
public sealed class GameFolderUiTests
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(30);

    private string _root = string.Empty;
    private string _settings = string.Empty;
    private ViewerApplication? _viewer;

    private ViewerApplication Viewer => _viewer ?? throw new InvalidOperationException("no viewer launched");

    [SetUp]
    public void LaunchWithNoDiscoverableInstall()
    {
        _root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "tf2ds-ui-gamefolder-" + Guid.NewGuid().ToString("N"))).FullName;
        _settings = Path.Combine(_root, "settings.cfg");
        Directory.CreateDirectory(Path.Combine(_root, "emptysteam"));

        _viewer = ViewerApplication.Launch(
            new Dictionary<string, string?>
            {
                [ViewerSettings.PathVariable] = _settings,
                [SteamInstall.SteamRootVariable] = Path.Combine(_root, "emptysteam"),
                [SteamInstall.OverrideVariable] = null,
            },
            askForGameFolder: true);

        // The control: discovery really failed, said before the picker opens.
        Retry.WhileFalse(() => Viewer.Count(MainForm.GameFolderLog) > 0, DialogTimeout, throwOnTimeout: true);
        Viewer.LastLine(MainForm.GameFolderLog).ShouldNotBeNull()
            .ShouldContain(MainForm.GameFolderLog + MainForm.GameFolderNotFound,
                customMessage: "discovery found a TF2 install, so the not-found path is not what runs here");
    }

    [TearDown]
    public void CloseViewer()
    {
        // Teardown releases and never asserts: a dialog left open would stop the viewer closing.
        if (_viewer is not null && Dialog() is { } dialog)
        {
            dialog.FindFirstChild(search => search.ByAutomationId("2"))?.AsButton().Invoke();
            Retry.WhileFalse(() => Dialog() is null, DialogTimeout, ignoreException: true);
        }

        _viewer?.Dispose();
        _viewer = null;

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
    public void StartupPicker_AFolderThatIsNotTf2_IsRefusedAndNothingIsSaved()
    {
        string notGame = Directory.CreateDirectory(Path.Combine(_root, "Pictures")).FullName;

        Choose(notGame);

        Retry.WhileFalse(() => Viewer.Count("] " + MainForm.NotAGameFolder) > 0, DialogTimeout)
            .Success.ShouldBeTrue($"the viewer never said '{MainForm.NotAGameFolder}'; status '{Viewer.StatusText()}'");
        Viewer.LastLine("] " + MainForm.NotAGameFolder).ShouldNotBeNull().ShouldContain(notGame);
        File.Exists(_settings).ShouldBeFalse("a refused folder must not be written to the cfg");
    }

    [Test]
    public void GameFolderMenuItem_AfterTheStartupPickerIsDeclined_SavesAValidTfFolderToTheCfg()
    {
        // Declined at startup: it stops asking, and says so in the cfg.
        CancelDialog();
        Retry.WhileFalse(() => File.Exists(_settings) && File.ReadAllText(_settings).Contains(
                ViewerSettings.AskForGameFolderCommand + " 0", StringComparison.Ordinal), DialogTimeout)
            .Success.ShouldBeTrue("declining the startup picker did not save cl_game_folder_ask 0");

        // A tf folder by the recogniser, picked from the folder above it as Steam's "Browse" shows it.
        string game = Path.Combine(_root, "Team Fortress 2");
        string tf = Directory.CreateDirectory(Path.Combine(game, "tf")).FullName;
        File.WriteAllBytes(Path.Combine(tf, SteamInstall.Recogniser), []);

        Viewer.Click(FindShown("File menu"), "File menu");
        Viewer.Click(FindShown(MainForm.GameFolderItemName), MainForm.GameFolderItemName);
        Choose(game);

        Retry.WhileFalse(() => Viewer.Count("] " + MainForm.GameFolderSet) > 0, DialogTimeout)
            .Success.ShouldBeTrue($"the folder was not accepted; status '{Viewer.StatusText()}'");
        File.ReadAllText(_settings).ShouldContain($"{ViewerSettings.GameFolderCommand} \"{tf}\"");
    }

    [Test]
    public void StartupPicker_ATf2FolderWithItsOwnBinds_RelabelsTheMenuShortcuts()
    {
        // The player's autoexec moves the screenshot off F5, TF2's default and the viewer's own.
        string tf = Directory.CreateDirectory(Path.Combine(_root, "tf")).FullName;
        File.WriteAllBytes(Path.Combine(tf, SteamInstall.Recogniser), []);
        Directory.CreateDirectory(Path.Combine(tf, "cfg"));
        File.WriteAllText(Path.Combine(tf, "cfg", "autoexec.cfg"), "bind F9 screenshot\n");

        Choose(tf);
        Retry.WhileFalse(() => Viewer.Count("] " + MainForm.GameFolderSet) > 0, DialogTimeout)
            .Success.ShouldBeTrue($"the folder was not accepted; status '{Viewer.StatusText()}'");

        Viewer.Click(FindShown(MainForm.ViewMenuName), MainForm.ViewMenuName);
        FindShown(MainForm.ScreenshotItemName).Properties.AcceleratorKey.ValueOrDefault
            .ShouldBe("F9", "the menu still prints the key bound before the player's config was read");
    }

    /// <summary>Types a folder into the open folder dialog and confirms it.</summary>
    private void Choose(string folder)
    {
        AutomationElement dialog = WaitForDialog();

        AutomationElement box = Retry.WhileNull(
                () => dialog.FindFirstDescendant(search => search
                    .ByControlType(ControlType.Edit).And(search.ByName("Folder:"))) is { IsEnabled: true } edit
                    ? edit
                    : null,
                DialogTimeout, ignoreException: true)
            .Result ?? throw new InvalidOperationException("the folder dialog has no Folder box");

        if (!Viewer.HasFocus())
        {
            ViewerApplication.TakeForeground(new IntPtr(dialog.Properties.NativeWindowHandle.Value.ToInt64()));
        }

        box.Focus();
        Retry.WhileFalse(Viewer.HasFocus, DialogTimeout).Success
            .ShouldBeTrue("the folder dialog did not take the foreground, so typing would go elsewhere");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(folder);
        Retry.WhileFalse(() => box.Patterns.Value.Pattern.Value.ValueOrDefault == folder, DialogTimeout)
            .Success.ShouldBeTrue("the Folder box did not end up holding the typed path");

        (dialog.FindFirstChild(search => search.ByAutomationId("1"))
                ?? throw new InvalidOperationException("the folder dialog has no Select Folder button"))
            .Patterns.Invoke.Pattern.Invoke();
        Retry.WhileFalse(() => Dialog() is null, DialogTimeout, ignoreException: true)
            .Success.ShouldBeTrue("the folder dialog stayed open after Select Folder");
    }

    private void CancelDialog()
    {
        AutomationElement dialog = WaitForDialog();
        Retry.WhileNull(
                () => dialog.FindFirstChild(search => search.ByAutomationId("2")) is { IsEnabled: true } cancel ? cancel : null,
                DialogTimeout, ignoreException: true)
            .Result.ShouldNotBeNull("the folder dialog has no Cancel button").AsButton().Invoke();
        Retry.WhileFalse(() => Dialog() is null, DialogTimeout, ignoreException: true)
            .Success.ShouldBeTrue("the folder dialog stayed open after Cancel");
    }

    private AutomationElement WaitForDialog() =>
        Retry.WhileNull(Dialog, DialogTimeout, ignoreException: true).Result
        ?? throw new InvalidOperationException("no folder dialog opened; status '" + Viewer.StatusText() + "'");

    /// <summary>An on-screen element of the viewer's process by accessible name — how UIA reaches a menu item.</summary>
    private AutomationElement FindShown(string name) =>
        Retry.WhileNull(
                () => Viewer.Window.Automation.GetDesktop().FindFirstDescendant(search => search
                    .ByProcessId(Viewer.Window.Properties.ProcessId.Value)
                    .And(search.ByName(name))) is { IsOffscreen: false } shown
                    ? shown
                    : null,
                DialogTimeout, ignoreException: true)
            .Result ?? throw new InvalidOperationException($"nothing named '{name}' was shown");

    /// <summary>The viewer's common dialog, found as a top-level window of its process.</summary>
    private AutomationElement? Dialog() =>
        Viewer.Window.FindFirstChild(search => search.ByClassName("#32770"))
        ?? Viewer.Window.Automation.GetDesktop().FindFirstChild(search => search
            .ByProcessId(Viewer.Window.Properties.ProcessId.Value)
            .And(search.ByClassName("#32770")));
}
