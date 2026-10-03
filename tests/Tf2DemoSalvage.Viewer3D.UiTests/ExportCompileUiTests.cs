using System;
using System.IO;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// The Export and Compile buttons, driven through their real file dialogs.
/// </summary>
/// <remarks>
/// **Export writes the open demo's assembly text and Compile turns it back into the same bytes**,
/// so the one assertion that matters is the rebuilt file against the demo on disk. The dialogs are
/// the stock Win32 common dialogs, reached as modal windows of the viewer and filled through UIA's
/// value and invoke patterns — no synthesized input, so nothing can land in another window.
/// </remarks>
[TestFixture]
public sealed class ExportCompileUiTests
{
    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(15);

    /// <summary>z1800 is 30 MB of text; a minute is generous on any machine this runs on.</summary>
    private static readonly TimeSpan WorkTimeout = TimeSpan.FromSeconds(120);

    private static ViewerApplication _viewer => ViewerSession.App;

    private string _folder = string.Empty;

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2ds-ui-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    /// <remarks>
    /// A failure mid-dialog leaves it modal over the shared viewer, and every later test in the session
    /// then fails on a disabled window; cancelling it here keeps one failure one failure.
    /// </remarks>
    [TearDown]
    public void CloseDialogAndDeleteFolder()
    {
        if (Dialog() is { } dialog)
        {
            TestContext.Out.WriteLine("a file dialog was still open after the test; cancelling it");
            dialog.FindFirstChild(search => search.ByAutomationId("2"))?.AsButton().Invoke();
            Retry.WhileFalse(() => Dialog() is null, DialogTimeout, throwOnTimeout: true);
        }

        Directory.Delete(_folder, recursive: true);
    }

    [Test]
    public void ExportThenCompile_OpenDemo_RebuildsItsBytes()
    {
        string text = Path.Combine(_folder, "z1800.txt");
        string rebuilt = Path.Combine(_folder, "z1800-rebuilt.dem");

        // The session waits for the world, not for the load to finish: run first, a press landed
        // during the post-load collection and the viewer logged none (two runs of five). Synchronised
        // on the line the load ends with.
        Retry.WhileFalse(() => _viewer.Count("opening state applied") > 0, WorkTimeout, throwOnTimeout: true);

        Press("Export assembly");
        FillDialog(text);
        WaitForStatus("Exported");

        Press("Compile assembly");
        FillDialog(text);
        FillDialog(rebuilt);
        WaitForStatus("Compiled");

        File.ReadAllBytes(rebuilt).AsSpan().SequenceEqual(File.ReadAllBytes(ViewerSession.DemoPath))
            .ShouldBeTrue("the compiled demo is not the opened demo byte for byte");
    }

    /// <summary>Invokes a File menu item; the viewer posts the dialog, so this returns before it opens.</summary>
    /// <remarks>
    /// Through the File menu, not the action-row buttons: a button's UIA Invoke is a BM_CLICK, which
    /// a window that is not active ignores — measured here as an invoke that completed and opened
    /// nothing — and this suite never takes the foreground.
    /// </remarks>
    private static void Press(string itemName)
    {
        // Not ViewerApplication.InvokeMenuItem: its Collapse after the invoke races the posted
        // dialog and, landing second, closed it — no dialog in one run of two. Invoking an item
        // closes its menu on its own.
        AutomationElement menu = Retry.WhileNull(
            () => _viewer.Window.FindFirstDescendant(search => search.ByName("File menu")),
            DialogTimeout).Result!;
        menu.Patterns.ExpandCollapse.Pattern.Expand();

        // Shown, not merely present: the item exists before its drop-down is on screen, and an
        // invoke then was accepted and did nothing (the viewer logged no press).
        Retry.WhileNull(
                () => menu.FindFirstDescendant(search => search.ByName(itemName)) is { IsOffscreen: false } item
                    ? item
                    : null,
                DialogTimeout)
            .Result!.Patterns.Invoke.Pattern.Invoke();
    }

    /// <summary>Types a path into the viewer's open file dialog and confirms it.</summary>
    private static void FillDialog(string path)
    {
        AutomationElement? dialog = Retry.WhileNull(Dialog, DialogTimeout).Result;
        dialog.ShouldNotBeNull(
            $"no file dialog opened; the status bar says '{_viewer.StatusText()}' and the desktop holds "
            + string.Join(", ", Array.ConvertAll(
                _viewer.Window.Automation.GetDesktop().FindAllChildren(search => search
                    .ByProcessId(_viewer.Window.Properties.ProcessId.Value)),
                window => $"{window.ClassName}/{window.Name}"))
            + "; the shell's children are " + string.Join(", ", Array.ConvertAll(
                _viewer.Window.FindAllChildren(),
                child => $"{child.Properties.ClassName.ValueOrDefault}/{child.Properties.Name.ValueOrDefault}")));

        // Enabled, not merely present: on CI the edit exists before the dialog finishes initialising,
        // and SetValue then threw ElementNotEnabledException and left the dialog open over every later
        // test (Test workflow red from c3b05a7b).
        AutomationElement name = Retry.WhileNull(
            () => dialog.FindFirstDescendant(search => search
                .ByControlType(ControlType.Edit).And(search.ByName("File name:"))) is { IsEnabled: true } edit
                ? edit
                : null,
            DialogTimeout).Result!;
        name.Patterns.Value.Pattern.SetValue(path);

        dialog.FindFirstChild(search => search.ByAutomationId("1"))!.AsButton().Invoke();

        Retry.WhileFalse(() => Dialog() is null, DialogTimeout);
    }

    /// <summary>
    /// The viewer's common dialog, found as a top-level window of its process.
    /// </summary>
    /// <remarks>
    /// Not <c>Window.ModalWindows</c>: that walks every descendant of the shell, and on this tree
    /// it timed out (COMException 0x80131505) before reaching the dialog.
    /// </remarks>
    private static AutomationElement? Dialog() =>
        _viewer.Window.FindFirstChild(search => search.ByClassName("#32770"))
        ?? _viewer.Window.Automation.GetDesktop().FindFirstChild(search => search
            .ByProcessId(_viewer.Window.Properties.ProcessId.Value)
            .And(search.ByClassName("#32770")));

    private static void WaitForStatus(string prefix)
    {
        bool reached = Retry.WhileFalse(
            () => _viewer.StatusText().StartsWith(prefix, StringComparison.Ordinal),
            WorkTimeout).Success;

        reached.ShouldBeTrue($"the status bar never said '{prefix}…'; it says '{_viewer.StatusText()}'");
    }
}
