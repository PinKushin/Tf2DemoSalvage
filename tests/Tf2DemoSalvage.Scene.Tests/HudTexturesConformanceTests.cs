using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CHud::Init`'s icons and `CHudTexture::DrawSelf` (game/client/hud.cpp:58, :464, :585).</summary>
/// <remarks>The recorder's textures are all 64 × 64 and its glyphs 10 wide in a 12-tall font.</remarks>
public sealed class HudTexturesConformanceTests
{
    private const string HudFile = """
        "sprites/640_hud"
        {
            TextureData
            {
                "crosshair" { "file" "sprites/qi_center" "x" "0" "y" "0" "width" "40" "height" "40" }
                "xbox_only" [$X360] { "file" "sprites/x" "x" "0" "y" "0" "width" "1" "height" "1" }
                "Shared" { "file" "sprites/first" "x" "0" "y" "0" "width" "8" "height" "8" }
            }
        }
        """;

    private const string ModFile = """
        "sprites/640_hud"
        {
            TextureFileRefs { "dfile" { "prefix" "d_" } "dnegfile" { "prefix" "dneg_" } }
            TextureData
            {
                "scattergun" { "dfile" "HUD/d_images" "dnegfile" "HUD/dneg_images" "x" "96" "y" "192" "width" "96" "height" "32" }
                "plus_sign" { "font" "Icons" "character" "+" }
                "shared" { "file" "sprites/second" "x" "0" "y" "0" "width" "4" "height" "4" }
            }
        }
        """;

    [Test]
    public void Load_AFileReference_MakesAPrefixedIconForEach()
    {
        HudTextures textures = Loaded(out _);

        textures.GetIcon("d_scattergun")!.TextureFile.ShouldBe("HUD/d_images");
        textures.GetIcon("dneg_scattergun")!.TextureFile.ShouldBe("HUD/dneg_images");
        textures.GetIcon("scattergun").ShouldBeNull("it names no plain `file`");
        textures.GetIcon("D_SCATTERGUN")!.Rc.ShouldBe((96, 192, 192, 224), "found without case; right and bottom are x + width, y + height");
    }

    [Test]
    public void Load_ANameInBothFiles_KeepsTheFirst() =>
        Loaded(out _).GetIcon("shared")!.TextureFile.ShouldBe("sprites/first");

    [Test]
    public void Load_AnXboxEntry_IsDropped() => Loaded(out _).GetIcon("xbox_only").ShouldBeNull();

    [Test]
    public void Load_AFontIcon_IsMeasuredAgainstTheProportionalFont()
    {
        HudTexture plus = Loaded(out _).GetIcon("plus_sign")!;

        plus.RenderUsingFont.ShouldBeTrue();
        plus.CharacterInFont.ShouldBe('+');
        plus.Rc.ShouldBe((0, 0, 10, 12));
    }

    [Test]
    public void DrawSelf_ATextureIcon_InsetsHalfATexel()
    {
        TextRecorder surface = new();

        Loaded(out _).GetIcon("crosshair")!.DrawSelf(surface, 5, 6, (255, 255, 255, 255));

        surface.SubRects.ShouldBe(["5 6 45 46 uv 0.0078125 0.0078125 0.6171875 0.6171875"]);
        surface.Calls.ShouldContain("texture sprites/qi_center");
    }

    [Test]
    public void DrawSelf_AFontIcon_DrawsItsCharacterAtThePosition()
    {
        TextRecorder surface = new();

        Loaded(out _).GetIcon("plus_sign")!.DrawSelf(surface, 7, 8, 100, 100, (1, 2, 3, 4));

        surface.Glyphs.ShouldBe(["+@7,8"]);
    }

    [Test]
    public void DrawSelfCropped_TheRightHalf_TakesTheRightHalfOfTheCoordinates()
    {
        TextRecorder surface = new();

        // crosshair: s from 0.5/64 to 39.5/64; half of 40 texels is 20.
        Loaded(out _).GetIcon("crosshair")!.DrawSelfCropped(surface, 0, 0, 20, 0, 20, 40, 20, 40, (255, 255, 255, 255));

        surface.SubRects.ShouldBe(["0 0 20 40 uv 0.3125 0.0078125 0.6171875 0.6171875"]);
    }

    [Test]
    public void EffectiveWidth_ATextureIcon_IsScaledAndTruncated() =>
        Loaded(out TextRecorder surface).GetIcon("crosshair")!.EffectiveWidth(surface, 0.99f).ShouldBe(39);

    private static HudTextures Loaded(out TextRecorder surface)
    {
        const string Scheme = """Scheme { Colors { } Borders { } Fonts { "Icons" { "isproportional" "only" "1" { "name" "Tahoma" "tall" "10" } } } }""";
        KeyValuesTree scheme = KeyValuesTree.Load(Encoding.UTF8.GetBytes(Scheme), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        Dictionary<string, byte[]> files = new()
        {
            ["scripts/hud_textures.txt"] = Encoding.UTF8.GetBytes(HudFile),
            ["scripts/mod_textures.txt"] = Encoding.UTF8.GetBytes(ModFile),
        };

        surface = new TextRecorder();
        VguiContext context = new(
            colours,
            VguiBorders.Load(scheme, colours, 480),
            scheme.Find("Fonts")!,
            640,
            480,
            "english",
            VguiSchemeFonts.Load(scheme, new VguiFontManager(new FakeGdi()), path => path, "english", 480))
        {
            Surface = surface,
            Read = files.GetValueOrDefault,
        };

        return HudTextures.Load(context);
    }
}
