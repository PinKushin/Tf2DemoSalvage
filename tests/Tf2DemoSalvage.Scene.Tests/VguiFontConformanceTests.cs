using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Which glyph set a scheme font becomes, as `vgui2.dll`'s `ReloadFontGlyphs` (0x18000ebf0) chooses it.</summary>
/// <remarks>
/// The first numbered entry whose `yres "min max"` covers the screen tall wins — a lone minimum means exactly that height,
/// no range means any; a proportional font whose entry has no `yres` scales `tall`, `blur` and `scanlines` (and forces
/// antialias when that changed `tall`); `tall` is clamped to 255 and to the language minimum (13 CJK, 18 Thai).
/// </remarks>
public sealed class VguiFontConformanceTests
{
    private const string Fonts = """
        Fonts
        {
            "Ranged"
            {
                "1" { "name" "Small" "tall" "10" "yres" "480 599" }
                "2" { "name" "Exact" "tall" "12" "yres" "720" }
                "3" { "name" "Any" "tall" "14" "weight" "700" "outline" "1" }
            }
            "Scaled"
            {
                "1" { "name" "TF2 Build" "tall" "24" "blur" "2" }
            }
        }
        """;

    [TestCase(500, "Small", 10)]
    [TestCase(720, "Exact", 12)]
    [TestCase(721, "Any", 14)]
    [TestCase(1080, "Any", 14)]
    public void Resolve_ByScreenTall_TakesTheFirstCoveringEntry(int tall, string face, int expectedTall)
    {
        VguiFont font = Resolve("Ranged", proportional: false, tall)!;

        font.Name.ShouldBe(face);
        font.Tall.ShouldBe(expectedTall);
    }

    [Test]
    public void Resolve_AnEntryWithoutYres_IsNotScaledWhenTheHandleIsNotProportional() =>
        Resolve("Scaled", proportional: false, 1080)!.Tall.ShouldBe(24);

    [Test]
    public void Resolve_AProportionalHandle_ScalesTallAndBlurAndForcesAntialias()
    {
        VguiFont font = Resolve("Scaled", proportional: true, 1080)!;

        font.Tall.ShouldBe(54, "24 at 1080 / 480");
        font.Blur.ShouldBe(4);
        (font.Flags & VguiFont.Antialias).ShouldBe(VguiFont.Antialias);
    }

    [Test]
    public void Resolve_AProportionalHandleOnARangedEntry_IsNotScaled() =>
        Resolve("Ranged", proportional: true, 500)!.Tall.ShouldBe(10);

    [Test]
    public void Resolve_Flags_AreTheSurfacesBits()
    {
        VguiFont font = Resolve("Ranged", proportional: false, 1080)!;

        font.Weight.ShouldBe(700);
        font.Flags.ShouldBe(VguiFont.Outline);
    }

    [Test]
    public void Resolve_ATallAbove255_IsClamped() =>
        VguiFonts.Resolve(Load("""Fonts { "Huge" { "1" { "name" "X" "tall" "200" } } }"""), "Huge", true, 1080, "english")!
            .Tall.ShouldBe(255);

    [TestCase("korean", 13)]
    [TestCase("thai", 18)]
    [TestCase("english", 5)]
    public void Resolve_ALanguageMinimum_RaisesASmallTall(string language, int expected) =>
        VguiFonts.Resolve(Load("""Fonts { "Tiny" { "1" { "name" "X" "tall" "5" } } }"""), "Tiny", false, 1080, language)!
            .Tall.ShouldBe(expected);

    private static VguiFont? Resolve(string name, bool proportional, int tall) =>
        VguiFonts.Resolve(Load(Fonts), name, proportional, tall, "english");

    private static KeyValuesTree Load(string fonts) =>
        KeyValuesTree.Load(Encoding.UTF8.GetBytes("Scheme { " + fonts + " }"), "test.res", _ => null).Find("Fonts")!;
}
