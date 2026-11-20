using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A cycle lands BETWEEN two frames, and the engine keeps the fraction.
/// </summary>
/// <remarks>
/// **<c>CalcPoseSingle</c>, <c>public/bone_setup.cpp:915</c>** — two lines, and the second is the
/// one this project never had:
///
/// <code>
/// float fFrame = cycle * (animdesc.numframes - 1);
///
/// iFrame = (int)fFrame;
/// s = (fFrame - iFrame);
/// </code>
///
/// Every bone is then sampled as <c>CalcBoneQuaternion( iFrame, s, … )</c> and
/// <c>CalcBonePosition( iFrame, s, … )</c>, a blend of frame <c>iFrame</c> with the next. Dropping
/// <c>s</c> plays an animation as its authored frames and nothing between them — about thirty poses
/// a second against a viewer drawing three hundred, which is what the owner saw as everything
/// "animating in steps" (B279).
///
/// **Truncated, not rounded.** `(int)fFrame` in C++ truncates toward zero, and the fraction is what
/// is left over. Rounding would land on the nearer frame and leave a fraction that is negative half
/// the time — the existing one-shot path rounded, which is why this pins it.
/// </remarks>
public sealed class FramePositionConformanceTests
{
    [Test]
    public void FrameAt_MidwayBetweenTwoFrames_KeepsHalfAsTheFraction()
    {
        // 11 frames means 10 intervals, so cycle 0.25 is frame 2.5.
        (int frame, float fraction) = StudioSequences.FrameAt(0.25f, frames: 11, loops: false);

        frame.ShouldBe(2);
        fraction.ShouldBe(0.5f, 1e-5f);
    }

    [Test]
    public void FrameAt_ExactlyOnAFrame_HasNoFraction()
    {
        (int frame, float fraction) = StudioSequences.FrameAt(0.2f, frames: 11, loops: false);

        frame.ShouldBe(2);
        fraction.ShouldBe(0f, 1e-5f);
    }

    /// <remarks>
    /// **A one-shot holds its last frame and must not ask for one past it.** At cycle one the
    /// engine's `fFrame` is exactly `numframes - 1`, so the fraction is zero and the next frame is
    /// never needed — but a cycle nudged past one by an advance must not walk off the end either.
    /// </remarks>
    [Test]
    public void FrameAt_AtTheEndOfAOneShot_HoldsTheLastFrameWithNoFraction()
    {
        (int frame, float fraction) = StudioSequences.FrameAt(1f, frames: 11, loops: false);

        frame.ShouldBe(10);
        fraction.ShouldBe(0f, 1e-5f);
    }

    /// <remarks>
    /// **A cycle outside [0, 1) takes <c>CalcPoseSingle</c>'s own rule** (<c>bone_setup.cpp:1977-1988</c>, B726): a
    /// looping one keeps its fraction (<c>cycle - (int)cycle</c>, plus one below zero), any other is clamped to [0, 1] —
    /// a one-shot past its end holds the last frame and one before its start the first, never wraps.
    /// </remarks>
    [TestCase(false, 1.25f, 10, 0f)]
    [TestCase(false, -0.25f, 0, 0f)]
    [TestCase(true, 1.25f, 2, 0.5f)]
    [TestCase(true, -0.25f, 7, 0.5f)]
    // A loop just below zero wraps to exactly one in float (`-1e-10 + 1`): `fFrame` is `numframes - 1`, `s` zero, and the
    // engine samples the LAST frame (`bone_setup.cpp:1056`), not frame 0 (B60 review).
    [TestCase(true, -1e-10f, 10, 0f)]
    // A one-shot's infinite cycle clamps like any other (`:1986`): the last frame, or the first (B60 review).
    [TestCase(false, float.PositiveInfinity, 10, 0f)]
    [TestCase(false, float.NegativeInfinity, 0, 0f)]
    public void FrameAt_ACycleOutsideTheRange_TakesCalcPoseSinglesRule(bool loops, float cycle, int frame, float fraction)
    {
        (int at, float into) = StudioSequences.FrameAt(cycle, frames: 11, loops);

        at.ShouldBe(frame);
        into.ShouldBe(fraction, 1e-5f);
    }

    /// <remarks>
    /// **No frames, or a cycle the engine leaves undefined, is frame zero with nothing to blend** — the guards the deleted
    /// <c>StudioSequences.FrameFor</c>'s tests held (B60 review): a zero-frame animation must not answer frame -1; a NaN
    /// cycle, a loop's infinite one, and a loop's cycle below <c>int</c> range (whose wrap the engine's <c>(int)</c> leaves
    /// negative) must not answer a frame the animation does not have. These are ours: the engine's answer is undefined.
    /// </remarks>
    [TestCase(0.7f, 0, false)]
    [TestCase(float.NaN, 11, false)]
    [TestCase(float.PositiveInfinity, 11, true)]
    [TestCase(-3e9f, 11, true)]
    public void FrameAt_NoFramesOrAnUndefinedCycle_IsFrameZero(float cycle, int frames, bool loops)
    {
        StudioSequences.FrameAt(cycle, frames, loops).ShouldBe((0, 0f));
    }

    /// <remarks>
    /// **A loop's last interval blends toward its last frame**: the mapping is the same `cycle * (frames - 1)` the engine
    /// uses for everything, and the frame after 9 is 10, not 0 (B60 review).
    /// </remarks>
    [Test]
    public void FrameAt_JustBelowTheEndOfALoop_IsInTheLastInterval()
    {
        (int frame, float fraction) = StudioSequences.FrameAt(0.95f, frames: 11, loops: true);

        frame.ShouldBe(9);
        fraction.ShouldBe(0.5f, 1e-5f);
    }

    /// <remarks>
    /// A single-frame animation — a pose holder, of which TF2 has many — has nothing to blend
    /// toward, and asking for frame 1 of a one-frame animation is how a reader walks off the end.
    /// </remarks>
    [Test]
    public void FrameAt_OnASingleFrameAnimation_IsAlwaysFrameZero()
    {
        (int frame, float fraction) = StudioSequences.FrameAt(0.7f, frames: 1, loops: false);

        frame.ShouldBe(0);
        fraction.ShouldBe(0f, 1e-5f);
    }
}
