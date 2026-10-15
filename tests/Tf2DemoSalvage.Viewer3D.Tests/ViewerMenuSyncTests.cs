using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// The View menu's checks follow the settings a bound key, `toggle` or `incrementvar` changed (D215): they are read
/// again each time the menu opens, and reading them sets nothing.
/// </summary>
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class ViewerMenuSyncTests
{
    private static MenuState State(bool showPosition, string volume, bool mute) => new(
        FullScreenExclusive: false,
        TextureQualities: [],
        ShowFrameRate: false,
        ShowPosition: showPosition,
        Phong: true,
        Huds: [],
        Volumes: new[] { "0", "50", "100" }.Select(key => new MenuChoice(key, key + "%", key == volume)).ToList(),
        MuteLoseFocus: mute);

    [Test]
    public void ViewMenu_OpenedAfterABindChangedTheSettings_ShowsTheSettingsAndCallsNothing()
    {
        MenuState current = State(showPosition: false, volume: "100", mute: true);
        List<string> called = [];
        using ViewerMenu menu = new(Actions(called), () => current, new KeyBindings());

        current = State(showPosition: true, volume: "50", mute: false);
        ToolStripMenuItem view = menu.Strip.Items.OfType<ToolStripMenuItem>().Single(item => item.Name == MainForm.ViewMenuId);
        typeof(ToolStripDropDownItem)
            .GetMethod("OnDropDownShow", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, [EventArgs.Empty]);

        ToolStripMenuItem volume = view.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Name == MainForm.VolumeMenuId);
        menu.PositionReadout.Checked.ShouldBeTrue();
        volume.DropDownItems.OfType<ToolStripMenuItem>().Where(item => item.Checked).Select(item => item.Name)
            .ShouldBe(["Volume50"]);
        volume.DropDownItems.OfType<ToolStripMenuItem>().Single(item => item.Name == "MuteLoseFocus").Checked.ShouldBeFalse();
        called.ShouldBeEmpty("a check that follows the settings writes none back, and saves none");
    }

    private static ViewerMenuActions Actions(List<string> called) => new(
        OpenDemo: () => called.Add("open"),
        ExportDemo: () => called.Add("export"),
        CompileDemo: () => called.Add("compile"),
        Exit: () => called.Add("exit"),
        SetFullScreen: _ => called.Add("fullscreen"),
        SetFullScreenExclusive: _ => called.Add("exclusive"),
        SetTextureQuality: _ => called.Add("quality"),
        SetSurfaceColours: _ => called.Add("colours"),
        SetFrameRateMeter: _ => called.Add("fps"),
        SetPositionReadout: _ => called.Add("showpos"),
        SetWireframe: _ => called.Add("wireframe"),
        SetFullbright: _ => called.Add("fullbright"),
        SetDrawWorld: _ => called.Add("world"),
        SetDrawEntities: _ => called.Add("entities"),
        SetDebugMode: (_, _) => called.Add("debug"),
        SetSpecular: _ => called.Add("specular"),
        SetPhong: _ => called.Add("phong"),
        Screenshot: () => called.Add("screenshot"),
        SetChosenHud: _ => called.Add("hud"),
        ChooseGameFolder: () => called.Add("folder"),
        SetVolume: _ => called.Add("volume"),
        SetMuteLoseFocus: _ => called.Add("mute"));
}
