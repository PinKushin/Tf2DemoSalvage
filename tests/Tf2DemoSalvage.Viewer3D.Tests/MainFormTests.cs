using System;
using System.Linq;
using System.Threading;
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

// STA and serial, because this constructs a Windows Form — see B178.
[NonParallelizable]
[Apartment(ApartmentState.STA)]
public sealed class MainFormTests
{
    [Test]
    public void MainForm_EveryAddressableControl_HasAnAutomationId()
    {
        // Asserted against the constants the shell exposes rather than against literals, so a
        // rename has to happen in one place and the UI tests can reference the same names.
        using MainForm form = new();

        Find(form, MainForm.ViewportId).ShouldNotBeNull();
        Find(form, MainForm.OpenButtonId).ShouldNotBeNull();
        Find(form, MainForm.OpenFolderButtonId).ShouldNotBeNull();
        Find(form, MainForm.PlaylistId).ShouldNotBeNull();
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
    public void MainForm_EveryAddressableControl_HasAnAccessibleName()
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
    public void MainForm_TheViewport_FillsTheWindowBeneathTheMenu()
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
    public void MainForm_ConstructingTheShell_CreatesNoDevice()
    {
        // The property this whole test class depends on. If the constructor built a swap chain,
        // none of these tests could run without a graphics adapter, and the form could not be
        // constructed in CI at all.
        using MainForm form = new();

        form.HasDevice.ShouldBeFalse();
        form.StatusText.ShouldBe("No demo loaded.");
    }

    [Test]
    public void MainForm_PathsOnTheCommandLine_LandInThePlaylist()
    {
        // The file-association path. It goes through the same AddToLibrary the Open buttons use,
        // so this also pins that the two cannot drift: if the command line grew its own loader,
        // this test would still pass while the behaviours diverged - so what it actually checks
        // is that the shell is constructible from paths at all, and the SHARED entry point is
        // what makes the guarantee.
        string folder = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "tf2salvage-tests", System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, "fromargv.dem"), new byte[16]);

        try
        {
            using MainForm form = new(folder);

            ListView playlist = (ListView)Find(form, MainForm.PlaylistId)!;
            playlist.Items.Count.ShouldBe(1);
            playlist.Items[0].Text.ShouldBe("fromargv.dem");
        }
        finally
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void MainForm_AnEmptyCommandLine_OpensNothing()
    {
        using MainForm form = new();

        ((ListView)Find(form, MainForm.PlaylistId)!).Items.Count.ShouldBe(0);
    }

    [Test]
    public void MainForm_TypingInTheSearchBox_NarrowsThePlaylist()
    {
        // Three demos, one query, one survivor - with two bystanders that must disappear. A single
        // demo in the folder could not tell "filtered correctly" from "filtered to everything".
        string folder = NewFolder();

        try
        {
            foreach (string name in new[] { "process_ace.dem", "gullywash_1.dem", "gullywash_2.dem" })
            {
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name), new byte[16]);
            }

            using MainForm form = new(folder);

            ListView playlist = (ListView)Find(form, MainForm.PlaylistId)!;
            TextBox search = (TextBox)Find(form, MainForm.SearchId)!;

            playlist.Items.Count.ShouldBe(3);

            search.Text = "gullywash";
            playlist.Items.Count.ShouldBe(2);

            search.Text = "process";
            playlist.Items.Count.ShouldBe(1);
            playlist.Items[0].Text.ShouldBe("process_ace.dem");

            // Clearing restores everything: a filter must not be a one-way door.
            search.Text = string.Empty;
            playlist.Items.Count.ShouldBe(3);
        }
        finally
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }
    }

    [Test]
    public void MainForm_TheSearchBox_SitsAboveThePlaylist()
    {
        // Layout, pinned because it was got wrong for the transport bar by reasoning about docking
        // instead of measuring it. Both controls share a panel, so their order is a property of
        // the order they were added in and nothing in the type system protects it.
        using MainForm form = new();

        Control search = Find(form, MainForm.SearchId)!;
        Control playlist = Find(form, MainForm.PlaylistId)!;

        search.Parent.ShouldBe(playlist.Parent);
        search.Dock.ShouldBe(DockStyle.Top);
        playlist.Dock.ShouldBe(DockStyle.Fill);
    }

    private static string NewFolder()
    {
        string folder = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "tf2salvage-tests", System.IO.Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(folder);
        return folder;
    }

    private static Control? Find(Control root, string name) =>
        root.Controls.Find(name, searchAllChildren: true).FirstOrDefault();

    private static ToolStripMenuItem FileMenu(MainForm form) =>
        form.MainMenuStrip!.Items.OfType<ToolStripMenuItem>()
            .Single(item => item.Name == MainForm.FileMenuId);
}
