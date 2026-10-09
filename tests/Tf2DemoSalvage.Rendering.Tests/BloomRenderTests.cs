using System;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>TF2's 8-bit bloom through the real passes, with predicted pixels (B514).</summary>
/// <remarks>
/// **A flat frame makes the prediction exact.** Every tap of every pass reads the same value, so the blur's thirteen
/// weights (sum 0.9999, `BlurFilter_ps2x.fxc:56-79`) only scale, and the bloom is the downsample's
/// <c>pow( g, 2.2 ) · dot( g, (0.3, 0.59, 0.11) )</c> = g^3.2 for a grey g (`Downsample_nohdr_ps2x.fxc:34-35`), stored
/// in eight bits, blurred, times <c>$bloomamount</c>, stored again, and ADDED to the gamma frame.
/// </remarks>
public sealed class BloomRenderTests
{
    private static byte Store(double value) => (byte)Math.Clamp(Math.Round(value * 255.0), 0.0, 255.0);

    private static int Predict(int stored, float amount)
    {
        double g = stored / 255.0;
        byte down = Store(Math.Pow(g, 3.2));
        byte across = Store(down / 255.0 * 0.9999);
        byte bloom = Store(across / 255.0 * 0.9999 * amount);

        return Math.Min(255, stored + bloom);
    }

    [TestCase(1f)]
    [TestCase(0.5f)]
    public void DrawBloom_AFlatGreyFrame_AddsGreyCubedPointTwoInGamma(float amount)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D");
            return;
        }

        target.Clear(0.5f, 0.5f, 0.5f);
        int before = target.PixelAt(32, 32).Red;

        target.DrawBloom(amount);
        (int red, int green, int blue) = target.PixelAt(32, 32);

        TestContext.Out.WriteLine($"BLOOM {before} -> {red} at {amount}, predicted {Predict(before, amount)}");

        before.ShouldBe(188, "the control: linear 0.5 is stored as sRGB 188");
        red.ShouldBeInRange(Predict(before, amount) - 1, Predict(before, amount) + 1);
        green.ShouldBe(red);
        blue.ShouldBe(red);
    }

    [Test]
    public void DrawBloom_ABlackFrame_StaysBlack()
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D");
            return;
        }

        target.Clear(0f, 0f, 0f);
        target.DrawBloom(1f);

        target.PixelAt(32, 32).ShouldBe((0, 0, 0));
    }

    /// <remarks>
    /// **The vertical blur steps one over the WIDTH** (`BlurFilterY.cpp:88-89`), so on a frame twice as wide as tall it
    /// spreads half as far vertically; on a square one the two agree, which is the control.
    /// </remarks>
    [TestCase(256, 128, true)]
    [TestCase(128, 128, false)]
    public void DrawBloom_ABrightDot_SpreadsLessVerticallyOnAWideFrame(int width, int height, bool wide)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(width, height);

        if (target is null)
        {
            Assert.Ignore("no Direct3D");
            return;
        }

        // A white 16-pixel square at the centre, drawn a row at a time.
        int cx = width / 2;
        int cy = height / 2;
        ((float X, float Y) From, (float X, float Y) To)[] rows = new ((float, float), (float, float))[16];

        for (int row = 0; row < rows.Length; row++)
        {
            float y = 1f - ((cy - 8 + row + 0.5f) * 2f / height);
            rows[row] = ((((cx - 8) * 2f / width) - 1f, y), (((cx + 8) * 2f / width) - 1f, y));
        }

        target.Clear(0f, 0f, 0f);
        target.DrawLines(rows);
        target.PixelAt(cx, cy).Red.ShouldBe(255, "the control: the square is drawn");
        target.DrawBloom(1f);

        // 32 pixels past the centre is 24 past the square's edge: six quarter-size texels, where the vertical blur on a
        // 2:1 frame reaches only three.
        int across = target.PixelAt(cx + 32, cy).Red;
        int down = target.PixelAt(cx, cy + 32).Red;

        TestContext.Out.WriteLine($"BLOOM {width}x{height}: 32 px across {across}, 32 px down {down}");

        across.ShouldBeGreaterThan(0, "the control: the dot must bloom at all");

        if (wide)
        {
            across.ShouldBeGreaterThan(down);
        }
        else
        {
            across.ShouldBeInRange(down - 1, down + 1);
        }
    }
}
