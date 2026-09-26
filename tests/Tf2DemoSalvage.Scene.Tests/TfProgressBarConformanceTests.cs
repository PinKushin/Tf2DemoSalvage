using Tf2DemoSalvage.Scene.Hud;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>`CTFProgressBar::Paint` (tf_time_panel.cpp:50): the round timer's pie, counting down counter-clockwise.</summary>
/// <remarks>A 100 × 100 bar: inactive 5 6 7 8, active 1 2 3 4, warning 9 9 9 9, warning at the default 0.75.</remarks>
public sealed class TfProgressBarConformanceTests
{
    [Test]
    public void Paint_NoTimePassed_IsTheWholeDialThenAFullCircle()
    {
        TextRecorder surface = Painted(0f);

        surface.Polygons.ShouldBe(
        [
            "0,0/0,0 100,0/1,0 100,100/1,1 0,100/0,1",
            "50,0/0.5,0 100,0/1,0 100,100/1,1 50,100/0.5,1",
            "0,50/0,0.5 50,50/0.5,0.5 50,100/0.5,1 0,100/0,1",
            "0,0/0,0 50,0/0.5,0 50,50/0.5,0.5 0,50/0,0.5",
        ]);
        surface.Calls.ShouldContain("color 1 2 3 4");
    }

    [Test]
    public void Paint_ATenthLeft_IsAWedgePastTwelve()
    {
        // 0.1 of a turn is 36°, under 45: one wedge from twelve o'clock, its edge at tan( 36° ) of the half height.
        TextRecorder surface = Painted(0.9f);

        surface.Polygons.ShouldBe(["0,0/0,0 100,0/1,0 100,100/1,1 0,100/0,1", "50,0/0.5,0 86.33,0/0.863,0 50,50/0.5,0.5 50,0/0.5,0"]);
    }

    [Test]
    public void Paint_AQuarterLeft_IsTheTopRightQuadrant()
    {
        TextRecorder surface = Painted(0.75f);

        surface.Polygons.ShouldBe(
        [
            "0,0/0,0 100,0/1,0 100,100/1,1 0,100/0,1",
            "50,0/0.5,0 100,0/1,0 100,50/1,0.5 50,50/0.5,0.5",
            "50,50/0.5,0.5 100,50/1,0.5 100,50/1,0.5 50,50/0.5,0.5",
        ]);
    }

    [Test]
    public void Paint_AtTheWarningPercent_UsesTheWarningColour()
    {
        TextRecorder surface = Painted(0.75f);

        surface.Calls.ShouldContain("color 9 9 9 9");
        surface.Calls.ShouldNotContain("color 1 2 3 4");
    }

    [Test]
    public void Paint_ThreeQuartersLeft_HalfThenTheBottomLeftWedge()
    {
        // 0.25 passed leaves 270°, the first branch; 270° exactly is not past 315°, so its wedge is the degenerate one at nine.
        TextRecorder surface = Painted(0.25f);

        surface.Polygons.ShouldBe(
        [
            "0,0/0,0 100,0/1,0 100,100/1,1 0,100/0,1",
            "50,0/0.5,0 100,0/1,0 100,100/1,1 50,100/0.5,1",
            "0,50/0,0.5 50,50/0.5,0.5 50,100/0.5,1 0,100/0,1",
            "0,50/0,0.5 0,50/0,0.5 50,50/0.5,0.5 0,50/0,0.5",
        ]);
    }

    [Test]
    public void Paint_TheTexture_IsTheProgressBar()
    {
        Painted(0.5f).Calls.ShouldContain("texture hud/objectives_timepanel_progressbar");
    }

    private static TextRecorder Painted(float percent)
    {
        TfProgressBar bar = new(null, "TimePanelProgressBar") { Wide = 100, Tall = 100 };
        TextRecorder surface = new();

        bar.SetAnimationValue("color_inactive", ((byte)5, (byte)6, (byte)7, (byte)8));
        bar.SetAnimationValue("color_active", ((byte)1, (byte)2, (byte)3, (byte)4));
        bar.SetAnimationValue("color_warning", ((byte)9, (byte)9, (byte)9, (byte)9));
        bar.SetAnimationValue("percent_warning", 0.75f);
        bar.Percentage = percent;
        bar.Paint(surface, null!);

        return surface;
    }
}
