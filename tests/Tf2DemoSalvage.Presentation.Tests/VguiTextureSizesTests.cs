using System.Collections.Generic;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A VGUI texture's size is read once per name, as `DrawSetTextureFile` loads a texture once (B-perf, 2026-10-17).</summary>
/// <remarks>
/// **The profile that found it**: f12 at 30000, each of twelve spectator cards' `ScalableImageBorder.Paint` asked
/// `DrawGetTextureSize`, which resolved the material out of the VPK — a file open — every frame. The engine's
/// `ISurface::DrawSetTextureFile` keys its texture by name and loads it once; the size is the texture's.
/// </remarks>
public sealed class VguiTextureSizesTests
{
    [Test]
    public void Size_AskedTwiceForOneName_ResolvesItOnceAndAnswersTheSame()
    {
        List<string> resolved = [];
        VguiTextureSizes sizes = new(name =>
        {
            resolved.Add(name);
            return (64, 32);
        });

        sizes.Size("vgui/border").ShouldBe((64, 32));
        sizes.Size("vgui/border").ShouldBe((64, 32));
        sizes.Size("vgui/other").ShouldBe((64, 32));

        resolved.ShouldBe(["vgui/border", "vgui/other"]);
    }

    /// <remarks>A material that does not resolve is not asked again either — the engine binds its error texture once.</remarks>
    [Test]
    public void Size_ForAMissingMaterial_ResolvesItOnce()
    {
        int asked = 0;
        VguiTextureSizes sizes = new(_ =>
        {
            asked++;
            return (0, 0);
        });

        sizes.Size("vgui/missing").ShouldBe((0, 0));
        sizes.Size("vgui/missing").ShouldBe((0, 0));

        asked.ShouldBe(1);
    }
}
