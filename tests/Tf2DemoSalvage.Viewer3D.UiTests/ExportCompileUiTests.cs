using System;
using System.IO;
using System.Linq;

using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>
/// The Export and Compile buttons, driven through their real file dialogs.
/// </summary>
/// <remarks>
/// **Export writes the open demo's assembly text and Compile turns it back into the same bytes**,
/// so the one assertion that matters is the rebuilt file against the demo on disk. The dialogs are
/// the stock Win32 common dialogs, reached as modal windows of the viewer, with the path typed into
/// the name box and Save or Open invoked through UIA.
///
/// **Opened by clicking the action-row buttons, never through the File menu.** Expanding the menu
/// through UI Automation leaves WinForms in keyboard menu mode, which outlives the dialog and eats
/// every later keystroke whatever control has focus: opening Export from the menu and cancelling,
/// with no export at all, failed nine key-press and full-screen tests after it on CI (probe run
/// 37147488078), against none with this test ignored (run 37138141067); moving focus back to the
/// viewport did not help (run 37155302709). A real click is what a user does and enters no menu
/// mode. It needs the foreground, which ViewerApplication.Click takes and verifies.
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
    }

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
        // **Asked once, never retried.** This went through WhileException after one UIA call timed out
        // (0x80131505, seen locally); a retry hides that rather than removing it. `Dialog` reads only
        // the window's and the desktop's direct children, never a whole-tree walk, so one read is
        // the smallest exposure there is, and a timeout that still lands surfaces as itself.
        if (Dialog() is { } dialog)
        {
            TestContext.Out.WriteLine("a file dialog was still open after the test; cancelling it\n" + DescribeWindows());
            // A box the dialog raised (CI: "File not found") disables it, so Cancel cannot be invoked
            // until that box is dismissed first.
            if (dialog.FindFirstChild(search => search.ByClassName("#32770")) is { } box)
            {
                box.FindFirstDescendant(search => search.ByControlType(ControlType.Button))?.AsButton().Invoke();
                Retry.WhileFalse(() => dialog.Properties.IsEnabled.ValueOrDefault, DialogTimeout);
            }

            dialog.FindFirstChild(search => search.ByAutomationId("2"))?.AsButton().Invoke();

            // Teardown releases state; it never asserts (owner, 2026-10-04). A dialog that will not close
            // is logged here, and the next test on the shared viewer fails as itself if it truly stuck.
            if (!Retry.WhileFalse(() => Dialog() is null, DialogTimeout, ignoreException: true).Success)
            {
                TestContext.Out.WriteLine("the file dialog was still open after Cancel:\n" + DescribeWindows());
            }
        }

        TestContext.Out.WriteLine("focus at teardown: " + Focused());
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

        int exported = _viewer.Count("] Exported ");
        _viewer.Click(MainForm.ExportButtonId);
        FillDialog(text, "z1800.txt");
        WaitForOutcome("Exported", exported).ShouldEndWith(" to " + text, Case.Sensitive, "the export went somewhere else");

        int compiled = _viewer.Count("] Compiled ");
        _viewer.Click(MainForm.CompileButtonId);
        FillDialog(text, string.Empty);
        FillDialog(rebuilt, "z1800.dem");
        WaitForOutcome("Compiled", compiled).ShouldContain(" to " + rebuilt + " (", Case.Sensitive, "the compile went somewhere else");

        File.ReadAllBytes(rebuilt).AsSpan().SequenceEqual(File.ReadAllBytes(ViewerSession.DemoPath))
            .ShouldBeTrue("the compiled demo is not the opened demo byte for byte");
    }

    [Test]
    public void FileDialog_OpenedThroughTheFileMenuByAutomation_LeavesTheKeyBindingsWorking()
    {
        // **What a screen reader does**: expand File through UI Automation, invoke Export, cancel.
        // WinForms resumes keyboard menu mode on the menu strip after the modal dialog, and every
        // later key then went to the menu whatever had focus — SPACE, Escape, every binding
        // (docs/memory/a-uia-expanded-menu-eats-later-keys.md). SPACE is checked before as the
        // control: without it a key that never worked would read as one the menu ate.
        Retry.WhileFalse(() => _viewer.Count("opening state applied") > 0, WorkTimeout, throwOnTimeout: true);
        SpaceSwitchesTheCamera().ShouldBeTrue("SPACE did not switch the camera before the menu either");

        AutomationElement menu = Retry.WhileNull(
                () => _viewer.Window.FindFirstDescendant(search => search.ByName("File menu")),
                DialogTimeout).Result ?? throw new InvalidOperationException("no File menu");
        menu.Patterns.ExpandCollapse.Pattern.Expand();

        // Shown, not merely present: the item exists before its drop-down is on screen.
        Retry.WhileNull(
                () => menu.FindFirstDescendant(search => search.ByName("Export assembly")) is { IsOffscreen: false } item
                    ? item
                    : null,
                DialogTimeout)
            .Result!.Patterns.Invoke.Pattern.Invoke();

        AutomationElement dialog = Retry.WhileNull(Dialog, DialogTimeout).Result
            ?? throw new InvalidOperationException("no file dialog opened");
        Retry.WhileNull(
                () => dialog.FindFirstChild(search => search.ByAutomationId("2")) is { IsEnabled: true } cancel ? cancel : null,
                DialogTimeout)
            .Result!.AsButton().Invoke();
        Retry.WhileFalse(() => Dialog() is null, DialogTimeout, throwOnTimeout: true);

        SpaceSwitchesTheCamera().ShouldBeTrue("SPACE stopped switching the camera after the dialog: " + Focused());
    }

    /// <summary>Presses the camera-mode key, then returns the camera to the free view it started in.</summary>
    private static bool SpaceSwitchesTheCamera()
    {
        int before = CameraModeChanges();
        ViewerSession.PressSwitchCameraMode();
        bool changed = Retry.WhileFalse(() => CameraModeChanges() > before, DialogTimeout).Success;

        // Back to free, as FirstPersonUiTests' own setup does: the session is shared.
        int free = _viewer.Count(BackToTheFreeCamera);
        for (int press = 0; changed && press < 2 && _viewer.Count(BackToTheFreeCamera) == free; press++)
        {
            int was = CameraModeChanges();
            ViewerSession.PressSwitchCameraMode();
            Retry.WhileFalse(() => CameraModeChanges() > was, DialogTimeout, throwOnTimeout: true);
        }

        return changed;
    }

    private const string BackToTheFreeCamera = "back to the free camera";

    private static int CameraModeChanges() =>
        _viewer.Count(ViewerSession.FirstPersonOn) + _viewer.Count("third person on,") + _viewer.Count(BackToTheFreeCamera);

    /// <summary>Types a path into the viewer's open file dialog and confirms it.</summary>
    /// <param name="path">The full path to type.</param>
    /// <param name="defaultName">The name the viewer pre-fills, or empty when it sets none.</param>
    private static void FillDialog(string path, string defaultName)
    {
        AutomationElement? dialog = Retry.WhileNull(Dialog, DialogTimeout, ignoreException: true).Result;
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
        // into the box on its own schedule, so it is the sign the box is ready to be typed into. The
        // extension is not compared because Explorer hides a known one there (it does on CI).
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
        Mark($"name box ready: id={name.Properties.AutomationId.ValueOrDefault} value='{name.Patterns.Value.Pattern.Value.ValueOrDefault}'; {Address(dialog)}");

        // Navigated, not merely drawn: the address band names its folder once the dialog has
        // browsed there, and anything typed before that can be replaced by the dialog's own setup.
        Retry.WhileFalse(() => Address(dialog).Contains("Address: ", StringComparison.Ordinal), DialogTimeout)
            .Success.ShouldBeTrue("the dialog never named the folder it opened in: " + Address(dialog));
        Mark($"navigated; value='{name.Patterns.Value.Pattern.Value.ValueOrDefault}'; {Address(dialog)}");
        // **Typed, not set through ValuePattern.** On the CI runner the box read back a path set that
        // way right up to OK, and the dialog still saved its default name in its default folder —
        // with a D: path as well as one under AppData, with one visible Save button, with the viewer
        // in front and no menu involved (runs 37145752910, 37147390608, 37157773686). Keystrokes are
        // what the dialog is built to hear; they go only after the guard confirms the box has focus.
        name.Focus();
        Retry.WhileFalse(
                () => _viewer.HasFocus()
                    && _viewer.Window.Automation.FocusedElement()?.Properties.AutomationId.ValueOrDefault
                        == name.Properties.AutomationId.ValueOrDefault,
                DialogTimeout)
            .Success.ShouldBeTrue("the dialog's name box did not take keyboard focus: " + Focused());
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Keyboard.Type(path);

        string typed = Retry.WhileFalse(
                () => name.Patterns.Value.Pattern.Value.ValueOrDefault == path, DialogTimeout).Success
            ? path
            : name.Patterns.Value.Pattern.Value.ValueOrDefault ?? "<no value>";
        Mark($"typed; reads back '{typed}'");
        typed.ShouldBe(path, "the name box did not end up holding the typed path");
        // A direct child with id 1: the folder view's list items carry index ids too, so "1" is also
        // its second file, one level down. Save is a Button and Open a SplitButton; both invoke.
        AutomationElement confirm = dialog.FindFirstChild(search => search.ByAutomationId("1"))
            ?? throw new InvalidOperationException("the dialog has no OK button:\n" + DescribeWindows());
        Mark($"before {confirm.Properties.ControlType.ValueOrDefault} '{confirm.Properties.Name.ValueOrDefault}': "
            + $"value='{name.Patterns.Value.Pattern.Value.ValueOrDefault}'; {Address(dialog)}");
        confirm.Patterns.Invoke.Pattern.Invoke();
        Mark("OK invoked");

        bool closed = Retry.WhileFalse(() => Dialog() is null, DialogTimeout, ignoreException: true).Success;
        Mark("closed: " + closed);
        closed.ShouldBeTrue($"the dialog stayed open after OK with '{typed}' in its name box:\n" + DescribeWindows());
    }

    /// <summary>The dialog's toolbar names, which include "Address: &lt;folder&gt;" once it has navigated.</summary>
    private static string Address(AutomationElement dialog) => string.Join(" | ", Array.ConvertAll(
        dialog.FindAllDescendants(search => search.ByControlType(ControlType.ToolBar)),
        bar => bar.Properties.Name.ValueOrDefault));

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

    /// <summary>Waits for the viewer to log a new outcome line starting with <paramref name="outcome"/>.</summary>
    /// <returns>The line, which names the file written.</returns>
    /// <remarks>
    /// **The log, not the status bar.** The status label carries the same sentence, but a long path
    /// makes it wider than the strip, WinForms then drops the label as unavailable, and UIA reads the
    /// status as empty — two runs read '' for two minutes after an export that had succeeded.
    /// </remarks>
    private static string WaitForOutcome(string outcome, int before)
    {
        string marker = "] " + outcome + " ";
        bool logged = Retry.WhileFalse(
            () => _viewer.Count(marker) > before || _viewer.Count("] Failed: ") > 0,
            WorkTimeout).Success;

        string? line = _viewer.LastLine(marker);
        (logged && line is not null).ShouldBeTrue(
            $"the viewer never logged '{outcome} …'; last failure: '{_viewer.LastLine("] Failed: ")}'\n" + DescribeWindows());
        // The log is CRLF and lines are split on the LF, so each keeps its CR.
        return line.TrimEnd();
    }
}
