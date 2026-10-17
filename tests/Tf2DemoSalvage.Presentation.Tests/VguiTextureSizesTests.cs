using System.Collections.Generic;

using Tf2DemoSalvage.Presentation;

namespace Tf2DemoSalvage.Presentation.Tests;

/// <summary>A VGUI texture's size is read once per name per set of files, as `DrawSetTextureFile` loads a texture once.</summary>
/// <remarks>
/// **The profile that found it**: f12 at 30000, each of twelve spectator cards' `ScalableImageBorder.Paint` asked
/// `DrawGetTextureSize`, which resolved the material out of the VPK — a file open — every frame.
/// </remarks>
public sealed class VguiTextureSizesTests
{
    private readonly object _stock = new();

    [Test]
    public void Size_AskedTwiceForOneName_ResolvesItOnceAndAnswersTheSame()
    {
        List<string> resolved = [];
        VguiTextureSizes sizes = new(name =>
        {
            resolved.Add(name);
            return (64, 32);
        }, () => _stock);

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
        }, () => _stock);

        sizes.Size("vgui/missing").ShouldBe((0, 0));
        sizes.Size("vgui/missing").ShouldBe((0, 0));

        asked.ShouldBe(1);
    }

    /// <remarks>
    /// **A HUD switch remakes the files, and the sizes go with them** — red in review: a custom HUD's border is not the stock
    /// one's size. The same name, read under the stock files, then a custom HUD's: answered from each.
    /// </remarks>
    [Test]
    public void Size_AfterAHudSwitch_IsReadFromTheNewFiles()
    {
        object custom = new();
        object files = _stock;
        VguiTextureSizes sizes = new(_ => ReferenceEquals(files, _stock) ? (64, 32) : (128, 16), () => files);

        sizes.Size("vgui/border").ShouldBe((64, 32), "the control: the stock HUD's");

        files = custom;

        sizes.Size("vgui/border").ShouldBe((128, 16));
    }

    /// <remarks>Before an install is open nothing resolves, and that must not be remembered as "the material is missing".</remarks>
    [Test]
    public void Size_BeforeTheFilesAreOpen_IsNotKept()
    {
        object? files = null;
        VguiTextureSizes sizes = new(_ => files is null ? (0, 0) : (64, 32), () => files);

        sizes.Size("vgui/border").ShouldBe((0, 0));

        files = _stock;

        sizes.Size("vgui/border").ShouldBe((64, 32));
    }
}
