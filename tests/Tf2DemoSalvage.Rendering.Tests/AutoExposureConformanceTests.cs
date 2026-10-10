using System;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>TF2's auto-exposure, `viewpostprocess.cpp` and <c>materialsystem.dll 0x180035fa0</c> (B514).</summary>
/// <remarks>
/// The histogram, target and goal are published (`viewpostprocess.cpp:615-713`, `:778-812`, `:1130-1182`); the walk is
/// read in disassembly: rate 2 (<c>mat_hdr_manual_tonemap_rate</c> doubled under algorithm 1), raised walking down toward
/// 6 by <c>(current − goal)·4·⅔</c>, times frame time, capped at 1/64 a frame, nothing at all at a frame time of zero.
/// </remarks>
public sealed class AutoExposureConformanceTests
{
    /// <summary>A histogram whose brightest 2% starts at the bottom of bin <paramref name="bright"/>, the rest in bin 8.</summary>
    /// <remarks>Bin 8 (0.354-0.422) keeps the median far above the 3% floor, so the floor never overrides the target.</remarks>
    private static int[] Frame(int bright)
    {
        int[] counts = new int[AutoExposure.Bins];
        counts[8] = 98;
        counts[bright] += 2;
        return counts;
    }

    [Test]
    public void Range_NoController_IsTheCvars()
    {
        AutoExposure.Range(false, 0f, false, 0f).ShouldBe((0.5f, 2f));
    }

    /// <remarks>cp_process_f12's controller; a zero is ignored, and a min above the max raises the max.</remarks>
    [Test]
    public void Range_TheMapsController_PinsItAndIgnoresZero()
    {
        AutoExposure.Range(true, 0.5f, true, 0.7f).ShouldBe((0.5f, 0.7f));
        AutoExposure.Range(true, 0f, true, 0.7f).ShouldBe((0.5f, 0.7f));
        AutoExposure.Range(true, 1.5f, true, 0.7f).ShouldBe((1.5f, 1.5f));
    }

    /// <remarks>
    /// The 2% border sits at the bottom of the brightest occupied bin when exactly 2% are in it: bin 15's lower edge
    /// <c>(15/16)^1.5</c> = 0.9077, so the target is 0.6 / 0.9077 times the scale the frame was drawn at.
    /// </remarks>
    [Test]
    public void TargetScalar_BrightestTwoPercentAtBinFifteen_IsSixtyPercentOverItsEdge()
    {
        AutoExposure.TargetScalar(Frame(15), 1f).ShouldBe(0.6f / MathF.Pow(15f / 16f, 1.5f), 1e-4f);
        AutoExposure.TargetScalar(Frame(15), 0.5f).ShouldBe(0.3f / MathF.Pow(15f / 16f, 1.5f), 1e-4f);
    }

    /// <remarks>Bin 11 spans 0.570-0.650 and holds 60%: the sticky bin keeps the scale where it is.</remarks>
    [Test]
    public void TargetScalar_BorderInTheBinHoldingSixtyPercent_HoldsTheScale()
    {
        AutoExposure.TargetScalar(Frame(11), 0.8f).ShouldBe(0.8f, 1e-6f);
    }

    [Test]
    public void Walk_ZeroFrameTime_HoldsTheScale()
    {
        AutoExposure exposure = new();
        exposure.Update(Frame(15), (0.5f, 0.7f), 0f);

        exposure.Current.ShouldBe(1f);
    }

    /// <remarks>
    /// Walking down from 1 to 0.5: rate = min(6, 0.5·4·⅔ + 2) = 3.333, times 1/60 s = 0.0556, capped at 1/64 = 0.015625, so
    /// one frame takes 1 − 0.5·0.015625 = 0.9921875.
    /// </remarks>
    [Test]
    public void Walk_OneSixtiethDown_IsCappedAtOneSixtyFourth()
    {
        AutoExposure exposure = new();
        exposure.Update(Frame(15), (0.5f, 0.5f), 1f / 60f);

        exposure.Goal.ShouldBe(0.5f);
        exposure.Current.ShouldBe(0.9921875f, 1e-6f);
    }

    /// <remarks>Walking up from 0.5 to 0.7 at 1/1000 s: rate 2 · 0.001 = 0.002, under the cap.</remarks>
    [Test]
    public void Walk_UpSlowly_StepsByRateTimesFrameTime()
    {
        AutoExposure exposure = new();
        exposure.Reset(0.5f);
        exposure.Update(Frame(0), (0.7f, 0.7f), 0.001f);

        exposure.Current.ShouldBe(0.5f + (0.002f * 0.2f), 1e-6f);
    }

    [Test]
    public void Reset_AfterAWalk_JumpsAndEmptiesTheHistory()
    {
        AutoExposure exposure = new();

        for (int frame = 0; frame < 20; frame++)
        {
            exposure.Update(Frame(15), (0.5f, 0.7f), 1f / 60f);
        }

        exposure.Reset(1f);

        exposure.Current.ShouldBe(1f);
        exposure.Goal.ShouldBe(1f);
    }

    /// <remarks>A frame of pixels at linear 0.3 settles where the 2% border (0.3) reaches the sticky bin (0.570-0.650).</remarks>
    [Test]
    public void Update_AFlatFrameOverManyFrames_SettlesIntoTheStickyBin()
    {
        AutoExposure exposure = new();

        for (int frame = 0; frame < 2000; frame++)
        {
            int[] counts = new int[AutoExposure.Bins];

            for (int pixel = 0; pixel < 100; pixel++)
            {
                AutoExposure.Count(counts, MathF.Min(1f, 0.3f * exposure.Current));
            }

            exposure.Update(counts, (0.5f, 2f), 1f / 60f);
        }

        (0.3f * exposure.Current).ShouldBeInRange(AutoExposure.BinMin(11), AutoExposure.BinMax(11));
    }
}
