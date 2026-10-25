using System.Collections.Generic;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>
/// A VGUI material is decoded once: the HUD's load-time size query keeps the texture it decoded and the renderer's first
/// draw takes it, as `SetupNewHudTexture` loads a texture once and draws from it (B595 part 6).
/// </summary>
/// <remarks>Each decode answers its own sequence number, so "the same texture" is "the same number".</remarks>
public sealed class VguiMaterialLoadsTests
{
    private readonly object _stock = new();

    [Test]
    public void Take_AfterLoad_IsTheLoadedTextureAndResolvesOnce()
    {
        List<string> resolved = [];
        VguiMaterialLoads<int> loads = new(name =>
        {
            resolved.Add(name);
            return resolved.Count;
        }, () => _stock);

        int? loaded = loads.Load("hud/d_images_v2");

        loads.Take("hud/d_images_v2").ShouldBe(loaded);
        resolved.ShouldBe(["hud/d_images_v2"]);
    }

    /// <remarks>The control: a material nothing loaded is resolved on the take, as before.</remarks>
    [Test]
    public void Take_WithoutLoad_ResolvesIt()
    {
        int asked = 0;
        VguiMaterialLoads<int> loads = new(_ => ++asked, () => _stock);

        loads.Take("hud/other").ShouldBe(1);
    }

    /// <remarks>A taken texture is the renderer's; the loads keep no second copy of it.</remarks>
    [Test]
    public void Take_Twice_ResolvesTheSecondAgain()
    {
        int asked = 0;
        VguiMaterialLoads<int> loads = new(_ => ++asked, () => _stock);

        loads.Load("hud/d_images_v2");
        loads.Take("hud/d_images_v2").ShouldBe(1);
        loads.Take("hud/d_images_v2").ShouldBe(2);
    }

    [Test]
    public void Take_AfterAHudSwitch_IsReadFromTheNewFiles()
    {
        object files = _stock;
        VguiMaterialLoads<int> loads = new(_ => ReferenceEquals(files, _stock) ? 1 : 2, () => files);

        loads.Load("hud/d_images_v2").ShouldBe(1, "the control: the stock HUD's");
        files = new object();

        loads.Take("hud/d_images_v2").ShouldBe(2);
    }
}
