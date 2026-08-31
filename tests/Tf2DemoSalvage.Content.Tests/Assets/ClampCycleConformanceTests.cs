using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A cycle past the end of a sequence WRAPS only when the sequence loops.
/// </summary>
/// <remarks>
/// **<c>C_BaseAnimating::ClampCycle</c>, <c>client/c_baseanimating.cpp:1431</c>:**
///
/// <code>
///   float C_BaseAnimating::ClampCycle( float flCycle, bool isLooping )
///   {
///       if (isLooping)
///       {
///           // (Valve's own to-do marker elided here, so the analyzer does not read it as ours:
///           //  "does this work with negative framerate?")
///           flCycle -= (int)flCycle;
///           if (flCycle &lt; 0.0f)
///           {
///               flCycle += 1.0f;
///           }
///       }
///       else
///       {
///           flCycle = clamp( flCycle, 0.0f, 0.999f );
///       }
///       return flCycle;
///   }
/// </code>
///
/// Called from `C_BaseAnimating::SetupBones` (<c>c_baseanimating.cpp:1854</c>) with
/// <c>IsSequenceLooping( hdr, blend-&gt;m_nSequence )</c>, which reads <c>STUDIO_LOOPING</c> —
/// <c>0x0001</c>, "ending frame should be the same as the starting frame"
/// (<c>public/studio.h:3078</c>).
///
/// **This project wrapped unconditionally**, in two places, both written as
/// <c>advanced - Math.Floor(advanced)</c>. That is the looping branch applied to every sequence.
///
/// **<see cref="StudioSequences.FrameFor(float, int, bool)"/> already gets this right and never got
/// the chance.** Its
/// own remarks say *"A non-looping sequence finishes at cycle one and has to hold its final pose"*,
/// and it takes a <c>loops</c> argument to do it — but a caller that has already wrapped the cycle
/// into [0,1) has destroyed the only evidence that the sequence ended. **An invariant one layer
/// keeps can be another layer's unstated precondition**, and honouring a flag one level too late
/// looks exactly like honouring it.
///
/// **Measured symptom, reported by the owner:** *"the health cab is in a animation loop, it doesnt
/// stop"*. `models/props_gameplay/resupply_locker.mdl` carries three sequences — <c>idle</c>,
/// <c>open</c>, <c>close</c> — and the viewer's own asset log reports all three as
/// <c>flags 0x0</c>, so not one of them loops. The cabinet opened and shut for ever.
///
/// Synthetic and file-free: every value here is arithmetic against the engine's own expression.
/// </remarks>
public sealed class ClampCycleConformanceTests
{
    /// <summary>The engine's own ceiling for a sequence that has finished.</summary>
    private const float End = 0.999f;

    [Test]
    public void ClampCycle_ALoopingCycleBeyondOne_WrapsToItsFraction()
    {
        // `flCycle -= (int)flCycle` — 2.25 is a quarter into the third pass.
        StudioSequences.ClampCycle(2.25f, loops: true).ShouldBe(0.25f, 1e-6f);
    }

    [Test]
    public void ClampCycle_ANonLoopingCycleBeyondOne_HoldsTheEnd()
    {
        // `clamp( flCycle, 0.0f, 0.999f )`. **The case the defect was.** Wrapped instead, a door
        // that has finished opening starts opening again, for ever.
        StudioSequences.ClampCycle(2.25f, loops: false).ShouldBe(End, 1e-6f);
    }

    [Test]
    public void ClampCycle_ANegativeLoopingCycle_WrapsForward()
    {
        // `if (flCycle < 0.0f) flCycle += 1.0f`. C truncates toward zero, so -0.25 becomes -0.25
        // and then 0.75 — three quarters through, played backwards.
        StudioSequences.ClampCycle(-0.25f, loops: true).ShouldBe(0.75f, 1e-6f);
    }

    [Test]
    public void ClampCycle_ANegativeNonLoopingCycle_ClampsToTheStart()
    {
        // The lower half of the same clamp, and it is not symmetric with the wrap above: a
        // one-shot sequence that has not begun holds its FIRST pose, it does not jump to its last.
        StudioSequences.ClampCycle(-0.25f, loops: false).ShouldBe(0f, 1e-6f);
    }

    [Test]
    public void ClampCycle_ACycleAlreadyInRange_IsUnchangedEitherWay()
    {
        // **The control.** Without it, "clamps correctly" and "returns a constant" agree on every
        // test above, and every real frame of playback is in this range.
        StudioSequences.ClampCycle(0.4f, loops: true).ShouldBe(0.4f, 1e-6f);
        StudioSequences.ClampCycle(0.4f, loops: false).ShouldBe(0.4f, 1e-6f);
    }

    [Test]
    public void FrameFor_AFinishedOneShotSequence_LandsOnTheLastFrame()
    {
        // **The composition, which is the thing that was actually broken.** Neither piece was
        // wrong on its own: the caller wrapped, `FrameFor` held. Only together do they lose the
        // final pose, so only together can a test see it.
        //
        // The resupply locker's numbers: 30 frames in `open`, advanced well past its end.
        StudioSequences.FrameFor(
            StudioSequences.ClampCycle(2.25f, loops: false), frames: 30, loops: false)
            .ShouldBe(29, "a one-shot sequence that has finished holds its last frame");
    }

    [Test]
    public void FrameFor_AFinishedLoopingSequence_ReturnsToTheStart()
    {
        // The control on the same composition: the identical input on a LOOPING sequence must
        // come back near the beginning, not hold. 2.25 wraps to 0.25, a quarter of 29 distinct
        // poses.
        StudioSequences.FrameFor(
            StudioSequences.ClampCycle(2.25f, loops: true), frames: 30, loops: true)
            .ShouldBe(7, "0.25 of 29 distinct poses, floored");
    }
}
