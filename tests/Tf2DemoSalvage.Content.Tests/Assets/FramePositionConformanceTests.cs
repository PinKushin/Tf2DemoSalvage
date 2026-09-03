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
    /// **A looping sequence never reaches cycle one**, because `ClampCycle` wraps it below first —
    /// so the duplicate final frame `STUDIO_LOOPING` describes is never sampled, and the mapping is
    /// the same `cycle * (frames - 1)` the engine uses for everything.
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

    /// <remarks>
    /// **The frame agrees with what `FrameFor` already answered for a loop**, which is the control
    /// against this quietly changing where an animation sits: the looping path already matched the
    /// engine's `(int)( cycle * (frames - 1) )` and only the fraction is new.
    /// </remarks>
    [Test]
    public void FrameAt_ForALoop_AgreesWithTheFrameAlone()
    {
        for (float cycle = 0f; cycle < 1f; cycle += 0.05f)
        {
            StudioSequences.FrameAt(cycle, frames: 11, loops: true).Frame.ShouldBe(
                StudioSequences.FrameFor(cycle, frames: 11, loops: true),
                $"cycle {cycle} must land on the same frame it always did");
        }
    }
}
