using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The received cycle is interpolated as LOOPING only when its sequence loops (B726, closing the open item B382 and B383
/// listed).
/// </summary>
/// <remarks>
/// **`C_BaseAnimating::Interpolate`** (<c>c_baseanimating.cpp:4471-4472</c>) sets the flag every frame, from the sequence
/// the entity is playing:
///
/// <code>
/// if ( !m_bClientSideAnimation )
///     m_iv_flCycle.SetLooping( IsSequenceLooping( GetSequence() ) );
/// </code>
///
/// A looping value is blended by <c>LoopingLerp</c>/<c>LoopingLerp_Hermite</c>, which wrap into [0, 1)
/// (<c>lerp_functions.h:22-39</c>, <c>:117-159</c>); any other by <c>Lerp</c>/<c>Lerp_Hermite</c>, and
/// <c>Lerp_Clamp( float )</c> is empty (<c>:82-84</c>), so a one-shot's Hermite overshoot is KEPT — Valve's comment at
/// <c>interpolatedvar.h:1452-1456</c> names <c>m_flCycle</c> as the variable it happens to. The model says which sequence
/// loops, so the scene tells the track at <c>OnNewModel</c>.
/// </remarks>
public sealed class CycleLoopingConformanceTests
{
    /// <summary>The track's own render delay, taken from it rather than restated (B267).</summary>
    private static readonly int Delay = ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval);

    /// <remarks>
    /// Two samples, 0.9 then 0.1, halfway between. A one-shot is `Lerp`: 0.5. A loop crosses the wrap: 0.9 → 1.1, halfway
    /// 1.0, wrapped to 0 — the control, and what every sequence got before.
    /// </remarks>
    [TestCase(false, 0.5f)]
    [TestCase(true, 0f)]
    public void At_TwoSamplesHalfACycleApart_BlendTheWayTheSequenceLoops(bool loops, float expected)
    {
        ScenePropTrack track = Track(loops);

        track.Add(0, new ScenePose { Cycle = 0.9f, Sequence = 4 });
        track.Add(10, new ScenePose { Cycle = 0.1f, Sequence = 4 });

        track.At(5d + Delay).ShouldNotBeNull().Cycle.ShouldBe(expected, 1e-4f);
    }

    /// <remarks>
    /// **The overshoot is kept**: 0 @ 0, 0.9 @ 10, then TF2's top wire step 1023/1024 @ 20, evenly spaced, sampled halfway
    /// through the last interval. `Lerp_Hermite` with d1 = 0.9, d2 = 0.0990234375, t = 0.5:
    /// 0.9 × 0.5 + 0.9990234375 × 0.5 + 0.9 × 0.125 − 0.0990234375 × 0.125 = 1.049633789. A loop's
    /// `LoopingLerp_Hermite` lifts p0 by one (0.9 apart) and reads 0.924633789 — the control.
    /// </remarks>
    [TestCase(false, 1.049633789f)]
    [TestCase(true, 0.924633789f)]
    public void At_AOneShotSplinedTowardItsEnd_KeepsTheHermiteOvershoot(bool loops, float expected)
    {
        ScenePropTrack track = Track(loops);

        track.Add(0, new ScenePose { Cycle = 0f, Sequence = 4 });
        track.Add(10, new ScenePose { Cycle = 0.9f, Sequence = 4 });
        track.Add(20, new ScenePose { Cycle = 1023f / 1024f, Sequence = 4 });

        track.At(15d + Delay).ShouldNotBeNull().Cycle.ShouldBe(expected, 1e-4f);
    }

    /// <remarks>
    /// **The respacing follows the flag too** (`TimeFixup2_Hermite`, `interpolatedvar.h:1393-1400`). 0.1 @ 0, 0.9 @ 20,
    /// 0.95 @ 30: the older interval is twice the newer, so the third sample is rebuilt halfway between 0.1 and 0.9 — 0.5 by
    /// `Lerp`, where `LoopingLerp` would cross the wrap and give 0. Then `Lerp_Hermite` at t = 0.5 with d1 = 0.4,
    /// d2 = 0.05: 0.45 + 0.475 + 0.05 − 0.00625 = 0.96875. (From a wrapped 0 it would read 1.03125.)
    /// </remarks>
    [Test]
    public void At_AOneShotWithUnevenSpacing_RespacesItsThirdSampleWithoutWrapping()
    {
        ScenePropTrack track = Track(loops: false);

        track.Add(0, new ScenePose { Cycle = 0.1f, Sequence = 4 });
        track.Add(20, new ScenePose { Cycle = 0.9f, Sequence = 4 });
        track.Add(30, new ScenePose { Cycle = 0.95f, Sequence = 4 });

        track.At(25d + Delay).ShouldNotBeNull().Cycle.ShouldBe(0.96875f, 1e-4f);
    }

    /// <remarks>
    /// **A track never told its model is not looping**, which is the engine's answer with no model: `IsSequenceLooping` is
    /// `GetSequenceFlags( pStudioHdr, iSequence ) &amp; STUDIO_LOOPING` (`c_baseanimating.cpp:5587-5590`), and
    /// `GetSequenceFlags` returns 0 for a null header, a model with no sequences or a sequence out of range
    /// (`game/shared/animation.cpp:572-580`). So 0.9 then 0.1 is `Lerp`'s 0.5, not the wrap's 0.
    /// </remarks>
    [Test]
    public void At_ATrackNeverToldItsModel_BlendsStraight()
    {
        ScenePropTrack track = new(entityIndex: 9, "models/props_gameplay/resupply_locker.mdl");

        track.Add(0, new ScenePose { Cycle = 0.9f, Sequence = 4 });
        track.Add(10, new ScenePose { Cycle = 0.1f, Sequence = 4 });

        track.At(5d + Delay).ShouldNotBeNull().Cycle.ShouldBe(0.5f, 1e-4f);
    }

    private static ScenePropTrack Track(bool loops)
    {
        ScenePropTrack track = new(entityIndex: 9, "models/props_gameplay/resupply_locker.mdl");

        track.OnNewModel([], at: 0, staticPropModel: false, sequenceLoops: sequence => sequence == 4 && loops);
        return track;
    }
}
