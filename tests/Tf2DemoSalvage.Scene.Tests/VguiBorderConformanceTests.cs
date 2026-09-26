using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>A scheme's `Borders`, as `vgui2.dll`'s `CScheme_LoadBorders` (0x18000dc40) builds them.</summary>
/// <remarks>
/// Blocks first, in order: a name already registered is re-applied with the later block and keeps its type; a new one is
/// made by `bordertype` (`image`, `scalable_image`, anything else a line border). Then string entries, each an alias to
/// whatever `GetBorder` answers for its value. Lookup (0x18000cf30) is by KeyValues symbol, so case-insensitive, first
/// entry winning; `GetBorder` (0x18000cfa0) falls back to `BaseBorder`.
/// </remarks>
public sealed class VguiBorderConformanceTests
{
    private const int ScreenTall = 1080;

    [Test]
    public void Load_ABlockWithNoType_IsALineBorderWithItsInsetAndSides()
    {
        VguiLineBorder border = (VguiLineBorder)Borders("""
            "Frame"
            {
                "inset" "1 2 3 4"
                "backgroundtype" "2"
                "Left" { "1" { "color" "Orange" "offset" "0 1" } "2" { "color" "10 20 30 40" } }
            }
            """).Get("Frame")!;

        border.Inset.ShouldBe([1, 2, 3, 4]);
        border.BackgroundType.ShouldBe(2);
        border.ProportionalScalar.ShouldBe(1f);
        border.Side(VguiLineBorder.Left).ShouldBe([
            new VguiBorderLine((255, 128, 0, 255), 0, 1),
            new VguiBorderLine((10, 20, 30, 40), 0, 0),
        ]);
        border.Side(VguiLineBorder.Top).ShouldBeEmpty();
    }

    [Test]
    public void Load_AnImageBorder_PrefixesVguiAndPaintsFirstByDefault()
    {
        VguiImageBorder border = (VguiImageBorder)Borders("""
            "Pic" { "bordertype" "image" "image" "hud/frame" "tiled" "1" }
            """).Get("Pic")!;

        border.Image.ShouldBe("vgui/hud/frame");
        border.Tiled.ShouldBeTrue();
        border.PaintFirst.ShouldBeTrue();
    }

    [Test]
    public void Load_AScalableImageBorder_ScalesTheDrawnCornersAndNotTheSourceOnes()
    {
        VguiScalableImageBorder border = (VguiScalableImageBorder)Borders("""
            "Box"
            {
                "bordertype" "scalable_image" "image" "box" "paintfirst" "0"
                "src_corner_height" "8" "src_corner_width" "9" "draw_corner_height" "8" "draw_corner_width" "4"
            }
            """).Get("Box")!;

        border.SourceCornerHeight.ShouldBe(8);
        border.SourceCornerWidth.ShouldBe(9);
        border.DrawCornerHeight.ShouldBe(18, "8 at 1080 / 480");
        border.DrawCornerWidth.ShouldBe(9);
        border.PaintFirst.ShouldBeFalse();
        border.Color.ShouldBe(((byte)255, (byte)255, (byte)255, (byte)255), "no color is white");
    }

    [Test]
    public void Load_AScalableImageBorderColour_ResolvesThroughTheScheme() =>
        ((VguiScalableImageBorder)Borders("""
            "Box" { "bordertype" "scalable_image" "color" "Orange" }
            """).Get("Box")!).Color.ShouldBe(((byte)255, (byte)128, (byte)0, (byte)255));

    [Test]
    public void Load_AStringEntry_AliasesTheNamedBorder()
    {
        VguiBorders borders = Borders("""
            "Alias" "Frame"
            "Frame" { "inset" "1 1 1 1" }
            """);

        borders.Get("Alias").ShouldBeSameAs(borders.Get("Frame"));
    }

    [Test]
    public void Get_AnUnknownName_IsBaseBorder()
    {
        VguiBorders borders = Borders("""
            "BaseBorder" { }
            """);

        borders.Get("Nothing").ShouldBeSameAs(borders.Get("BaseBorder"));
    }

    [Test]
    public void Get_AnUnknownNameWithoutABaseBorder_IsNull() =>
        Borders("""
            "Frame" { }
            """).Get("Nothing").ShouldBeNull();

    [Test]
    public void Get_IsCaseInsensitive()
    {
        VguiBorders borders = Borders("""
            "Frame" { }
            """);

        borders.Get("FRAME").ShouldBeSameAs(borders.Get("frame"));
        borders.Get("FRAME").ShouldNotBeNull();
    }

    [Test]
    public void Load_ADuplicateName_ReappliesTheLaterBlockAndKeepsTheFirstType()
    {
        VguiImageBorder border = (VguiImageBorder)Borders("""
            "Pic" { "bordertype" "image" "image" "first" }
            "Pic" { "image" "second" }
            """).Get("Pic")!;

        border.Image.ShouldBe("vgui/second");
    }

    [Test]
    public void Load_ADuplicateWithAShortInset_KeepsTheValuesItDoesNotName()
    {
        // `sscanf( inset, "%d %d %d %d" )` writes only what it reads, into the inset `GetInset` already returned.
        VguiLineBorder border = (VguiLineBorder)Borders("""
            "Frame" { "inset" "1 2 3 4" "Top" { "1" { "color" "Orange" } } }
            "Frame" { "inset" "9" }
            """).Get("Frame")!;

        border.Inset.ShouldBe([9, 2, 3, 4]);
        border.Side(VguiLineBorder.Top).Count.ShouldBe(1, "a side the later block omits is kept");
    }

    private static VguiBorders Borders(string body)
    {
        KeyValuesTree root = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("Scheme { Colors { \"Orange\" \"255 128 0 255\" } Borders { " + body + " } }"),
            "test.res",
            _ => null);

        return VguiBorders.Load(root, VguiScheme.Load(root), ScreenTall);
    }
}
