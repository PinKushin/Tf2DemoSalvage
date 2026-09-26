using System.Collections.Generic;
using System.Text;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`vgui::AnimationController` (vgui2/vgui_controls/AnimationController.cpp).</summary>
/// <remarks>
/// A viewport 640 × 480 (scale 1) holding a panel `Target` at 10,10 20×20. `Animate` starts from the value the panel has
/// when the animation begins; a sequence named twice keeps the first; posted commands wait for their delay.
/// </remarks>
public sealed class VguiAnimationControllerConformanceTests
{
    [Test]
    public void Animate_Alpha_IsLinearFromTheValueAtTheStart()
    {
        (VguiAnimationController controller, VguiPanel target, VguiContext context) = Rig("event Fade { Animate Target Alpha \"0\" Linear 0.0 1.0 }");

        controller.StartAnimationSequence("Fade");
        controller.UpdateAnimations(0f, context);
        controller.UpdateAnimations(0.5f, context);
        target.GetFloat("alpha").ShouldBe(127.5f);

        controller.UpdateAnimations(1f, context);
        target.GetFloat("alpha").ShouldBe(0f);
        controller.ActiveAnimationCount.ShouldBe(0);
    }

    [Test]
    public void Parse_ASequenceNamedTwice_KeepsTheFirst()
    {
        (VguiAnimationController controller, VguiPanel target, VguiContext context) = Rig("""
            event Fade { Animate Target Alpha "10" Linear 0.0 0.0 }
            event Fade { Animate Target Alpha "20" Linear 0.0 0.0 }
            """);

        controller.StartAnimationSequence("Fade");
        controller.UpdateAnimations(0f, context);

        target.GetFloat("alpha").ShouldBe(10f);
    }

    [Test]
    public void Animate_Position_MeasuresRAndCAgainstTheSizingPanel()
    {
        (VguiAnimationController controller, VguiPanel target, VguiContext context) = Rig("event Move { Animate Target Position \"r100 c-20\" Linear 0.0 0.0 }");

        controller.StartAnimationSequence("Move");
        controller.UpdateAnimations(0f, context);

        (target.X, target.Y).ShouldBe((640 - 100, 240 - 20));
    }

    [Test]
    public void Animate_Size_IsScaledWhenTheScreenIsLarger()
    {
        (VguiAnimationController controller, VguiPanel target, VguiContext context) = Rig("event Grow { Animate Target Size \"30 40\" Linear 0.0 0.0 }", tall: 960);

        controller.StartAnimationSequence("Grow");
        controller.UpdateAnimations(0f, context);

        (target.Wide, target.Tall).ShouldBe((60, 80));
    }

    [TestCase("Orange", 255, 128, 0, 255)]
    [TestCase("\"1 2 3 4\"", 1, 2, 3, 4)]
    [TestCase("Undeclared", 255, 0, 255, 255)]
    public void Animate_FgColor_IsNumbersOrASchemeColourOrPink(string target, int red, int green, int blue, int alpha)
    {
        (VguiAnimationController controller, VguiPanel panel, VguiContext context) = Rig($"event Tint {{ Animate Target FgColor {target} Linear 0.0 0.0 }}");

        controller.StartAnimationSequence("Tint");
        controller.UpdateAnimations(0f, context);

        panel.FgColor.ShouldBe(((byte)red, (byte)green, (byte)blue, (byte)alpha));
    }

    [Test]
    public void Animate_AnIntVariable_StartsFromZeroBecauseAnIntIsNotRead()
    {
        (VguiAnimationController controller, VguiPanel target, VguiContext context) = Rig("event Count { Animate Target Count \"100\" Linear 0.0 1.0 }");

        ((IntPanel)target).SetAnimationValue("Count", 60);
        controller.StartAnimationSequence("Count");
        controller.UpdateAnimations(0f, context);
        controller.UpdateAnimations(0.5f, context);

        target.GetInt("Count").ShouldBe(50, "(100 − 0) × 0.5, not (100 − 60) × 0.5 + 60");
    }

    [Test]
    public void RunEvent_IsPostedForItsDelay()
    {
        (VguiAnimationController controller, VguiPanel target, VguiContext context) = Rig("""
            event Outer { RunEvent Inner 0.5 }
            event Inner { Animate Target Alpha "0" Linear 0.0 0.0 }
            """);

        controller.UpdateAnimations(0f, context);
        controller.StartAnimationSequence("Outer");
        controller.UpdateAnimations(0.4f, context);
        target.GetFloat("alpha").ShouldBe(255f);

        controller.UpdateAnimations(0.5f, context);
        controller.UpdateAnimations(0.5f, context);
        target.GetFloat("alpha").ShouldBe(0f);
    }

