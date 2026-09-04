using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A sequence flagged <c>STUDIO_REALTIME</c> takes its cycle from the clock, not from the entity.
/// </summary>
/// <remarks>
/// **<c>CalcPoseSingle</c>'s first branch** (<c>bone_setup.cpp:1955</c>), before anything else the
/// function does with a cycle:
///
/// <code>
///   if (seqdesc.flags &amp; STUDIO_REALTIME)
///   {
///       float cps = Studio_CPS( pStudioHdr, seqdesc, sequence, poseParameter );
///       cycle = flTime * cps;
///       cycle = cycle - (int)cycle;
///   }
/// </code>
///
/// **The cycle the entity carries is DISCARDED**, not corrected — the flag's own comment is
/// *"cycle index is taken from a real-time clock, not the animations cycle index"*
/// (<c>studio.h:3086</c>). So a server-animated entity, whose cycle we otherwise take straight off
/// the wire and never advance, still animates when its sequence carries this.
///
/// **Measured before it was written** (B309): 32 of 26,387 sequences across all 14,109 models in
/// `tf2_misc_dir.vpk` carry the flag, on the MvM bot animation models —
/// `bot_soldier_boss_animations.mdl:layer_primary_jump_floatNoise` and its neighbours. Rare, real,
/// and on content a demo can contain.
///
/// <code>
///   dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- sequence-flags
/// </code>
///
/// **The wrap is a plain truncation and not `ClampCycle`.** `cycle - (int)cycle` ignores
/// `STUDIO_LOOPING` entirely, so a non-looping realtime sequence still wraps — which is the one
/// place these two normalisations disagree, and the reason this is written as its own branch rather
/// than folded into the existing clamp.
/// </remarks>
public sealed class RealtimeSequenceTests
{
    /// <summary>`STUDIO_REALTIME` (<c>studio.h:3086</c>).</summary>
    private const int Realtime = 0x0100;

    /// <remarks>
    /// **The distinguishing input is a SERVER-animated entity**, whose cycle we take off the wire
    /// and never advance. Its sent cycle is 0.9, which is frame 27; the clock says 3.25 seconds at
    /// one cycle a second, so the realtime cycle is 0.25 and frame 7.5. Correct and broken predict
    /// frames that are nowhere near each other, and neither is the fixture's default.
    /// </remarks>
    [Test]
    public void Pose_ForARealtimeSequence_TakesTheCycleFromTheClock()
    {
        EntityModelSet models = new() { Geometry = _ => Animated(Realtime) };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp cabinet = Held(cycle: 0.9f);

        scene.Build([], [cabinet], At(3.25d));
        scene.Pose(At(3.25d));

        (int Sequence, int Frame, float Fraction)? posed = models.FrameOf(cabinet.EntityIndex);

        posed.ShouldNotBeNull("the prop must reach the skeleton at all");

        posed.Value.Frame.ShouldBe(7, "frac(3.25 x 1 cycle a second) is 0.25, and 0.25 of 30 is 7.5");
        posed.Value.Fraction.ShouldBe(0.5f, 1e-4d, "half past frame 7, not frame 27");
    }

    /// <remarks>
    /// **The control, and it is what says the FLAG did it.** The same entity, the same clock, the
    /// same sent cycle — with the flag cleared it must hold frame 27, which is what a server-animated
    /// entity does. Without this, a decode that took the clock for everything would pass the test
    /// above.
    /// </remarks>
    [Test]
    public void Pose_ForAnOrdinarySequence_KeepsTheCycleTheWireSent()
    {
        EntityModelSet models = new() { Geometry = _ => Animated(0) };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp cabinet = Held(cycle: 0.9f);

        scene.Build([], [cabinet], At(3.25d));
        scene.Pose(At(3.25d));

        (int Sequence, int Frame, float Fraction)? posed = models.FrameOf(cabinet.EntityIndex);

        posed.ShouldNotBeNull("the prop must reach the skeleton at all");

        posed.Value.Frame.ShouldBe(27, "0.9 of 30 frames, straight off the wire");
    }

    /// <remarks>
    /// **A realtime sequence wraps whether or not it loops**, because `cycle - (int)cycle` is not
    /// `ClampCycle`. At 3.25 seconds an ordinary non-looping sequence would be clamped to its last
    /// frame; this one is at frame 7 like its looping twin, and that difference is the whole reason
    /// the branch is separate.
    /// </remarks>
    [Test]
    public void Pose_ForARealtimeSequenceThatDoesNotLoop_WrapsAnyway()
    {
        EntityModelSet models = new() { Geometry = _ => Animated(Realtime, loops: false) };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp cabinet = Held(cycle: 0.9f);

        scene.Build([], [cabinet], At(3.25d));
        scene.Pose(At(3.25d));

        models.FrameOf(cabinet.EntityIndex).ShouldNotBeNull().Frame
            .ShouldBe(7, "cycle - (int)cycle ignores STUDIO_LOOPING");
    }

