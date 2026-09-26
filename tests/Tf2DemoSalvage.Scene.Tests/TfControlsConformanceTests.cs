using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>TF's own controls: `CExLabel` (game/client/econ/econ_controls.cpp:531) and `CTFImagePanel` (tf_imagepanel.cpp).</summary>
/// <remarks>
/// `CExLabel` takes `fgcolor` — a scheme colour, default `Label.TextColor`, green when the scheme has neither — and puts it
/// back after every scheme pass. `CTFImagePanel` is a <see cref="VguiScalableImagePanel"/> whose image is `teambg_N` for
/// the local player's team, the image key otherwise.
/// </remarks>
public sealed class TfControlsConformanceTests
{
    [TestCase("\"fgcolor\" \"Orange\"", 255, 128, 0, 255)]
    [TestCase("\"fgcolor\" \"1 2 3 4\"", 1, 2, 3, 4)]
    [TestCase("", 10, 20, 30, 255)]
    [TestCase("\"fgcolor\" \"Nothing\"", 0, 255, 0, 255)]
    public void ExLabel_FgColor_SurvivesTheSchemePass(string keys, int red, int green, int blue, int alpha)
    {
        (VguiContext context, _) = Context();
        TfExLabel label = new(null, "Label");

        label.ApplySettings(Block(keys, "Label"), context);
        label.PerformApplySchemeSettings(context);

        label.FgColor.ShouldBe(((byte)red, (byte)green, (byte)blue, (byte)alpha));
    }

    [Test]
    public void ExLabel_FgColorOverride_StillWins()
    {
        (VguiContext context, _) = Context();
        TfExLabel label = new(null, "Label");

        label.ApplySettings(Block("\"fgcolor\" \"Orange\" \"fgcolor_override\" \"7 7 7 7\"", "Label"), context);
        label.PerformApplySchemeSettings(context);

        label.FgColor.ShouldBe(((byte)7, (byte)7, (byte)7, (byte)7));
    }

    [TestCase(0, "vgui/plain")]
    [TestCase(2, "vgui/red")]
    [TestCase(3, "vgui/blue")]
    [TestCase(1, "vgui/plain")]
    public void TfImagePanel_LocalTeam_PicksItsTeamBackground(int team, string texture)
    {
        (VguiContext context, TextRecorder surface) = Context();
        TfImagePanel panel = new(null, "Bg") { LocalTeam = team };

        panel.ApplySettings(Block("\"image\" \"plain\" \"teambg_2\" \"red\" \"teambg_3\" \"blue\"", "Bg"), context);
        panel.PerformApplySchemeSettings(context);
        panel.Think();
        panel.PaintBackground(surface, context);

        surface.Calls.ShouldContain($"texture {texture}");
    }

    [Test]
    public void TfImagePanel_ATeamChange_SwapsTheImage()
    {
        (VguiContext context, TextRecorder surface) = Context();
        TfImagePanel panel = new(null, "Bg") { LocalTeam = 2 };

        panel.ApplySettings(Block("\"teambg_2\" \"red\" \"teambg_3\" \"blue\"", "Bg"), context);
        panel.LocalTeam = 3;
        panel.PaintBackground(surface, context);

        surface.Calls.ShouldContain("texture vgui/blue");
    }

    [TestCase("CExLabel", typeof(TfExLabel))]
    [TestCase("CTFImagePanel", typeof(TfImagePanel))]
    public void Create_TfControls_AreRegistered(string name, System.Type type) =>
        VguiControlFactory.Create(name).ShouldBeOfType(type);

    private static KeyValuesTree Block(string keys, string name) =>
        KeyValuesTree.Load(Encoding.UTF8.GetBytes($"Resource {{ \"{name}\" {{ {keys} \"wide\" \"100\" \"tall\" \"50\" }} }}"), "test.res", _ => null)
            .Find(name)!;

    private static (VguiContext Context, TextRecorder Surface) Context()
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""
                Scheme
                {
                    Colors { "Orange" "255 128 0 255" }
                    BaseSettings { "Label.TextColor" "10 20 30 255" }
                    Borders { }
                    Fonts { "Default" { "1" { "name" "Arial" "tall" "12" } } }
                }
                """),
            "scheme.res",
            _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        TextRecorder surface = new();

        return (new VguiContext(colours, VguiBorders.Load(scheme, colours, 480), scheme.Find("Fonts")!, 640, 480, "english") { Surface = surface }, surface);
    }
}