    [Test]
    public void SetVisible_HidesTheNamedChild()
    {
        (VguiAnimationController controller, VguiPanel target, VguiContext context) = Rig("event Hide { SetVisible Target 0 0.0 }");

        controller.StartAnimationSequence("Hide");
        controller.UpdateAnimations(0f, context);

        target.Visible.ShouldBeFalse();
    }

    [TestCase("[$X360]", 255f)]
    [TestCase("[$WIN32]", 0f)]
    [TestCase("[!$X360]", 0f)]
    public void Parse_AConditionalAfterACommand_DecidesWhetherItStays(string condition, float expected)
    {
        (VguiAnimationController controller, VguiPanel target, VguiContext context) = Rig($"event Fade {{ Animate Target Alpha \"0\" Linear 0.0 0.0 {condition} }}");

        controller.StartAnimationSequence("Fade");
        controller.UpdateAnimations(0f, context);

        target.GetFloat("alpha").ShouldBe(expected);
    }

    [Test]
    public void StartAnimationSequence_Again_RestartsFromTheCurrentValue()
    {
        (VguiAnimationController controller, VguiPanel target, VguiContext context) = Rig("event Fade { Animate Target Alpha \"0\" Linear 0.0 1.0 }");

        controller.StartAnimationSequence("Fade");
        controller.UpdateAnimations(0f, context);
        controller.UpdateAnimations(0.5f, context);
        controller.StartAnimationSequence("Fade");
        controller.UpdateAnimations(0.5f, context);
        controller.UpdateAnimations(1.0f, context);

        target.GetFloat("alpha").ShouldBe(63.75f, "127.5 halfway to 0 again");
        controller.ActiveAnimationCount.ShouldBe(1);
    }

    [TestCase(VguiAnimationController.Interpolator.Accel, 0.25f)]
    [TestCase(VguiAnimationController.Interpolator.Deaccel, 0.70710677f)]
    [TestCase(VguiAnimationController.Interpolator.SimpleSpline, 0.5f)]
    [TestCase(VguiAnimationController.Interpolator.Bounce, 0.5f)] // the second bounce's top: sin( π/2 )
    [TestCase(VguiAnimationController.Interpolator.Linear, 0.5f)]
    public void Interpolate_Halfway_IsTheCurve(VguiAnimationController.Interpolator interpolator, float expected) =>
        VguiAnimationController.Interpolate(interpolator, 0f, 0.5f, 0f, 1f, (0f, 0f, 0f, 0f), (1f, 0f, 0f, 0f)).A.ShouldBe(expected, 0.0001f);

    [Test]
    public void Interpolate_Pulse_EndsAtOneEachWholeCycle() =>
        VguiAnimationController.Interpolate(VguiAnimationController.Interpolator.Pulse, 2f, 0.25f, 0f, 1f, (0f, 0f, 0f, 0f), (1f, 0f, 0f, 0f)).A.ShouldBe(0f, 0.0001f);

    private static (VguiAnimationController Controller, VguiPanel Target, VguiContext Context) Rig(string script, int tall = 480)
    {
        KeyValuesTree scheme = KeyValuesTree.Load(
            Encoding.UTF8.GetBytes("""Scheme { Colors { "Orange" "255 128 0 255" } Borders { } Fonts { } }"""), "scheme.res", _ => null);
        VguiScheme colours = VguiScheme.Load(scheme);
        Dictionary<string, byte[]> files = new() { ["scripts/test.txt"] = Encoding.UTF8.GetBytes(script) };
        VguiContext context = new(colours, VguiBorders.Load(scheme, colours, tall), scheme.Find("Fonts")!, tall * 4 / 3, tall, "english")
        {
            Read = files.GetValueOrDefault,
        };
        HudViewport viewport = new() { Wide = tall * 4 / 3, Tall = tall };
        IntPanel target = new(viewport) { X = 10, Y = 10, Wide = 20, Tall = 20 };
        VguiAnimationController controller = new(viewport);

        target.PerformApplySchemeSettings(context);
        controller.SetScriptFile(viewport, "scripts/test.txt", wipeAll: true, context).ShouldBeTrue();

        return (controller, target, context);
    }

    private sealed class IntPanel : VguiPanel
    {
        public IntPanel(VguiPanel parent)
            : base(parent, "Target") =>
            DeclareAnimationVar("Count", VguiPanelVarType.Whole, "0");
    }
}