    /// <remarks>
    /// **The second of three sites, and a sabotage is what showed it was untested.** Valve applies
    /// the rewrite inside `CalcPoseSingle`, which `AccumulatePose` runs for the main sequence, for
    /// each wire layer and for each autolayer alike — so the branch exists three times here. The
    /// first version of this file drove only the main sequence: its fixture sent no layers at all,
    /// so the other two `Realtime(...)` checks were not merely unasserted but **never executed**.
    ///
    /// **That is the pattern this project keeps finding in other people's code**, arrived at in its
    /// own: a value decoded, a branch written, and no caller reaching it
    /// (`docs/memory/decoding-a-field-is-not-honouring-it.md`).
    /// </remarks>
    [Test]
    public void Pose_ForARealtimeWireLayer_TakesTheLayersCycleFromTheClock()
    {
        EntityModelSet models = new() { Geometry = _ => Animated(Realtime) };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp cabinet = Held(cycle: 0.9f) with
        {
            Pose = new ScenePose
            {
                Sequence = 0,
                Cycle = 0.9f,
                Layers = [new SceneAnimationLayer(Order: 0, Sequence: 0, Cycle: 0.9f, Weight: 1f)],
            },
        };

        scene.Build([], [cabinet], At(3.25d));
        scene.Pose(At(3.25d));

        models.LayersOf(cabinet.EntityIndex).ShouldNotBeNull()
            .ShouldContain(
                layer => layer.Frame == 7,
                "the layer's cycle comes from the clock too, not from the 0.9 the wire sent");
    }

    /// <remarks>
    /// **The third site, and the one TF2 actually uses.** All 32 sequences carrying the flag are
    /// named `layer_*` — MvM bot `layer_primary_jump_floatNoise` and its neighbours — so an
    /// autolayer target is the case the flag exists for, and it was the least covered.
    ///
    /// **The parent is ordinary and the TARGET is realtime**, which is what separates the two
    /// cycles: the parent stays where the wire put it while the layer runs off the clock.
    /// </remarks>
    [Test]
    public void Pose_ForARealtimeAutoLayer_SamplesTheLayerOnTheClock()
    {
        EntityModelSet models = new() { Geometry = _ => Layered() };

        MomentScene scene = new(models, new ViewmodelScene(), new RecordingLogger());

        SceneProp cabinet = Held(cycle: 0.9f);

        scene.Build([], [cabinet], At(3.25d));
        scene.Pose(At(3.25d));

        models.LayersOf(cabinet.EntityIndex).ShouldNotBeNull()
            .ShouldContain(
                layer => layer.Sequence == LayeredTarget && layer.Frame == 7,
                "the autolayer target carries STUDIO_REALTIME, so it is sampled at frac(3.25)");
    }

    /// <summary>Which sequence the autolayer names.</summary>
    private const int LayeredTarget = 2;

    /// <summary>A model whose first sequence layers a REALTIME third over itself.</summary>
    private static PropModels.ModelFrames Layered()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithFlags(
            ("idle", 0x0001), ("open", 0x0001), ("spin", Realtime | 0x0001));

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
            {
                [0] = (0, 1, 0f),
            },
            [0],
            [true],
            Skinned: model with
            {
                Models =
                [
                    AnimatedStudioBytes.OneSecondLoop(
                        animations: 3,
                        sequences: 3,
                        autoLayerOn: 0,
                        autoLayers:
                        [
                            new StudioAutoLayer(
                                Sequence: LayeredTarget,
                                PoseParameter: 0,
                                Flags: 0,
                                Start: 0f,
                                Peak: 0f,
                                Tail: 0f,
                                End: 0f),
                        ]),
                ],
            });
    }

    /// <summary>A server-animated cabinet holding the cycle the wire sent.</summary>
    private static SceneProp Held(float cycle) =>
        new(
            9,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = 0, Cycle = cycle },
            null);

    /// <summary>A moment at a demo time in seconds, at a one-second tick interval.</summary>
    private static MomentInfo At(double seconds) =>
        new(seconds, (int)seconds, false, null, null, 1f, 54f);

    /// <summary>A model whose one sequence carries the given flags and really has frames.</summary>
    private static PropModels.ModelFrames Animated(int flags, bool loops = true)
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithFlags(
            ("idle", flags | (loops ? 0x0001 : 0)));

        return new PropModels.ModelFrames(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)>
            {
                [0] = (0, 1, 0f),
            },
            [0],
            [true],
            Skinned: model with { Models = [AnimatedStudioBytes.OneSecondLoop(animations: 1)] });
    }
}
