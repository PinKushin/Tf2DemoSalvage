using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The cycle the CLIENT advances, at its two ends (B726): <c>C_BaseAnimating::FrameAdvance</c> for a client-animated
/// entity and <c>C_BaseViewModel::Interpolate</c> for a viewmodel. Neither is <c>ClampCycle</c>.
/// </summary>
/// <remarks>
/// **`FrameAdvance`** (<c>c_baseanimating.cpp:5533-5546</c>) touches only a cycle outside [0, 1): a looping one loses
/// its whole part by truncation, <c>flNewCycle -= (int)(flNewCycle)</c>, with no <c>+1</c>, so a negative playback rate
/// leaves it NEGATIVE; any other is set to 0 or to ONE. **`C_BaseViewModel::Interpolate`** (<c>c_baseviewmodel.cpp:175-210</c>)
/// clamps a negative elapsed time to zero, then touches only <c>dt &gt;= 1</c>: a one-shot to 0.999, a loop by
/// <c>fmod( dt, 1 )</c>. A negative <c>dt</c> is kept. The bones then take <c>CalcPoseSingle</c>'s rule
/// (<c>bone_setup.cpp:1977-1988</c>). The fixture has 31 frames at one cycle a second, so <c>fFrame = cycle × 30</c>.
/// </remarks>
public sealed class ClientCycleConformanceTests
{
    /// <remarks>
    /// One-shot past its end (1.5 s at rate 1): `FrameAdvance` stores 1.0, the last frame. A looping sequence played
    /// backwards for 0.25 s: −0.25 stored, and `CalcPoseSingle` draws 0.75, frame 22.5. Both failed with `ClampCycle`
    /// (0.999, and 0.75 stored).
    /// </remarks>
    [TestCase(false, 1f, 1.5d, 1f, 30, 0f)]
    [TestCase(true, -1f, 0.25d, -0.25f, 22, 0.5f)]
    public void Simulate_AClientAnimatedCycleOutOfRange_IsFrameAdvancesEnd(
        bool loops, float playbackRate, double seconds, float key, int frame, float fraction)
    {
        SceneProp prop = Prop() with
        {
            Pose = new ScenePose { X = 100f, Speed = 1f, PlaybackRate = playbackRate },
            ClientSideAnimated = true,
        };

        Posed(prop, loops, seconds, out EntityModelSet models);

        models.ShadowAnimation(prop).ShouldBe((0, key));
        AssertFrame(models, prop, frame, fraction);
    }

    /// <remarks>
    /// The viewmodel's own rule. A one-shot 1.5 s in holds 0.999 (frame 29.97) and a loop 1.25 s in is 0.25 (frame 7.5):
    /// the controls, which `ClampCycle` also answered. An animation that starts 0.25 s in the FUTURE is at 0, not at the
    /// wrapped 0.75; and a one-shot played backwards keeps its negative cycle, drawn at frame 0.
    /// </remarks>
    [TestCase(false, 1f, 0d, 1.5d, 0.999f, 29, 0.97f)]
    [TestCase(true, 1f, 0d, 1.25d, 0.25f, 7, 0.5f)]
    [TestCase(true, 1f, 1d, 0.75d, 0f, 0, 0f)]
    [TestCase(false, -1f, 0d, 0.25d, -0.25f, 0, 0f)]
    public void Simulate_AViewmodelCycle_IsCBaseViewModelsRule(
        bool loops, float playbackRate, double started, double seconds, float key, int frame, float fraction)
    {
        SceneProp prop = Prop() with
        {
            Pose = new ScenePose { X = 100f, PlaybackRate = playbackRate, AnimationStartSeconds = started },
            ClientSideAnimated = true,
            FirstPerson = true,
        };

        Posed(prop, loops, seconds, out EntityModelSet models);

        models.ShadowAnimation(prop).ShouldBe((0, key));
        AssertFrame(models, prop, frame, fraction);
    }

    private static void Posed(SceneProp prop, bool loops, double seconds, out EntityModelSet models)
    {
        models = new EntityModelSet();
        SceneProp[] props = [prop];
        List<ModelInstance> into = [];
        Func<string, PropModels.ModelFrames> frames = loops ? ModelFramesFixture.OneSecondLoop : ModelFramesFixture.OneSecondOneShot;

        models.Add(props, path => frames(path) with { HeaderBounds = new StudioBox(-16f, -16f, 0f, 16f, 16f, 64f) });
        models.Instances(props, into, seconds: seconds, frustum: default);

        into.Count.ShouldBe(1, "the control: the prop is posed");
    }

    private static void AssertFrame(EntityModelSet models, SceneProp prop, int frame, float fraction)
    {
        (int Sequence, int Frame, float Fraction) posed = models.FrameOf(prop.EntityIndex).ShouldNotBeNull();
        posed.Frame.ShouldBe(frame);
        posed.Fraction.ShouldBe(fraction, 1e-4d);
    }

    private static SceneProp Prop() =>
        new(7, "models/props_gameplay/resupply_locker.mdl", SceneModelKind.Studio, new ScenePose { X = 100f }, null);
}
