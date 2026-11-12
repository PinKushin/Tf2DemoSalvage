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
    /// **A sequence the model does not have is played as sequence 0, and its flag is sequence 0's** — one mapping for the
    /// pose and for the interpolator. `StandardBlendingRules` sets `SetSequence( 0 )` when `GetSequence() &gt;= hdr-&gt;GetNumSeq()`
    /// (`c_baseanimating.cpp:1967-1970`), as `OnNewModel` does (`:1172-1178`), so the next `Interpolate`'s
    /// `IsSequenceLooping( GetSequence() )` asks about 0. **A negative sequence takes the same 0** here: `ScenePose.Sequence`
    /// is never negative from a demo (the wire's `m_nSequence` is unsigned, `baseanimating.cpp:245`, and its default is 0),
    /// and every drawing consumer already reads a negative one as 0 (`PropModels.Select`), where the engine's −1 would not
    /// draw at all (`SetupBones`, `:2824`). The fixture has one looping sequence.
    /// </remarks>
    [TestCase(999)]
    [TestCase(-1)]
    public void Simulate_ASequenceTheModelDoesNotHave_IsPosedAndFlaggedAsSequenceZero(int wire)
    {
        EntityModelSet models = new();
        Func<int, bool>? told = null;
        SceneProp prop = new(
            7, "models/props_gameplay/resupply_locker.mdl", SceneModelKind.Studio, new ScenePose { X = 100f, Sequence = wire }, null);
        SceneProp[] props = [prop];

        models.ModelResolved = (_, _, _, sequenceLoops) => told = sequenceLoops;
        models.Add(props, Boxed(ModelFramesFixture.OneSecondLoop));
        models.Instances(props, [], seconds: 0.5d, frustum: default);

        told.ShouldNotBeNull().Invoke(wire).ShouldBeTrue("sequence 0 loops");
        models.FrameOf(prop.EntityIndex).ShouldNotBeNull().Sequence.ShouldBe(0);
    }

    /// <remarks>
    /// **A model drawn from baked frames is told about too** (the Fable review's question): `OnNewModel` is the entity's,
    /// whatever draws it (`c_baseanimating.cpp:1124-1130`), and a baked model's flags are its studio header's —
    /// `STUDIOHDR_FLAGS_STATIC_PROP` (0x10) and the per-sequence `STUDIO_LOOPING`. The one-triangle fixture is not skinned;
    /// its one sequence loops and it declares no pose parameter.
    /// </remarks>
    [Test]
    public void Simulate_ABakedModel_ReportsItsFlagsToo()
    {
        EntityModelSet models = new();
        (IReadOnlyList<bool> Poses, bool StaticProp, Func<int, bool> Loops)? told = null;
        SceneProp prop = new(7, "models/props_gameplay/crate.mdl", SceneModelKind.Studio, new ScenePose { X = 100f }, null);
        SceneProp[] props = [prop];

        models.ModelResolved = (_, poses, staticProp, sequenceLoops) => told = (poses, staticProp, sequenceLoops);
        models.Add(props, path => ModelFramesFixture.BoxedTriangle(path) with { StudioFlags = 0x10 });
        models.Instances(props, [], seconds: 0.5d, frustum: default);

        (IReadOnlyList<bool> poses, bool staticPropModel, Func<int, bool> loops) = told.ShouldNotBeNull("a baked model resolved");
        poses.ShouldBeEmpty();
        staticPropModel.ShouldBeTrue();
        loops(0).ShouldBeTrue();
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

    /// <remarks>
    /// **The baked path takes the same two rules** (the Fable review). A SERVER-animated prop's cycle is the received one,
    /// not advanced — the client advances only entities on its client-side animation list (`c_baseanimating.cpp:4689`) —
    /// and `CalcPoseSingle` brings it into range (`bone_setup.cpp:1977-1988`): 1.25 is the last frame, 30, and 0.5 is frame
    /// 15, at 0.5 s whatever the rate. A CLIENT-animated one advances and `FrameAdvance` ends a one-shot at ONE
    /// (`:5533-5546`): from 0 at rate 1, 1.5 s in is frame 30 with no blend. `ClampCycle` drew all three short of where the
    /// engine does. A 31-frame one-shot baked at one cycle a second.
    /// </remarks>
    [TestCase(false, 1.25f, 30, 0f)]
    [TestCase(false, 0.5f, 15, 0f)]
    [TestCase(true, 0f, 30, 0f)]
    public void SelectFor_ABakedOneShot_TakesTheReceivedOrAdvancedCycleAsTheEngineDoes(
        bool clientSideAnimated, float wire, int frame, float blend)
    {
        EntityModelSet models = new();
        SceneProp prop = new(
            7,
            "models/props_gameplay/resupply_locker.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = 100f, Cycle = wire, PlaybackRate = 1f },
            null,
            ClientSideAnimated: clientSideAnimated);

        models.Add([prop], BakedOneShot);

        (int at, int _, float between) = models.SelectFor(prop, clientSideAnimated ? 1.5d : 0.5d);

        at.ShouldBe(frame);
        between.ShouldBe(blend, 1e-4f);
    }

    private static PropModels.ModelFrames BakedOneShot(string path)
    {
        PropModels.ModelFrames triangle = ModelFramesFixture.BoxedTriangle(path);
        List<IReadOnlyList<PropVertex>> frames = [];

        for (int frame = 0; frame < 31; frame++)
        {
            frames.Add(triangle.Geometry[0]);
        }

        return triangle with
        {
            Geometry = frames,
            Layout = new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 31, 1f) },
            SequenceAnimation = [0],
            SequenceLoops = [false],
        };
    }

    private static Func<string, PropModels.ModelFrames> Boxed(Func<string, PropModels.ModelFrames> frames) =>
        path => frames(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) };
}
