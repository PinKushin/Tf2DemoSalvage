using Tf2DemoSalvage.Presentation;
using Tf2DemoSalvage.Scene;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>The HUD with no install behind it: the condition the CI runner is in (docs/memory/ci-is-the-machine-without-tf2.md).</summary>
public sealed class VguiHudTests
{
    [Test]
    public void Frame_WithNoClientScheme_PaintsNothing()
    {
        // An install with no files opens as an empty `GameContent`, not a null one, so the window's `_game is null` guard
        // let the HUD run. With no `ClientScheme.res`, `Panel.BgColor` falls back to opaque white and the viewport panel
        // filled the screen with it — CI's capture was one colour, every pixel lit.
        VguiSurfaceHost host = new(_ => null, _ => null, new FpsPanelTests.SolidGdi(), _ => (0, 0));
        VguiHud hud = new(host, new EntityModelSet());

        host.BeginFrame(640, 480);
        hud.Frame(new HudState(true, true, 0, 100, true, 100, 150, 1f, 2, LocalIndex: 1, Players: []));

        host.List.Quads.ShouldBeEmpty();
        host.List.Models.ShouldBeEmpty();
    }

    /// <summary>A host whose `ClientScheme.res` exists and is empty, and which holds no other file: the HUD runs on defaults.</summary>
    /// <returns>The host.</returns>
    internal static VguiSurfaceHost EmptySchemeHost() =>
        new(path => path == "resource/ClientScheme.res" ? [] : null, _ => null, new FpsPanelTests.SolidGdi(), _ => (0, 0));
}
