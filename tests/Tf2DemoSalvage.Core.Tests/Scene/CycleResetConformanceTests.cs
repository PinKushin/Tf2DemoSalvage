using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// A new sequence RESETS the cycle history rather than being blended across (B383).
/// </summary>
/// <remarks>
/// **`C_BaseAnimating::PostDataUpdate`** (<c>c_baseanimating.cpp:4738</c>):
///
/// <code>
/// // reset prev cycle if new sequence
/// if (m_nNewSequenceParity != m_nPrevNewSequenceParity)
/// {
///     MDLCACHE_CRITICAL_SECTION();
///     CStudioHdr *hdr = GetModelPtr();
///     if ( hdr &amp;&amp; !( hdr->flags() &amp; STUDIOHDR_FLAGS_STATIC_PROP ) )
///         m_iv_flCycle.Reset();
/// }
/// </code>
///
/// Three separate facts, each of which a comparison of sequence NUMBERS gets wrong:
///
/// 1. **The trigger is a parity counter**, bumped in `ResetSequenceInfo`
///    (<c>m_nNewSequenceParity = ( m_nNewSequenceParity + 1 ) &amp; EF_PARITY_MASK</c>, <c>:5574</c>), so
///    it fires when the SAME animation is started again — which no comparison of sequence numbers can
///    see.
/// 2. **`Reset()` DISCARDS**, so the cycle holds at the value it has now instead of holding the OLD one.
/// 3. **A `STUDIOHDR_FLAGS_STATIC_PROP` model is exempt**, for a CPU reason Valve states in the comment
///    rather than a behavioural one — its effect is behavioural regardless.
///
/// **And `Reset()` adds THREE entries, which is the mechanism rather than a margin**
/// (<c>interpolatedvar.h:740</c>): `ClearHistory()` then three `AddToHead( gpGlobals->curtime, m_pValue,
/// false )`. Three sharing one changetime make <c>dt2</c> zero, so `GetInterpolationInfo` declines to set
/// `m_bHermite` (<c>:851</c>) and the first blend after a sequence change is LINEAR.
///
/// **What this project did instead**: kept every entry and refused to blend across a change of sequence
/// number, which holds the OLD cycle where the engine snaps to the new one, misses a restart of the same
/// animation entirely, and applies to a static-prop model that Valve exempts.
/// </remarks>
public sealed class CycleResetConformanceTests
{
    /// <summary>The track's own render delay, taken from it rather than restated (B267).</summary>
    private static readonly int Delay =
        ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    [Test]
    public void SequenceRestarted_AfterAReset_HoldsTheNewCycleRatherThanTheOld()
    {
        ScenePropTrack track = new(entityIndex: 9, "models/props_gameplay/door_slide_large_door.mdl");

        // A sequence running, sampled far enough in that the history holds several entries.
        for (int tick = 0; tick <= 40; tick += 10)
        {
            track.Add(tick, new ScenePose { Cycle = tick / 100f, Sequence = 4 });
        }

        // The animation is started AGAIN — the same sequence number, which is why the engine needs a
        // counter. The update carrying it states the cycle back at the start.
        // **The update first, then the reset** — `PostDataUpdate` latches through its base class before it
        // reaches the parity block, so the reset re-seeds from the value that just arrived rather than
        // from the previous one.
        track.Add(50, new ScenePose { Cycle = 0f, Sequence = 4 });
        track.SequenceRestarted(50);

        // **Sampled where the pair is NOT degenerate, which took a failing run to get right.** At
        // `50 + Delay` the delayed target lands exactly on the newest entry, so `Older == Newer`, the
        // fraction is zero and the cycle reads 0 whether or not anything was reset — the assertion could
        // not fail. Halfway back, the target is strictly between the older run's last entry and the new
        // one, which is the only place the two answers differ: 0.2 blended, 0 reset.
        track.At(45d + Delay).ShouldNotBeNull().Cycle.ShouldBe(
            0f, 1e-4f, "Reset() discarded the older run, so there is nothing to blend from");
    }

