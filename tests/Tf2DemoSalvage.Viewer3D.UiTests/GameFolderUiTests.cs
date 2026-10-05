using System;
using System.IO;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>File &gt; TF2 folder: the picker D210 gives a user whose install was not found.</summary>
/// <remarks>
/// **Only the REFUSED choice is driven here, deliberately.** An accepted folder is saved to the
/// viewer's settings file, and that file is the owner's real one — there is no per-run settings
/// path — so a test that picked a valid folder would rewrite where the owner's viewer looks for
/// TF2. Accepting and saving are covered in-process (<c>ViewerSettingsTests</c>,
/// <c>SteamInstallTests</c>); this covers the part only the real window has: the menu entry, the
/// native dialog, and the refusal reaching the user.
///
/// **Opened with real clicks on the menu, never UIA's Expand**, which strands WinForms in keyboard
/// menu mode after a modal dialog (<c>docs/memory/a-uia-expanded-menu-eats-later-keys.md</c>).
/// </remarks>
[TestFixture]
public sealed class GameFolderUiTests
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(15);

    private static ViewerApplication _viewer => ViewerSession.App;

    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "tf2ds-ui-notgame-" + Guid.NewGuid().ToString("N"))).FullName;
    }

    [TearDown]
    public void CloseDialogAndDeleteFolder()
    {
        // Teardown releases state and never asserts: a dialog left open would disable the shared
        // viewer for every later test, so it is cancelled and the outcome logged.
        if (Dialog() is { } dialog)
        {
            dialog.FindFirstChild(search => search.ByAutomationId("2"))?.AsButton().Invoke();

            if (!Retry.WhileFalse(() => Dialog() is null, DialogTimeout, ignoreException: true).Success)
            {
                TestContext.Out.WriteLine("the folder dialog was still open after Cancel");
            }
        }

        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public void GameFolderMenuItem_AFolderThatIsNotTf2_IsRefusedAndSaysWhy()
    {
        int refused = _viewer.Count("] " + MainForm.NotAGameFolder);

        _viewer.Click(MainForm.FileMenuId);
        Retry.WhileFalse(() => _viewer.Exists(MainForm.GameFolderItemId), DialogTimeout)
            .Success.ShouldBeTrue("the File menu has no TF2 folder entry:\n" + _viewer.DescribeTree());
        _viewer.Click(MainForm.GameFolderItemId);

        AutomationElement dialog = Retry.WhileNull(Dialog, DialogTimeout, ignoreException: true).Result
            ?? throw new InvalidOperationException("the TF2 folder entry opened no dialog");

        if (!_viewer.HasFocus())
        {
            ViewerApplication.TakeForeground(new IntPtr(dialog.Properties.NativeWindowHandle.Value.ToInt64()));
        }

        // The folder picker's own box, named "Folder:" by the shell.
        AutomationElement box = Retry.WhileNull(
                () => dialog.FindFirstDescendant(search => search
                    .ByControlType(ControlType.Edit).And(search.ByName("Folder:"))) is { IsEnabled: true } edit
                    ? edit
                    : null,
                DialogTimeout)
            .Result ?? throw new InvalidOperationException("the folder dialog has no Folder box");

        box.Focus();
        Retry.WhileFalse(_viewer.HasFocus, DialogTimeout).Success
            .ShouldBeTrue("the folder dialog did not take the foreground, so typing would go elsewhere");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(_folder);
        Retry.WhileFalse(() => box.Patterns.Value.Pattern.Value.ValueOrDefault == _folder, DialogTimeout)
            .Success.ShouldBeTrue("the Folder box did not end up holding the typed path");

        (dialog.FindFirstChild(search => search.ByAutomationId("1"))
                ?? throw new InvalidOperationException("the folder dialog has no Select Folder button"))
            .Patterns.Invoke.Pattern.Invoke();

        Retry.WhileFalse(() => Dialog() is null, DialogTimeout, ignoreException: true)
            .Success.ShouldBeTrue("the folder dialog stayed open after Select Folder");

        Retry.WhileFalse(() => _viewer.Count("] " + MainForm.NotAGameFolder) > refused, DialogTimeout)
            .Success.ShouldBeTrue(
                $"the viewer never said '{MainForm.NotAGameFolder}'; status reads '{_viewer.StatusText()}'");
        _viewer.LastLine("] " + MainForm.NotAGameFolder).ShouldNotBeNull().ShouldContain(_folder);
    }

    /// <summary>The viewer's common dialog, found as a top-level window of its process.</summary>
    private static AutomationElement? Dialog() =>
        _viewer.Window.FindFirstChild(search => search.ByClassName("#32770"))
        ?? _viewer.Window.Automation.GetDesktop().FindFirstChild(search => search
            .ByProcessId(_viewer.Window.Properties.ProcessId.Value)
            .And(search.ByClassName("#32770")));
}
