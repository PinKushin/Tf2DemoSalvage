using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A scheme's fonts made into handles, as `vgui2.dll`'s `CScheme_LoadFonts` and `ReloadFontGlyphs` do.</summary>
/// <remarks>
/// `CScheme_LoadFonts` (0x18000e040): each `CustomFontFiles` entry is a file, or a block naming a `font` file and a face
/// `name` with a `range` (`%x %x`) under the current language; each `Fonts` entry makes a `-no` handle unless
/// `isproportional` is `only`, then a `-p` one. `GetFont` (0x18000d0c0) answers the handle for name and proportionality,
/// or none. `ReloadFontGlyphs` passes the face's registered range to `SetFontGlyphSet`.
/// </remarks>
public sealed class VguiSchemeFontsConformanceTests
{
    private const string Scheme = """
        Scheme
        {
            CustomFontFiles
            {
                "1" "resource/plain.ttf"
                "2" { "font" "resource/ranged.ttf" "name" "Ranged" "english" { "range" "0x0020 0x007f" } }
            }
            Fonts
            {
                "Both" { "1" { "name" "Tahoma" "tall" "10" } }
                "PropOnly" { "isproportional" "only" "1" { "name" "Tahoma" "tall" "10" } }
                "UsesRange" { "1" { "name" "Ranged" "tall" "10" } }
            }
        }
        """;

    [Test]
    public void Load_CustomFontFiles_AreAddedByTheirFullPaths()
    {
        FakeGdi gdi = new();

        Load(gdi);

        gdi.Added.ShouldBe(["DISK/resource/plain.ttf", "DISK/resource/ranged.ttf"]);
    }

    [Test]
    public void GetFont_APlainEntry_HasBothHandles()
    {
        VguiSchemeFonts fonts = Load(new FakeGdi());

        fonts.GetFont("Both", proportional: false).ShouldNotBeNull();
        fonts.GetFont("both", proportional: true).ShouldNotBeNull("the key is compared without case");
        fonts.GetFont("Both", proportional: true).ShouldNotBeSameAs(fonts.GetFont("Both", proportional: false));
    }

    [Test]
    public void GetFont_AnOnlyProportionalEntry_HasNoPlainHandle()
    {
        VguiSchemeFonts fonts = Load(new FakeGdi());

        fonts.GetFont("PropOnly", proportional: false).ShouldBeNull();
        fonts.GetFont("PropOnly", proportional: true).ShouldNotBeNull();
    }

    [Test]
    public void GetFont_AnUnknownName_IsNone() => Load(new FakeGdi()).GetFont("Nothing", proportional: false).ShouldBeNull();

    [Test]
    public void ReloadFontGlyphs_AFaceWithARegisteredRange_IsSetWithIt()
    {
        VguiFontAmalgam font = Load(new FakeGdi()).GetFont("UsesRange", proportional: false)!;

        font.GetFontForChar(0x20)!.GlyphSet.Name.ShouldBe("Ranged");
        font.GetFontForChar(0x80)!.GlyphSet.Name.ShouldBe("Tahoma", "above the english range");
    }

    private static VguiSchemeFonts Load(FakeGdi gdi)
    {
        KeyValuesTree root = KeyValuesTree.Load(Encoding.UTF8.GetBytes(Scheme), "scheme.res", _ => null);

        return VguiSchemeFonts.Load(root, new VguiFontManager(gdi), path => "DISK/" + path, "english", 480);
    }
}
