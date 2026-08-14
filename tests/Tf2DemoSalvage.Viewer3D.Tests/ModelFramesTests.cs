using System.Collections.Generic;

using Tf2DemoSalvage.Viewer3D;

namespace Tf2DemoSalvage.Viewer3D.Tests;

/// <summary>
/// Choosing which baked animation frame a prop draws.
/// </summary>
/// <remarks>
/// **The server does not send a cycle, so the viewer advances its own.**
/// <c>C_BaseAnimating::FrameAdvance</c> adds <c>interval * cyclerate * playbackrate</c> every
/// frame and treats a networked cycle as an occasional correction. Replaying only what was
/// networked leaves every health pack frozen on frame zero, which is what they looked like.
///
/// **The numbers here are cp_process's real medkit**, measured: thirty frames at 0.3448 cycles a
/// second, a 2.9 second loop. At TF2's 0.015 second tick that is 193 ticks per loop and 6.7 ticks
/// per frame — which is the arithmetic that makes these tests sensitive. Sampling ten ticks apart,
/// as a first check did, is one and a half frames and can round to the same answer; the owner
/// caught that and put the odds at about one in thirty.
/// </remarks>
public sealed class ModelFramesTests
{
    /// <summary>cp_process's medkit: thirty frames, 0.3448 cycles a second.</summary>
    private const float CyclesPerSecond = 0.3448f;

    private const int Frames = 30;

    /// <summary>How long one loop takes, in seconds.</summary>
    private const double Period = 1d / CyclesPerSecond;

    private static PropModels.ModelFrames Medkit() =>
        new(
            [.. new List<IReadOnlyList<PropVertex>>(new IReadOnlyList<PropVertex>[Frames])],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
            {
                [0] = (0, Frames, CyclesPerSecond),
            },
            [0],
            [true]);

    [Test]
    public void OneWholePeriodLater_TheSameFrameIsDrawn()
    {
        // **The measurement that a wrong rate cannot survive.** Any cycle rate makes the frame
        // change over time, so "it moved" proves nothing. Returning to the SAME frame exactly one
        // period later proves the rate itself, because a wrong one drifts and never comes back.
        PropModels.ModelFrames medkit = Medkit();

        int start = medkit.Frame(sequence: 0, cycle: 0f, seconds: 7.5d);

        medkit.Frame(sequence: 0, cycle: 0f, seconds: 7.5d + Period).ShouldBe(start);
        medkit.Frame(sequence: 0, cycle: 0f, seconds: 7.5d + (Period * 3)).ShouldBe(start);
    }

    [Test]
    public void HalfAPeriodLater_TheFrameIsAsFarAwayAsItCanBe()
    {
        // Half a loop is the maximum separation, and choosing it deliberately is the difference
        // between a test that detects the animation and one that might alias onto the same frame.
        PropModels.ModelFrames medkit = Medkit();

        int start = medkit.Frame(sequence: 0, cycle: 0f, seconds: 0d);
        int half = medkit.Frame(sequence: 0, cycle: 0f, seconds: Period / 2);

        int apart = System.Math.Abs(half - start);

        apart.ShouldBeGreaterThan(
            (Frames / 2) - 3, "half a loop apart should be about half the frames apart");
    }

    [Test]
    public void AFrozenCycleStillAdvances_BecauseTheServerNeverSendsOne()
    {
        // Every prop in the corpus reports cycle exactly zero at every tick. If the drawn frame
        // followed only that, nothing would ever animate - which is the defect this fixes, stated
        // as the condition that produced it.
        PropModels.ModelFrames medkit = Medkit();

        medkit.Frame(sequence: 0, cycle: 0f, seconds: 0d)
            .ShouldNotBe(medkit.Frame(sequence: 0, cycle: 0f, seconds: Period / 2));
    }

    [Test]
    public void AnUnsentSequence_IsSequenceZeroRatherThanNothing()
    {
        // **Absent is not unknown.** A property that never changes from its default is never sent,
        // so every health pack in the corpus reports sequence -1 while animating perfectly well in
        // game. Treating that as "no animation" is what kept them still.
        PropModels.ModelFrames medkit = Medkit();

        medkit.Frame(sequence: -1, cycle: 0f, seconds: Period / 2)
            .ShouldBe(medkit.Frame(sequence: 0, cycle: 0f, seconds: Period / 2));
    }

    [Test]
    public void ASequenceTheModelDoesNotHave_DrawsItsFirstFrame()
    {
        // Reached when a demo names a sequence added in a later game version than the model on
        // this machine. A prop that vanishes is a worse answer than one standing still.
        Medkit().Frame(sequence: 99, cycle: 0f, seconds: 12d).ShouldBe(0);
    }

    [Test]
    public void AModelThatDoesNotAnimate_StaysOnItsOnlyFrame()
    {
        // A static prop has one frame and a zero cycle rate. Advancing time must not walk off it,
        // and the frame count must not become a divisor.
        PropModels.ModelFrames still = new(
            [.. new List<IReadOnlyList<PropVertex>>(new IReadOnlyList<PropVertex>[1])],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
            {
                [0] = (0, 1, 0f),
            },
            [0],
            [true]);

        still.IsStill.ShouldBeTrue();
        still.Frame(sequence: 0, cycle: 0f, seconds: 1000d).ShouldBe(0);
    }
}
