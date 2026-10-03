using System;
using System.IO;
using System.Linq;

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
    private static readonly System.Globalization.CultureInfo Invariant =
        System.Globalization.CultureInfo.InvariantCulture;

    private static readonly TimeSpan DialogTimeout = TimeSpan.FromSeconds(15);

    /// <summary>z1800 is 30 MB of text; a minute is generous on any machine this runs on.</summary>
    private static readonly TimeSpan WorkTimeout = TimeSpan.FromSeconds(120);

    private static ViewerApplication _viewer => ViewerSession.App;

    private string _folder = string.Empty;

    /// <summary>What happened in each dialog and when, printed at teardown.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Timeline = new();

    [SetUp]
    public void CreateFolder()
    {
        _folder = Path.Combine(Path.GetTempPath(), "tf2ds-ui-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        TestContext.Out.WriteLine("focus at setup: " + Focused());
        _focusBefore = _viewer.Window.Automation.FocusedElement() is { } focused
            && focused.Properties.ProcessId.ValueOrDefault == _viewer.Window.Properties.ProcessId.Value
                ? focused.Properties.AutomationId.ValueOrDefault
                : null;
    }

    /// <summary>The viewer control that had keyboard focus before the test, or null if none did.</summary>
    private string? _focusBefore;

    private static string Focused() =>
        (_viewer.Window.Automation.FocusedElement() is { } focused
            ? $"{focused.Properties.ControlType.ValueOrDefault} '{focused.Properties.Name.ValueOrDefault}' "
              + $"id={focused.Properties.AutomationId.ValueOrDefault} class={focused.Properties.ClassName.ValueOrDefault} "
              + $"pid={focused.Properties.ProcessId.ValueOrDefault}"
            : "nothing")
        + $"; foreground pid={ViewerApplication.ForegroundProcessId()}, viewer pid={_viewer.Window.Properties.ProcessId.Value}";

    /// <remarks>
    /// A failure mid-dialog leaves it modal over the shared viewer, and every later test in the session
    /// then fails on a disabled window; cancelling it here keeps one failure one failure.
    /// </remarks>
    [TearDown]
    public void CloseDialogAndDeleteFolder()
    {
        if (Dialog() is { } dialog)
        {
            TestContext.Out.WriteLine("a file dialog was still open after the test; cancelling it\n" + DescribeWindows());
            // A box the dialog raised (CI: "File not found") disables it, so Cancel cannot be invoked
            // until that box is dismissed first.
            if (dialog.FindFirstChild(search => search.ByClassName("#32770")) is { } box)
            {
                box.FindFirstDescendant(search => search.ByControlType(ControlType.Button))?.AsButton().Invoke();
                Retry.WhileFalse(() => dialog.Properties.IsEnabled.ValueOrDefault, DialogTimeout, throwOnTimeout: true);
            }

            dialog.FindFirstChild(search => search.ByAutomationId("2"))?.AsButton().Invoke();
            Retry.WhileFalse(() => Dialog() is null, DialogTimeout, throwOnTimeout: true);
        }

        // Driving the File menu through UIA leaves the menu bar holding keyboard focus (measured:
        // "focus at teardown: MenuBar 'Main menu'"), and every later key-press test then typed into
        // the menu — the ten failures after this one on CI. Put focus back where the test found it.
        string restore = string.IsNullOrEmpty(_focusBefore) ? "Viewport" : _focusBefore;
        _viewer.Find(restore).Focus();
        bool restored = Retry.WhileFalse(
            () => _viewer.Window.Automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault == restore,
            DialogTimeout).Success;
        TestContext.Out.WriteLine("focus at teardown: " + Focused());
        restored.ShouldBeTrue($"keyboard focus did not return to '{restore}'");
        TestContext.Out.WriteLine("dialog timeline:\n" + string.Join("\n", Timeline));
        Timeline.Clear();
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
        FillDialog(text, "z1800.txt");
        WaitForStatus("Exported");
        _viewer.StatusText().ShouldEndWith(" to " + text, Case.Sensitive, "the export went somewhere else");

        Press("Compile assembly");
        FillDialog(text, string.Empty);
        FillDialog(rebuilt, "z1800.dem");
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
    /// <param name="path">The full path to type.</param>
    /// <param name="defaultName">The name the viewer pre-fills, or empty when it sets none.</param>
    private static void FillDialog(string path, string defaultName)
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

        // Holding the viewer's own default name, not merely enabled: the dialog writes that default
        // into the box on its own schedule, and on CI it landed AFTER the typed path and replaced it —
        // the export went to Documents\z1800.txt and Compile's open dialog then sat under "File not
        // found" (Test workflow red from c3b05a7b; run 37131359285). The extension is not compared
        // because Explorer hides a known one in that box.
        System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string what) => Timeline.Enqueue($"{clock.ElapsedMilliseconds,6} ms  {what}");
        Mark($"dialog '{dialog.Properties.Name.ValueOrDefault}' found");

        string stem = Path.GetFileNameWithoutExtension(defaultName);
        AutomationElement? name = Retry.WhileNull(
            () => dialog.FindFirstDescendant(search => search
                .ByControlType(ControlType.Edit).And(search.ByName("File name:"))) is { IsEnabled: true } edit
                && (edit.Patterns.Value.Pattern.Value.ValueOrDefault ?? string.Empty)
                    .StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                ? edit
                : null,
            DialogTimeout).Result;
        name.ShouldNotBeNull($"the file dialog's name box never became enabled holding '{stem}':\n" + DescribeWindows());
        Mark("name boxes: " + string.Join("; ", Array.ConvertAll(
            dialog.FindAllDescendants(search => search.ByControlType(ControlType.Edit).And(search.ByName("File name:"))),
            edit => $"'{edit.Properties.Name.ValueOrDefault}' id={edit.Properties.AutomationId.ValueOrDefault} "
                + $"offscreen={edit.Properties.IsOffscreen.ValueOrDefault} enabled={edit.Properties.IsEnabled.ValueOrDefault}")));
        Mark($"name box ready: id={name.Properties.AutomationId.ValueOrDefault} value='{name.Patterns.Value.Pattern.Value.ValueOrDefault}'");
        name.Patterns.Value.Pattern.SetValue(path);

        string typed = Retry.WhileFalse(
                () => name.Patterns.Value.Pattern.Value.ValueOrDefault == path, DialogTimeout).Success
            ? path
            : name.Patterns.Value.Pattern.Value.ValueOrDefault ?? "<no value>";
        Mark($"typed; reads back '{typed}'");
        typed.ShouldBe(path, "the name box did not end up holding the typed path");
        dialog.FindFirstChild(search => search.ByAutomationId("1"))!.AsButton().Invoke();
        Mark("OK invoked");

        bool closed = Retry.WhileFalse(() => Dialog() is null, DialogTimeout).Success;
        Mark("closed: " + closed);
        closed.ShouldBeTrue($"the dialog stayed open after OK with '{typed}' in its name box:\n" + DescribeWindows());
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

    /// <summary>
    /// Every top-level window on the desktop — class, name, owning process, enabled — with the text
    /// of each dialog, and the viewer's log tail: what is modal over what, when a dialog is disabled.
    /// </summary>
    private static string DescribeWindows()
    {
        System.Text.StringBuilder report = new();
        int viewer = _viewer.Window.Properties.ProcessId.Value;
        foreach (AutomationElement window in _viewer.Window.Automation.GetDesktop().FindAllChildren())
        {
            int pid = window.Properties.ProcessId.ValueOrDefault;
            report.Append(pid == viewer ? "  * " : "    ")
                .Append(Invariant, $"{window.Properties.ClassName.ValueOrDefault} '{window.Properties.Name.ValueOrDefault}' ")
                .AppendLine(Invariant, $"pid={pid} enabled={window.Properties.IsEnabled.ValueOrDefault}");
            if (window.Properties.ClassName.ValueOrDefault == "#32770")
            {
                foreach (AutomationElement text in window.FindAllDescendants(search => search
                    .ByControlType(ControlType.Text)))
                {
                    report.AppendLine(Invariant, $"        text: {text.Properties.Name.ValueOrDefault}");
                }
            }
        }

        // UIA parents an owned window under its owner, so a box the dialog raised is the dialog's
        // child, not a desktop window — two levels down covers a message box over the file dialog.
        foreach (AutomationElement child in _viewer.Window.FindAllChildren(search => search.ByClassName("#32770")))
        {
            report.AppendLine(Invariant,
                $"  viewer child dialog '{child.Properties.Name.ValueOrDefault}' enabled={child.Properties.IsEnabled.ValueOrDefault}");
            foreach (AutomationElement inner in child.FindAllChildren())
            {
                report.AppendLine(Invariant,
                    $"      {inner.Properties.ClassName.ValueOrDefault} '{inner.Properties.Name.ValueOrDefault}' enabled={inner.Properties.IsEnabled.ValueOrDefault}");
                if (inner.Properties.ClassName.ValueOrDefault == "#32770")
                {
                    foreach (AutomationElement text in inner.FindAllChildren())
                    {
                        report.AppendLine(Invariant,
                            $"          {text.Properties.ClassName.ValueOrDefault} '{text.Properties.Name.ValueOrDefault}'");
                    }
                }
            }
        }

        // Without the per-frame render lines, which otherwise fill any tail.
        report.AppendLine("viewer log tail (render lines left out):");
        foreach (string line in _viewer.Tail(4000).Where(line => !line.Contains("[render]", StringComparison.Ordinal)).TakeLast(40))
        {
            report.Append("    ").AppendLine(line);
        }

        return report.ToString();
    }

    private static void WaitForStatus(string prefix)
    {
        bool reached = Retry.WhileFalse(
            () => _viewer.StatusText().StartsWith(prefix, StringComparison.Ordinal),
            WorkTimeout).Success;

        reached.ShouldBeTrue($"the status bar never said '{prefix}…'; it says '{_viewer.StatusText()}'\n" + DescribeWindows());
    }
}
