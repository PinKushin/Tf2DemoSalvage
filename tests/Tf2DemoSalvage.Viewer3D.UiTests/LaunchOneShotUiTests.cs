using System;
using System.Collections.Generic;
using System.IO;

using FlaUI.Core.Tools;

namespace Tf2DemoSalvage.Viewer3D.UiTests;

/// <summary>A one-shot on the launch line runs once the window is up, with no demo named (B829 review 1, Fable).</summary>
/// <remarks>
/// TF2's `stuffcmds` (`engine.dll` x64 build 1007 FUN_1801683d0) runs every `+command` on the line. The view registered
/// the session's <c>WindowShown</c> only when a path was named, so `tf2demoview +togglefullscreen` held the one-shot and
/// never performed it; the session's tests call <c>WindowShown</c> themselves and could not see that wiring.
/// </remarks>
[TestFixture]
public sealed class LaunchOneShotUiTests
{
    private string _root = string.Empty;

    [SetUp]
    public void CreateRoot() =>
        _root = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "tf2ds-ui-oneshot-" + Guid.NewGuid().ToString("N"))).FullName;

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
    public void Startup_ToggleFullScreenOnTheLineWithNoDemo_CoversTheScreen()
    {
        System.Drawing.Rectangle screen = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;

        using ViewerApplication viewer = ViewerApplication.Launch(
            new Dictionary<string, string?> { [Tf2DemoSalvage.Scene.ViewerSettings.PathVariable] = Path.Combine(_root, "settings.cfg") },
            askForGameFolder: false,
            "+togglefullscreen");

        bool covered = Retry.WhileFalse(
            () => viewer.Window.BoundingRectangle.Width == screen.Width && viewer.Window.BoundingRectangle.Height == screen.Height,
            TimeSpan.FromSeconds(30)).Result;

        ViewerApplication.Log($"B829: screen={screen.Width}x{screen.Height} window={viewer.Window.BoundingRectangle}");
        covered.ShouldBeTrue("the launch line's +togglefullscreen did not take the window full screen");
    }
}
