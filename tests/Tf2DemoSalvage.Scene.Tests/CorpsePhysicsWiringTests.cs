using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// That a corpse's bones actually come from physics when it is drawn (B58, D146).
/// </summary>
/// <remarks>
/// **Every part of the ragdoll path has its own green suite and none of them prove this one.**
/// `RagdollBody.PoseInto`, `IvpEnvironment.Simulate`, `RagdollSimulation`, `CorpsePhysics.Advance` —
/// all covered, and all of them pass whether or not `EntityModelSet.Instances` ever reaches them.
/// That gap is the shape this project has shipped three no-ops in, each with a green suite
/// (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
///
/// **Synthetic rather than against a real demo (D38).** The model is built here, so the test knows
/// the right answer instead of comparing two readings of the same file — and it runs in
/// milliseconds where a corpus test would need the game installed and could only skip without it.
/// </remarks>
public sealed class CorpsePhysicsWiringTests
{
    /// <remarks>
    /// **The wiring assertion, and it needs TWO moments.** A corpse is seeded at the tick it is
    /// first drawn at, so a single draw correctly steps nothing — the first version of this test
    /// asserted `Steps &gt; 0` after one draw and failed against working code. Advancing the clock
    /// is what makes stepping observable.
    ///
    /// **The count is the value `CorpsePhysics` itself used**, carried out rather than recomputed
    /// (B243). A second later at 66 ticks a second is 66 steps, which is also the assertion that
    /// the clock is the DEMO's and not the frame's.
    /// </remarks>
    [Test]
    public void Instances_ForACorpseDrawnASecondLater_SimulatesTheTicksBetween()
    {
        SceneProp corpse = Corpse();

        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [corpse];

        models.Add(drawn, _ => Frames());

        // **The DEMO's tick, set beside the seconds and not derived from them.** A demo's ticks do
        // not start at zero, so the two are different numbers — see `EntityModelSet.CurrentTick`,
        // which is the property this pair of calls exists to exercise.
        models.CurrentTick = 66d;

        models.Instances(drawn, [], seconds: 1d);

        models.Corpses.Count.ShouldBe(1, "the corpse got a simulation");
        models.Corpses.Steps.ShouldBe(0, "seeded at the tick asked for, so nothing to step yet");

        models.CurrentTick = 132d;

        models.Instances(drawn, [], seconds: 2d);

        models.Corpses.Steps.ShouldBe(66, "one second at the demo's tick rate");
    }

    /// <remarks>
    /// **The control, and it is the one that matters.** Without it, "simulates corpses" and
    /// "simulates everything it draws" are the same observation — and the second would put every
    /// living player under physics. A prop of any other class must be left alone even though its
    /// model declares the very same ragdoll.
    /// </remarks>
    [Test]
    public void Instances_ForANonCorpseWithTheSameModel_SimulatesNothing()
    {
        EntityModelSet models = Drawn(Corpse() with { ClassName = "CTFPlayer" }, seconds: 1d);

        models.Corpses.Count.ShouldBe(0);
        models.Corpses.Steps.ShouldBe(0);
    }

    /// <remarks>
    /// **The step count comes from the TICK, not from how many frames were drawn.** Drawing the
    /// same moment twice must not advance the simulation, or a corpse would fall at a speed that
    /// tracked the frame rate — plausible at every rate and matching TF2 at none.
    /// </remarks>
    [Test]
    public void Instances_DrawnTwiceAtTheSameMoment_StepsOnlyOnce()
    {
        SceneProp corpse = Corpse();

        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [corpse];

        models.Add(drawn, _ => Frames());

        // Seed, then advance a second so the counter is NON-ZERO before the repeat. Comparing two
        // zeroes would pass against a wiring that never steps at all, which is the exact thing the
        // suite is here to catch.
        models.CurrentTick = 66d;
        models.Instances(drawn, [], seconds: 1d);

        models.CurrentTick = 132d;
        models.Instances(drawn, [], seconds: 2d);

        int after = models.Corpses.Steps;

        after.ShouldBe(66, "the control: it really had stepped");

        models.Instances(drawn, [], seconds: 2d);

        models.Corpses.Steps.ShouldBe(after, "the same tick asks for no further steps");
    }

    /// <summary>Builds a scene with the one prop and draws it once.</summary>
    private static EntityModelSet Drawn(SceneProp prop, double seconds)
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [prop];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: seconds);

        return models;
    }

    private static SceneProp Corpse() =>
        new(
            9,
            "models/player/soldier.mdl",
            SceneModelKind.Studio,
            new ScenePose { X = 0f, Y = 0f, Z = 0f },
            ClassName: RagdollProps.RagdollClassName);

    /// <summary>A one-bone model whose <c>.phy</c> declares a single body.</summary>
    /// <remarks>
    /// **One element is enough and two would test something else.** What is being asked here is
    /// whether production reaches the simulation at all, not whether a joint solves — that is
    /// covered where the joint lives.
    /// </remarks>
    private static PropModels.ModelFrames Frames()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithBones("bip_pelvis");

        PhysicsModel physics = PhysicsModel.From(
            [new PhysicsSolid(0, "bip_pelvis", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f)],
            [],
            1,
            checksum: 0);

        RagdollBody ragdoll = RagdollBody.Build(physics, model.Bones)!;

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
            Skinned: model,
            Ragdoll: ragdoll);
    }
}
