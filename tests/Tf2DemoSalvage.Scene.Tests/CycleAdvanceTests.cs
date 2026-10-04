using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The exact cycle `EntityModelSet` hands the skeleton for a client-animated entity — the closed form and the integrated
/// `C_BaseAnimating::FrameAdvance` form (B172), each pinned to a frame and fraction rather than "it moved".
/// </summary>
/// <remarks>
/// **The arithmetic is the engine's**: `addcycle = flInterval * cyclerate * m_flPlaybackRate`
/// (`c_baseanimating.cpp:5493`), accumulated onto the last `m_flCycle`. The fixture plays one cycle a second over 31 frames
/// (<see cref="AnimatedStudioBytes"/>), so a cycle <c>c</c> lands on frame <c>(int)(c × 30)</c> with the remainder as the
/// fraction (`CalcPoseSingle`'s <c>iFrame = (int)fFrame; s = fFrame - iFrame</c>). Every expected cycle below is chosen so
/// that the mutated arithmetic — a sign, a divide for a multiply — lands on a different frame even after the loop wraps.
/// Half rate is used throughout because at rate one a multiply and a divide agree.
/// </remarks>
public sealed class CycleAdvanceTests
{
    /// <summary><c>STUDIO_LOOPING</c>, <c>studio.h</c>.</summary>
    private const int SequenceLooping = 0x0001;

    /// <summary>
    /// No speed means the closed form: <c>m_flCycle + (now − start) × rate</c>, measured from the animation's own start
    /// (`C_BaseViewModel::UpdateAnimationParity`, `c_baseviewmodel.cpp:467`).
    /// </summary>
    [Test]
    public void Advance_WithoutASpeed_IsTheStatedCyclePlusElapsedSinceStartTimesRate()
    {
        EntityModelSet models = new();

        // 0.25 + (1.0 − 0.5) × 0.5 = 0.5 → frame 15 exactly.
        SceneProp prop = Prop(new ScenePose { Cycle = 0.25f, AnimationStartSeconds = 0.5d, PlaybackRate = 0.5f });

        (int Sequence, int Frame, float Fraction) at = PoseAt(models, prop, 1.0d);

        at.Frame.ShouldBe(15);
        at.Fraction.ShouldBe(0f, 1e-4f);
    }

    /// <summary>
    /// With a speed, the first frame starts from the closed form and every later one ADDS this frame's step at this
    /// frame's rate — so a rate change bends the phase rather than jumping it (B172).
    /// </summary>
    [Test]
    public void Advance_WithASpeedAndARateChange_AccumulatesEachStepAtItsOwnRate()
    {
        EntityModelSet models = new();

        // First frame, closed form: 0 + 0.5 × 1 = 0.5.
        PoseAt(models, Prop(Running(rate: 1f)), 0.5d);

        // Then 0.5 + (0.75 − 0.5) × 0.5 = 0.625 → frame 18.75. The closed form would say 0.375 → 11.25.
        (int Sequence, int Frame, float Fraction) at = PoseAt(models, Prop(Running(rate: 0.5f)), 0.75d);

        at.Frame.ShouldBe(18);
        at.Fraction.ShouldBe(0.75f, 1e-3f);
    }

    /// <summary>
    /// A step longer than <c>LongestCycleStep</c> (one second) is a seek, so the cycle is recomputed from the closed form
    /// rather than integrated across the gap.
    /// </summary>
    [Test]
    public void Advance_AfterAStepLongerThanOneSecond_RestartsFromTheClosedForm()
    {
        EntityModelSet models = new();

        PoseAt(models, Prop(Running(rate: 1f)), 0.5d);

        // Closed: 2.25 × 0.5 = 1.125 → 0.125 → frame 3.75. Integrated would be 0.5 + 1.75 × 0.5 = 1.375 → 0.375 → 11.25.
        (int Sequence, int Frame, float Fraction) at = PoseAt(models, Prop(Running(rate: 0.5f)), 2.25d);

        at.Frame.ShouldBe(3);
        at.Fraction.ShouldBe(0.75f, 1e-3f);
    }

    /// <summary>A step of exactly one second is still integrated — the bound is exclusive.</summary>
    [Test]
    public void Advance_AfterAStepOfExactlyOneSecond_StillIntegrates()
    {
        EntityModelSet models = new();

        PoseAt(models, Prop(Running(rate: 1f)), 0.5d);

        // Integrated: 0.5 + 1 × 0.5 = 1.0 → 0. Closed would be 1.5 × 0.5 = 0.75 → frame 22.5.
        (int Sequence, int Frame, float Fraction) at = PoseAt(models, Prop(Running(rate: 0.5f)), 1.5d);

        at.Frame.ShouldBe(0);
        at.Fraction.ShouldBe(0f, 1e-3f);
    }

    /// <summary>A frame a hair BEHIND the last holds the cycle: the engine's never runs backwards.</summary>
    [Test]
    public void Advance_AtAnEarlierTime_HoldsTheLastCycle()
    {
        EntityModelSet models = new();

        PoseAt(models, Prop(Running(rate: 1f)), 0.5d);

        // Held at 0.5 → frame 15. The closed form would be 0.25 → 7.5.
        (int Sequence, int Frame, float Fraction) at = PoseAt(models, Prop(Running(rate: 1f)), 0.25d);

        at.Frame.ShouldBe(15);
        at.Fraction.ShouldBe(0f, 1e-3f);
    }

    /// <summary>Poses one prop at one time and reads back the frame the skeleton was handed.</summary>
    private static (int Sequence, int Frame, float Fraction) PoseAt(EntityModelSet models, SceneProp prop, double seconds)
    {
        SceneProp[] props = [prop];
        models.Add(props, _ => Animated());
        models.Instances(props, [], seconds: seconds);

        (int Sequence, int Frame, float Fraction)? at = models.FrameOf(prop.EntityIndex);
        at.ShouldNotBeNull("the prop must reach the skeleton");
        return at.Value;
    }

    /// <summary>A pose that carries a speed, which is what puts it on the integrated path.</summary>
    private static ScenePose Running(float rate) => new() { Speed = 1f, PlaybackRate = rate };

    /// <summary>A client-side-animated prop in that pose.</summary>
    private static SceneProp Prop(ScenePose pose) =>
        new(
            7,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            pose,
            null,
            ClientSideAnimated: true);

    /// <summary>One looping one-cycle-a-second sequence over 31 frames.</summary>
    private static PropModels.ModelFrames Animated()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithBones("root");

        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> looping = [];

        foreach ((int group, IReadOnlyList<StudioSequence> sequences) in model.Groups)
        {
            List<StudioSequence> marked = [];

            foreach (StudioSequence sequence in sequences)
            {
                marked.Add(sequence with { Flags = SequenceLooping });
            }

            looping.Add((group, marked));
        }

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: model with { Models = [AnimatedStudioBytes.OneSecondLoop(animations: 3)], Groups = looping });
    }
}
