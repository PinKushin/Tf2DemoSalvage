using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A SERVER-animated entity's received cycle, at its two consumers (B726): the bones and the shadow's
/// <c>ANIMATION_CHANGED</c> key. Neither goes through <c>C_BaseAnimating::ClampCycle</c>.
/// </summary>
/// <remarks>
/// **The key is <c>m_flCycle</c> as received.** <c>RecvPropFloat(RECVINFO(m_flCycle))</c> has no proxy
/// (<c>c_baseanimating.cpp:152</c>), the interpolator writes the variable unclamped (<c>Lerp_Clamp( float )</c> is empty,
/// <c>lerp_functions.h:82-84</c>; <c>LoopingLerp</c> wraps a looping one, <c>:22-39</c>), and <c>PostDataUpdate</c> and
/// <c>Interpolate</c> compare it raw (<c>:4721</c>, <c>:4485</c>).
///
/// **The bones take <c>CalcPoseSingle</c>'s own rule** (<c>bone_setup.cpp:1977-1988</c>): only a cycle outside [0, 1) is
/// touched, a looping one wrapped, any other clamped to [0, 1] — one, not <c>ClampCycle</c>'s 0.999. The fixture has 31
/// frames, so <c>fFrame = cycle × 30</c>.
///
/// **The 1023/1024 rows are the case a demo really holds.** TF2 sends the cycle in 10 bits (<c>ANIMATION_CYCLE_BITS</c>
/// under <c>TF_DLL</c>, <c>shareddefs.h:22-23</c>) with <c>SPROP_ROUNDDOWN</c> over [0, 1] (<c>baseanimating.cpp:226</c>),
/// so the high value is <c>1 − 1/1024</c> (<c>dt_send.cpp:285-286</c>) and a finished one-shot, held at 1.0 by the server
/// (<c>StudioFrameAdvanceInternal</c>, <c>baseanimating.cpp:465-475</c>), arrives as 0.9990234375 — above 0.999. The others
/// are an interpolated value's: a non-looping Hermite overshoots (the comment at <c>interpolatedvar.h:1452-1455</c>).
/// </remarks>
public sealed class WireCycleConformanceTests
{
    private const float LastStep = 1023f / 1024f;

    [TestCase(false, LastStep, 29, 0.970703125f)]
    [TestCase(false, 1f, 30, 0f)]
    [TestCase(false, 1.25f, 30, 0f)]
    [TestCase(false, -0.01f, 0, 0f)]
    [TestCase(true, LastStep, 29, 0.970703125f)]
    [TestCase(true, 1f, 0, 0f)]
    [TestCase(true, 1.25f, 7, 0.5f)]
    [TestCase(true, -0.01f, 29, 0.7f)]
    public void Simulate_AServerAnimatedWireCycle_IsTheShadowKeyRawAndCalcPoseSinglesPhase(
        bool loops, float wire, int frame, float fraction)
    {
        EntityModelSet models = new();
        SceneProp prop = new(
            7,
            "models/props_gameplay/resupply_locker.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = 100f, Cycle = wire },
            null);
        SceneProp[] props = [prop];
        List<ModelInstance> into = [];
        Func<string, PropModels.ModelFrames> load = loops ? Boxed(ModelFramesFixture.OneSecondLoop) : Boxed(ModelFramesFixture.OneSecondOneShot);

        models.Add(props, load);
        models.Instances(props, into, seconds: 0.5d, frustum: default);

        into.Count.ShouldBe(1, "the control: the prop is posed");
        models.ShadowAnimation(prop).ShouldBe((0, wire));

        (int Sequence, int Frame, float Fraction) posed = models.FrameOf(prop.EntityIndex).ShouldNotBeNull();
        posed.Frame.ShouldBe(frame);
        posed.Fraction.ShouldBe(fraction, 1e-4d);
    }

    /// <remarks>
    /// **The model says which sequence loops, and the interpolator is told** (B726): `Interpolate` sets
    /// `m_iv_flCycle`'s flag from `IsSequenceLooping( GetSequence() )` every frame (`c_baseanimating.cpp:4471-4472`), and
    /// the interpolation runs a layer below models, so `ModelResolved` carries the answer. Asked for sequence 0 of the loop
    /// and of the one-shot fixture.
    /// </remarks>
    [TestCase(true)]
    [TestCase(false)]
    public void ModelResolved_AModelsSequences_ReportsWhetherEachLoops(bool loops)
    {
        EntityModelSet models = new();
        Func<int, bool>? told = null;
        SceneProp prop = new(7, "models/props_gameplay/resupply_locker.mdl", SceneModelKind.Studio, new ScenePose { X = 100f }, null);
        SceneProp[] props = [prop];

        models.ModelResolved = (_, _, _, sequenceLoops) => told = sequenceLoops;
        models.Add(props, loops ? Boxed(ModelFramesFixture.OneSecondLoop) : Boxed(ModelFramesFixture.OneSecondOneShot));
        models.Instances(props, [], seconds: 0.5d, frustum: default);

        told.ShouldNotBeNull("the model resolved").Invoke(0).ShouldBe(loops);
    }

    /// <remarks>
    /// **And the source hands it to the entity's track**: 0.9 then 0.1, halfway, is the one-shot's `Lerp` 0.5 once the
    /// track is told sequence 4 does not loop — where a looping blend, every cycle's before, reads 0.
    /// </remarks>
    [Test]
    public void OnNewModel_ASequenceThatDoesNotLoop_IsBlendedStraightByItsTrack()
    {
        ScenePropTrack track = new(entityIndex: 7, "models/props_gameplay/resupply_locker.mdl");

        track.Add(0, new ScenePose { Cycle = 0.9f, Sequence = 4 });
        track.Add(10, new ScenePose { Cycle = 0.1f, Sequence = 4 });

        new TimelineMoments(DemoTimeline.ForTracks([track])).OnNewModel(7, [], staticProp: false, sequenceLoops: _ => false);

        track.At(5d + ScenePropTrack.DelayTicksFor(ScenePropTrack.Tf2TickInterval)).ShouldNotBeNull().Cycle.ShouldBe(0.5f, 1e-4f);
    }

    private static Func<string, PropModels.ModelFrames> Boxed(Func<string, PropModels.ModelFrames> frames) =>
        path => frames(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) };
}