    [Test]
    public void SequenceRestarted_BeforeTheReset_StillAnswersFromTheOlderRun()
    {
        // **The scrub control, and it is the whole reason a reset is a boundary here rather than a
        // deletion.** The engine can delete because a live client never seeks backwards; a viewer does.
        // Asking about a moment before the reset must still give the older run's answer.
        ScenePropTrack track = new(entityIndex: 9, "models/props_gameplay/door_slide_large_door.mdl");

        for (int tick = 0; tick <= 40; tick += 10)
        {
            track.Add(tick, new ScenePose { Cycle = tick / 100f, Sequence = 4 });
        }

        // **The update first, then the reset** — `PostDataUpdate` latches through its base class before it
        // reaches the parity block, so the reset re-seeds from the value that just arrived rather than
        // from the previous one.
        track.Add(50, new ScenePose { Cycle = 0f, Sequence = 4 });
        track.SequenceRestarted(50);

        // Drawn where the delayed target lands inside the older run: 0.2 was stated at tick 20.
        track.At(20d + Delay).ShouldNotBeNull().Cycle.ShouldBe(
            0.2f, 1e-4f, "every entry is retained, so a scrub back into the older run still answers");
    }

    [Test]
    public void SequenceRestarted_ForAStaticPropModel_DoesNotReset()
    {
        // **Valve's exemption, reproduced because its EFFECT is behavioural whatever its motive.** The
        // comment gives a CPU reason — a reset would keep the entity on the interpolation list for ever —
        // but the observable consequence is that a static-prop model keeps its older cycles.
        ScenePropTrack track = new(entityIndex: 9, "models/props_c17/oildrum001.mdl");

        track.OnNewModel([], at: 0, staticPropModel: true);

        for (int tick = 0; tick <= 40; tick += 10)
        {
            track.Add(tick, new ScenePose { Cycle = tick / 100f, Sequence = 4 });
        }

        // **The update first, then the reset** — `PostDataUpdate` latches through its base class before it
        // reaches the parity block, so the reset re-seeds from the value that just arrived rather than
        // from the previous one.
        track.Add(50, new ScenePose { Cycle = 0f, Sequence = 4 });
        track.SequenceRestarted(50);

        // No reset, so all three of the older run's samples survive and the cycle SPLINES through them.
        // Predicted before it was read, and the first attempt at this line said 0.2 by forgetting the
        // spline — which is the point of writing the arithmetic out:
        //
        //   oldest 0.3 @ 30, older 0.4 @ 40, newer 0 @ 50 — evenly spaced, so no respacing
        //   no gap reaches half a cycle, so LoopingLerp_Hermite does not raise anything
        //   d1 = 0.1, d2 = -0.4, t = 0.5
        //   0.4*0.5 + 0*0.5 + 0.1*0.125 + (-0.4)*(-0.125) = 0.2 + 0.0125 + 0.05 = 0.2625
        track.At(45d + Delay).ShouldNotBeNull().Cycle.ShouldBe(
            0.2625f, 1e-4f, "a static-prop model is exempt, so its older cycles were not discarded");
    }

    [Test]
    public void OnNewModel_WithNoLoopingParameters_StillSizesTheHistoryToTheModel()
    {
        // **The width is the model's parameter COUNT and the loop flags are a separate fact**
        // (`c_baseanimating.cpp:1124` then `:1130`). A callback that reported only "which ones wrap"
        // returned an empty array for every model where none does — which is most of them — and the
        // count went with it, leaving the history one component wide and dropping the second parameter.
        ScenePropTrack track = new(entityIndex: 3, "models/buildables/sentry1.mdl");

        track.OnNewModel([false, false], at: 0, staticPropModel: false);

        track.Add(0, new ScenePose { PoseParameters = [0.2f, 0.8f] });
        track.Add(20, new ScenePose { PoseParameters = [0.6f, 0.4f] });

        ScenePose at = track.At(20d + Delay).ShouldNotBeNull();

        at.PoseParameters.Count.ShouldBe(2);
        at.PoseParameters[0].ShouldBe(0.6f, 1e-4f);

        // The second parameter is the one a width of 1 would have lost: it would read back as its stated
        // value rather than the interpolated one, because nothing would have been stored for it.
        at.PoseParameters[1].ShouldBe(0.4f, 1e-4f);
    }
}
