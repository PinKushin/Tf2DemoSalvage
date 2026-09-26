using System.Collections.Generic;
using System.Linq;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`vgui::Label` (vgui2/vgui_controls/Label.cpp): one text image, aligned in the panel and coloured by the scheme.</summary>
/// <remarks>
/// `ComputeAlignment` (:396) places the image by the nine alignments, west when it is wider than the label; `Paint` (:499)
/// adds the x inset on the left, takes it off on the right, and centres y for west, center and east; a disabled label
/// draws twice, offset by one in `Label.DisabledFgColor1` and then in place in `Label.DisabledFgColor2`.
/// `ApplySchemeSettings` (:990) sizes the image to its content and takes `Label.TextColor`, `TextDullColor` or
/// `TextBrightColor`; `PerformLayout` (:1308) shrinks the image to the label less its inset.
/// Every glyph here is 10 wide and 12 tall (<see cref="TextRecorder"/>), so "ab" is 20 by 12.
/// </remarks>
public sealed class VguiLabelConformanceTests
{
    private const int ScreenTall = 480;

    [TestCase("north-west", "a@0,0")]
    [TestCase("north", "a@40,0")]
    [TestCase("north-east", "a@80,0")]
    [TestCase("west", "a@0,9")]
    [TestCase("center", "a@40,9")]
    [TestCase("east", "a@80,9")]
    [TestCase("south-west", "a@0,18")]
    [TestCase("south", "a@40,18")]
    [TestCase("south-east", "a@80,18")]
    [TestCase("nonsense", "a@0,9")]
    public void Paint_EachAlignment_PlacesTheTextByComputeAlignment(string alignment, string first) =>
        Painted($"\"textAlignment\" \"{alignment}\"")[0].ShouldBe(first);

    [Test]
    public void Paint_TextInsetX_IsAddedOnTheLeftAndTakenOffOnTheRight()
    {
        Painted("\"textAlignment\" \"west\" \"textinsetx\" \"5\"")[0].ShouldBe("a@5,9");
        Painted("\"textAlignment\" \"east\" \"textinsetx\" \"5\"")[0].ShouldBe("a@75,9");
        Painted("\"textAlignment\" \"center\" \"textinsetx\" \"5\"")[0].ShouldBe("a@40,9", "centred text ignores the x inset");
    }

    [Test]
    public void Paint_TextInsetY_MovesTheTextDown() =>
        Painted("\"textAlignment\" \"north-west\" \"textinsety\" \"3\"")[0].ShouldBe("a@0,3");

    [Test]
    public void Paint_ProportionalInsets_ScaleXAndCeilAFractionalY()
    {
        // At 960 tall: x 5 → 10; y 2.5 → ceil(2.5 × 2000 / 1000) = 5. Without the key, y is `GetInt` of "2.5": 2.
        Painted("\"textAlignment\" \"north-west\" \"textinsetx\" \"5\" \"textinsety\" \"2.5\" \"use_proportional_insets\" \"1\"", screenTall: 960)[0]
            .ShouldBe("a@10,5");
        Painted("\"textAlignment\" \"north-west\" \"textinsetx\" \"5\" \"textinsety\" \"2.5\"", screenTall: 960)[0].ShouldBe("a@5,2");
    }

    [Test]
    public void Paint_ImageWiderThanTheLabel_AlignsWestWithoutLayout() =>
        Painted("\"textAlignment\" \"east\" \"wide\" \"15\"", layout: false)[0].ShouldBe("a@0,9");

    [Test]
    public void Paint_Enabled_DrawsInTheSchemeTextColor()
    {
        TextRecorder surface = PaintLabel("\"textAlignment\" \"west\"");

        surface.Calls.ShouldContain("text color 10 20 30 255");
        surface.Glyphs.ShouldBe(["a@0,9", "b@10,9"]);
    }

    [Test]
    public void Paint_Disabled_DrawsOffsetInColor1ThenInPlaceInColor2()
    {
        TextRecorder surface = PaintLabel("\"textAlignment\" \"west\" \"enabled\" \"0\"");

        surface.Glyphs.ShouldBe(["a@1,10", "b@11,10", "a@0,9", "b@10,9"]);
        surface.Calls.Where(call => call.StartsWith("text color", System.StringComparison.Ordinal))
            .ShouldBe(["text color 1 1 1 255", "text color 2 2 2 255"]);
    }

