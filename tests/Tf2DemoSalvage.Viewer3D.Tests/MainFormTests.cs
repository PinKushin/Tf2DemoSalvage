using System;
using System.Linq;
using System.Windows.Forms;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Tests the viewer shell's structure and its automation surface.
/// </summary>
/// <remarks>
/// **These run without a display and without a GPU**, which is the point of building the form
/// with no side effects in its constructor. The device is created when the viewport panel gets a
/// window handle, and nothing here shows the form, so no swap chain is ever made — that keeps
/// these usable in CI on a machine with no graphics adapter.
///
/// What they pin down is the part the UI tests depend on: every control automation can address
/// has a stable <see cref="Control.Name"/>, which is what UIA reports as AutomationId. A renamed
/// control breaks an automation script silently — the script simply fails to find it and reports
/// a missing element, which reads like an application bug rather than a rename.
/// </remarks>
[TestFixture]
public sealed class MainFormTests
{
    [Test]
    public void EveryAddressableControlHasItsAutomationId()
    {
        // Asserted against the constants the shell exposes rather than against literals, so a
        // rename has to happen in one place and the UI tests can reference the same names.
        using MainForm form = new();

        Find(form, MainForm.ViewportId).ShouldNotBeNull();
        Find(form, MainForm.ImportButtonId).ShouldNotBeNull();
        Find(form, MainForm.ExportButtonId).ShouldNotBeNull();
        Find(form, MainForm.CompileButtonId).ShouldNotBeNull();
        Find(form, TransportBar.PlayButtonId).ShouldNotBeNull();
        form.Name.ShouldBe("MainWindow");

        ToolStripMenuItem file = FileMenu(form);
        file.Name.ShouldBe(MainForm.FileMenuId);
        file.DropDownItems.OfType<ToolStripMenuItem>()
            .Select(item => item.Name)
            .ShouldBe([MainForm.OpenDemoItemId, MainForm.ExitItemId]);
    }

    [Test]
    public void EveryAddressableControlHasAnAccessibleName()
    {
        // Separate from the id and not interchangeable with it: the id is a stable identifier
        // that must not be translated, the accessible name is prose a screen reader reads out.
        // A control with an id and no name is addressable by a test and silent to a user.
        using MainForm form = new();

        form.AccessibleName.ShouldNotBeNullOrWhiteSpace();
        Find(form, MainForm.ViewportId)!.AccessibleName.ShouldBe("Demo viewport");
        FileMenu(form).AccessibleName.ShouldBe("File menu");
    }

    [Test]
    public void TheViewportFillsTheWindowBeneathTheMenu()
    {
        // Docking order is easy to get wrong in a way that looks fine until the window is
        // resized. **WinForms docks in REVERSE collection order** - the last control added docks
        // first and claims its edge, and whatever is Fill takes what is left. So the Fill control
        // must come EARLIER in the collection than the edges, which reads backwards and is why
        // the first version of this assertion was inverted.
        using MainForm form = new();

        Control viewport = Find(form, MainForm.ViewportId)!;

        viewport.Dock.ShouldBe(DockStyle.Fill);
        form.Controls.IndexOf(viewport)
            .ShouldBeLessThan(form.Controls.IndexOf(form.MainMenuStrip!));
    }

    [Test]
    public void ConstructingTheShellCreatesNoDevice()
    {
        // The property this whole test class depends on. If the constructor built a swap chain,
        // none of these tests could run without a graphics adapter, and the form could not be
        // constructed in CI at all.
        using MainForm form = new();

        form.HasDevice.ShouldBeFalse();
        form.StatusText.ShouldBe("No demo loaded.");
    }

    private static Control? Find(Control root, string name) =>
        root.Controls.Find(name, searchAllChildren: true).FirstOrDefault();

    private static ToolStripMenuItem FileMenu(MainForm form) =>
        form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
            .Single(item => item.Name == MainForm.FileMenuId);
}
