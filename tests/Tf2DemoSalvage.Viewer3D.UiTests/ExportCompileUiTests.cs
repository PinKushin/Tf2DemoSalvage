using System;
using System.IO;
using System.Threading.Tasks;

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

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void ExportThenCompile_OpenDemo_RebuildsItsBytes()
    {
        string text = Path.Combine(_folder, "z1800.txt");
        string rebuilt = Path.Combine(_folder, "z1800-rebuilt.dem");

        Press("ExportButton");
        FillDialog(text);
        WaitForStatus("Exported");

        Press("CompileButton");
        FillDialog(text);
        FillDialog(rebuilt);
        WaitForStatus("Compiled");

        File.ReadAllBytes(rebuilt).AsSpan().SequenceEqual(File.ReadAllBytes(ViewerSession.DemoPath))
            .ShouldBeTrue("the compiled demo is not the opened demo byte for byte");
    }

    /// <summary>Invokes a button off the test thread: invoking one that opens a modal blocks until it closes.</summary>
    private static void Press(string automationId) =>
        _ = Task.Run(() => _viewer.ClickButton(automationId));

    /// <summary>Types a path into the viewer's open file dialog and confirms it.</summary>
    private static void FillDialog(string path)
    {
        AutomationElement? dialog = Retry.WhileNull(Dialog, DialogTimeout).Result;
        dialog.ShouldNotBeNull("no file dialog opened");

        AutomationElement name = Retry.WhileNull(
            () => dialog.FindFirstDescendant(search => search
                .ByControlType(ControlType.Edit).And(search.ByName("File name:"))),
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
        _viewer.Window.Automation.GetDesktop().FindFirstChild(search => search
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