    [TestCase("", 10, 20, 30)]
    [TestCase("\"dulltext\" \"1\"", 40, 50, 60)]
    [TestCase("\"brighttext\" \"1\"", 70, 80, 90)]
    public void ApplySchemeSettings_TextColorState_PicksTheSchemeColor(string keys, int red, int green, int blue)
    {
        VguiLabel label = Built(keys, Context(ScreenTall));

        label.FgColor.ShouldBe(((byte)red, (byte)green, (byte)blue, (byte)255));
        label.BgColor.ShouldBe(((byte)5, (byte)6, (byte)7, (byte)8), "Label.BgColor");
    }

    [Test]
    public void ApplySchemeSettings_FgColorOverride_WinsOverTheLabelColor() =>
        Built("\"fgcolor_override\" \"9 9 9 9\"", Context(ScreenTall)).FgColor.ShouldBe(((byte)9, (byte)9, (byte)9, (byte)9));

    [Test]
    public void ApplySchemeSettings_AutoWideAndTall_SizeTheLabelToItsContent()
    {
        VguiLabel label = Built("\"textinsetx\" \"4\" \"auto_wide_tocontents\" \"1\"", Context(ScreenTall));

        (label.Wide, label.Tall).ShouldBe((24, 30), "text 20 plus the x inset; tall untouched");

        label = Built("\"textinsety\" \"4\" \"auto_tall_tocontents\" \"1\"", Context(ScreenTall));

        (label.Wide, label.Tall).ShouldBe((100, 16), "max(image 12 + y inset 4, content 12); wide untouched");
    }

    [Test]
    public void PerformLayout_TextWiderThanTheLabel_ShrinksTheImageToEndInDots() =>
        Painted("\"textAlignment\" \"west\" \"wide\" \"15\" \"labelText\" \"abc\"").ShouldBe(["a@0,9", ".@10,9", ".@14,9", ".@18,9"], "all three dots draw; the panel's clip cuts them");

    [Test]
    public void ApplySettings_LabelText_IsLocalised() =>
        Built("\"labelText\" \"#Known\"", Context(ScreenTall)).Text.ShouldBe("Hi");

    [Test]
    public void Create_Label_IsAVguiLabel() => VguiControlFactory.Create("label").ShouldBeOfType<VguiLabel>();

    private static List<string> Painted(string keys, int screenTall = ScreenTall, bool layout = true) =>
        PaintLabel(keys, screenTall, layout).Glyphs;

    private static TextRecorder PaintLabel(string keys, int screenTall = ScreenTall, bool layout = true)
    {
        VguiContext context = Context(screenTall);
        VguiLabel label = Built(keys, context, layout);
        TextRecorder surface = (TextRecorder)context.Surface!;

        surface.Glyphs.Clear();
        surface.Calls.Clear();
        label.Paint(surface, context);

        return surface;
    }

    private static VguiLabel Built(string keys, VguiContext context, bool layout = true)
    {
        VguiLabel label = new(null, "Label");
        string body = $"\"Label\" {{ {keys} \"wide\" \"100\" \"tall\" \"30\" \"labelText\" \"ab\" }}";
        KeyValuesTree block = KeyValuesTree.Load(Encoding.UTF8.GetBytes("Resource { " + body + " }"), "test.res", _ => null).Find("Label")!;

        label.ApplySettings(block, context);
        label.PerformApplySchemeSettings(context);

        if (layout)
        {
            label.Think();
        }

        return label;
    }

    private static VguiContext Context(int screenTall)
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""
                Scheme
                {
                    BaseSettings
                    {
                        "Label.TextColor" "10 20 30 255"
                        "Label.TextDullColor" "40 50 60 255"
                        "Label.TextBrightColor" "70 80 90 255"
                        "Label.BgColor" "5 6 7 8"
                        "Label.DisabledFgColor1" "1 1 1 255"
                        "Label.DisabledFgColor2" "2 2 2 255"
                    }
                    Borders { }
                    Fonts { "Default" { "1" { "name" "Arial" "tall" "12" } } }
                }
                """),
            "scheme.res",
            _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        VguiSchemeFonts fonts = VguiSchemeFonts.Load(scheme, new VguiFontManager(new FakeGdi()), _ => null, "english", screenTall);
        Dictionary<string, string> strings = new() { ["Known"] = "Hi" };

        return new VguiContext(colours, VguiBorders.Load(scheme, colours, screenTall), scheme.Find("Fonts")!, 640, screenTall, "english", fonts)
        {
            Surface = new TextRecorder(),
            Localize = strings.GetValueOrDefault,
        };
    }
}
