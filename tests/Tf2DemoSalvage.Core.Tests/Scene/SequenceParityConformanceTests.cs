using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A prop's animation restarts when its sequence parity changes, and its cycle is measured from
/// there.
/// </summary>
/// <remarks>
/// **Owner's report, twice, about the same entity:** the spawn health cabinets first *"stay in a
/// animation loop, it doesnt stop"* and then, after the cycle clamp was corrected, *"animating once
/// to open and not closing"*. Both symptoms, one cause.
///
/// **The demo says everything needed and this project read none of it.** Measured on `cp_fulgur`,
/// cabinet 52 is told <c>seq0</c> (idle) → <c>seq1</c> (open) → <c>seq2</c> (close), and every
/// keyframe carries cycle <c>0.00</c>. `EntityModels` then computes
/// <c>elapsed = seconds - AnimationStartSeconds</c> with `AnimationStartSeconds` left at zero for
/// every prop — so `elapsed` is the WHOLE RECORDING. Before the clamp that wrapped for ever; after
/// it, it pins the last frame for ever. Neither is an animation.
///
/// **`C_BaseAnimating` measures the interval from a stamp, not from the start of time.**
/// <c>c_baseanimating.cpp:5480</c>:
///
/// <code>
///   flInterval = ( curtime - m_flAnimTime );
///   float addcycle = flInterval * cyclerate * m_flPlaybackRate;
///   float flNewCycle = GetCycle() + addcycle;
///   m_flAnimTime = curtime;              // re-stamped on every advance
/// </code>
///
/// and it knows an animation RESTARTED from a parity counter — <c>c_baseanimating.cpp:4737</c>:
///
/// <code>
///   // reset prev cycle if new sequence
///   if (m_nNewSequenceParity != m_nPrevNewSequenceParity)
///   {
///       ...
///       m_iv_flCycle.Reset();
///   }
/// </code>
///
/// **Both fields are on the wire and this project asks for neither.** `m_nNewSequenceParity` is read
/// only through `ViewmodelNewSequenceParity`, whose own remarks admit it is *"Decoded and not yet
/// acted on"* — and cite this very line. `m_flAnimTime` is cited in seven comments and decoded
/// nowhere; the schema puts it in <c>DT_AnimTimeMustBeFirst</c>, not <c>DT_BaseEntity</c>, so
/// asking under the obvious name is silently no match
/// (`docs/memory/a-property-name-needs-its-declaring-table.md`).
///
/// **This test pins the parity half**, which is what a demo can answer without reconstructing the
/// tick-base encoding `RecvProxy_AnimTime` uses: a sequence restart stamps the moment, and the
/// cycle is measured from it. The seventh half-implemented mechanism found in this session.
///
/// Synthetic (D38): the sequence changes here are ones the test put there.
/// </remarks>
public sealed class SequenceParityConformanceTests
{
    /// <summary>Entity slot the prop occupies.</summary>
    private const int Prop = 9;

    [Test]
    public void Build_APropWhoseSequenceParityChanges_RestartsItsAnimationClock()
    {
        // The measured shape: idle, then told to open. The open must be timed from when it was
        // told, not from tick zero.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            (Tick: 0, Sequence: 0, Parity: 0),
            (Tick: 660, Sequence: 1, Parity: 1)));

        ScenePropTrack track = Single(timeline);

        Started(track, at: 660).ShouldBeGreaterThan(
            0d,
            "the animation began when the server said it did, not at the start of the recording");
    }

    [Test]
    public void Build_APropWhoseSequenceNeverChanges_KeepsItsOriginalClock()
    {
        // **The control.** A prop that has been idling since the demo opened must not have its
        // clock rewritten every time it is mentioned — a restart on every update would pin every
        // looping animation to its first frame, which is the opposite failure and just as wrong.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            (Tick: 0, Sequence: 0, Parity: 0),
            (Tick: 660, Sequence: 0, Parity: 0)));

        ScenePropTrack track = Single(timeline);

        // **Read the LAST keyframe rather than one at a chosen tick.** A track records only the
        // moments a pose CHANGED, so an update that restates the same sequence, parity and position
        // is collapsed and no keyframe exists at 660 — which is right, and which the first draft of
        // this test mistook for a failure.
        Last(track).ShouldBe(
            Started(track, at: 0),
            "nothing restarted, so the clock is the one it already had");
    }

    [Test]
    public void Build_APropReplayingTheSameSequence_StillRestarts()
    {
        // **The reason the counter exists at all, and why the sequence NUMBER is not enough.** A
        // cabinet used twice plays `open` twice; the sequence is identical both times and only the
        // parity says it began again. `m_nNewSequenceParity = ( m_nNewSequenceParity + 1 ) &
        // EF_PARITY_MASK` — c_baseanimating.cpp:5574.
        DemoTimeline timeline = DemoTimeline.Build(SyntheticProp.Demo(
            (Tick: 0, Sequence: 1, Parity: 0),
            (Tick: 660, Sequence: 1, Parity: 1)));

        ScenePropTrack track = Single(timeline);

        Started(track, at: 660).ShouldBeGreaterThan(
            Started(track, at: 0),
            "the same sequence played again is a new animation, which is what the counter is for");
    }

    /// <summary>The animation clock on the track's final keyframe.</summary>
    private static double Last(ScenePropTrack track) =>
        track.Keyframes[^1].Pose.AnimationStartSeconds;

    private static ScenePropTrack Single(DemoTimeline timeline)
    {
        foreach (ScenePropTrack track in timeline.Props)
        {
            if (track.EntityIndex == Prop)
            {
                return track;
            }
        }

        throw new System.InvalidOperationException("the fixture produced no prop track");
    }

    /// <summary>When the animation showing at that tick was stamped as having begun.</summary>
    private static double Started(ScenePropTrack track, int at)
    {
        foreach ((int Tick, ScenePose Pose) frame in track.Keyframes)
        {
            if (frame.Tick == at)
            {
                return frame.Pose.AnimationStartSeconds;
            }
        }

        throw new System.InvalidOperationException($"no keyframe at tick {at}");
    }
}
