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
    /// **The engine simulates a ragdoll whether or not the view can see it** (B58) — it belongs to
    /// `physenv`, and visibility governs drawing alone. Advancing only DRAWN corpses made one that
    /// came back into view replay every tick it missed in a single frame: measured on f12 as 135, 151
    /// and 551 ms frames with `corpses` holding all of it.
    ///
    /// **`Culled` is the control.** A corpse the frustum did not actually reject would pass this
    /// against the old wiring too, so the test first proves it was outside the view.
    /// </remarks>
    [Test]
    public void Instances_ForACorpseOutsideTheView_StillSimulatesTheTicksBetween()
    {
        SceneProp corpse = Corpse();

        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [corpse];

        models.Add(drawn, _ => Frames());

        models.CurrentTick = 66d;
        models.Instances(drawn, [], seconds: 1d, frustum: LookingAwayFromTheOrigin());

        models.CurrentTick = 132d;
        models.Instances(drawn, [], seconds: 2d, frustum: LookingAwayFromTheOrigin());

        models.Culled.ShouldBe(1, "the control: the corpse really was outside the view");
        models.Corpses.Steps.ShouldBe(66, "one second at the demo's tick rate, seen or not");
    }

    /// <remarks>
    /// **A faded corpse leaves the physics world** (owner, 2026-09-19), which is what makes a seek cheap: the rebuild after a
    /// seek replays only the last fifteen seconds of deaths, and a corpse that never left would be carried by every rebuild
    /// after it. `EndFadeOut` → `ClearRagdoll` destroys its constraints and then its objects.
    ///
    /// **The empty moment is the case that matters**, and it is the one an early return hides: a moment carrying no corpses at
    /// all is every corpse having faded, so retiring has to happen before `Advance` decides it has nothing to do.
    ///
    /// The first assertion is the control — without it, a wiring that never put the corpse in the world at all would pass.
    /// </remarks>
    [Test]
    public void Instances_ForACorpseTheMomentStopsCarrying_TakesItOutOfTheWorld()
    {
        SceneProp corpse = Corpse();

        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [corpse];

        models.Add(drawn, _ => Frames());

        models.CurrentTick = 66d;
        models.Instances(drawn, [], seconds: 1d);

        models.Corpses.Count.ShouldBe(1, "the control: it was in the world to begin with");

        int held = models.Corpses.Physics!.Simulation.HeldUnits;
        held.ShouldBeGreaterThan(0, "the control: its bodies really were units in the environment");

        models.CurrentTick = 132d;
        models.Instances([], [], seconds: 2d);

        models.Corpses.Count.ShouldBe(0, "a corpse the moment no longer carries has faded, and a faded corpse leaves");

        // **`Count` alone cannot see this, which is why both are asserted.** It reads the bookkeeping dictionary, and the
        // retire empties that whether or not the ragdoll was torn down — measured: skipping `Destroy()` left this test green.
        // Only the environment's own unit count is evidence that the bodies left the world.
        models.Corpses.Physics.Simulation.HeldUnits
            .ShouldBe(0, "the bodies left the environment, not just the bookkeeping");
    }

    /// <summary>A camera 200 units along +X looking further along it, so the origin is behind.</summary>
    private static ViewFrustum LookingAwayFromTheOrigin() =>
        ViewFrustum.PerspectiveFromAspect(
            origin: (200f, 0f, 0f),
            forward: (1f, 0f, 0f),
            right: (0f, -1f, 0f),
            up: (0f, 0f, 1f),
            nearZ: 7f,
            farZ: 1000f,
            fovX: 90f,
            aspect: 1f);

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

    /// <remarks>
    /// **Every corpse is in one environment, as the engine's `physenv` holds them** (D179): two corpses make one world, stepped once
    /// per tick, not two stepped once each.
    /// </remarks>
    [Test]
    public void Instances_TwoCorpses_ShareOneEnvironmentSteppedOncePerTick()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };
        List<SceneProp> drawn = [Corpse(), Corpse() with { EntityIndex = 10 }];
        models.Add(drawn, _ => Frames());

        models.CurrentTick = 66d;
        models.Instances(drawn, [], seconds: 1d);
        models.CurrentTick = 132d;
        models.Instances(drawn, [], seconds: 2d);

        models.Corpses.Count.ShouldBe(2, "the control: both corpses are simulated");
        models.Corpses.Rebuilds.ShouldBe(1, "one environment, built once");
        models.Corpses.Steps.ShouldBe(66, "stepped once per tick for both, not once per corpse");
    }

    /// <remarks>
    /// **A seek backwards rebuilds the environment and replays it from the earliest death on screen** (D179): a corpse that died at
    /// tick 66, seen at 132 and then at 100, is rebuilt at 66 and stepped the 34 ticks to 100.
    /// </remarks>
    [Test]
    public void Instances_SeekingBackwards_RebuildsAndReplaysFromTheDeath()
    {
        SceneProp corpse = Corpse() with { FirstTick = 66 };
        EntityModelSet models = new() { Geometry = _ => Frames() };
        List<SceneProp> drawn = [corpse];
        models.Add(drawn, _ => Frames());

        models.CurrentTick = 132d;
        models.Instances(drawn, [], seconds: 2d);
        models.Corpses.Steps.ShouldBe(66, "the control: seeded at its death and stepped to 132");

        models.CurrentTick = 100d;
        models.Instances(drawn, [], seconds: 100d / 66d);

        models.Corpses.Rebuilds.ShouldBe(2, "the backward seek rebuilt the environment");
        models.Corpses.Steps.ShouldBe(66 + 34, "and replayed it from the death at 66 to 100");
    }

    /// <remarks>
    /// **A gib is a physics prop, thrown and simulated** (B409): `CreateGibsFromList` makes each piece with
    /// `BreakModelCreateSingle`, which gives it a physics object and `AddVelocity( rndVel, angVelocity )`
    /// (`physpropclientside.cpp:787-796`). The owner saw every gib hold in the air where the player died, because nothing put one
    /// into the world. Thrown at 400 in/s along x, it is well along x a second later.
    /// </remarks>
    [Test]
    public void Instances_AGib_IsThrownByItsVelocityAndSimulated()
    {
        SceneProp gib = Corpse() with { ClassName = RagdollProps.GibClassName, FirstTick = 66, Force = (400f, 0f, 0f) };
        EntityModelSet models = new() { Geometry = _ => GibFrames() };
        List<SceneProp> drawn = [gib];
        models.Add(drawn, _ => GibFrames());

        models.CurrentTick = 66d;
        models.Instances(drawn, [], seconds: 1d);
        models.CurrentTick = 132d;
        List<ModelInstance> instances = [];
        models.Instances(drawn, instances, seconds: 2d);

        models.Corpses.Count.ShouldBe(1, "the gib is in the physics world");
        models.Corpses.Roots[gib.EntityIndex].X.ShouldBeGreaterThan(300f, "thrown 400 in/s along x for a second");

        // **The output: the baked mesh is DRAWN where the physics put it**, as `C_PhysPropClientside`'s origin follows its object.
        instances.ShouldNotBeEmpty("the control: the gib was drawn");
        instances[0].Matrix[12].ShouldBe(models.Corpses.Roots[gib.EntityIndex].X, 1e-3f, "drawn at the simulated origin (row-major, translation last)");
    }

    /// <summary>A gib's frames as a real gib model loads: BAKED, no skeleton, one prop body from its <c>.phy</c>.</summary>
    /// <remarks>Measured on `cp_process_f12`: every `soldiergib00N.mdl` loads with a prop body and <c>Skinned</c> null.</remarks>
    private static PropModels.ModelFrames GibFrames() =>
        Frames() with { Skinned = null, Ragdoll = RagdollBody.BuildProp(GibPhysics())! };

    /// <summary>A single-solid prop with one hull, as every gib's <c>.phy</c> is.</summary>
    private static PhysicsModel GibPhysics()
    {
        PhysicsLedge ledge = new(
            [new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0f, 0f), new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0f, 0.1f)],
            [(0, 1, 2), (0, 3, 1), (0, 2, 3), (1, 3, 2)],
            [(0, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 0)],
            [0, 0, 0, 0],
            [0, 0, 0, 0],
            Vector3.Zero,
            0.1f);

        return PhysicsModel.From(
            [new PhysicsSolid(0, "gib_reference", "", "flesh", 5f, 1f, 0f, 0f, 10f, 0f)],
            [],
            1,
            checksum: 0,
            collisionRules: null,
            hulls: [[ledge]],
            massProperties: [new PhysicsMassProperties(Vector3.Zero, Vector3.One / (IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre))]);
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

    internal static SceneProp Corpse() =>
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
    internal static PropModels.ModelFrames Frames()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithBones("bip_pelvis");

        PhysicsModel physics = PhysicsModel.From(
            [new PhysicsSolid(0, "bip_pelvis", "", "flesh", 10f, 1f, 0f, 0f, 100f, 0f)],
            [],
            1,
            checksum: 0,
            collisionRules: null,
            hulls: null,

            // Every solid the engine builds carries a mass center and hull inertia, and a ragdoll refuses one
            // that does not (B403): the bone, and a square inch per kilogram, in IVP metres.
            massProperties: [new PhysicsMassProperties(Vector3.Zero, Vector3.One / (IvpTransform.InchesPerMetre * IvpTransform.InchesPerMetre))]);

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

            // **A real box, so the frustum can judge it.** An empty one is never culled
            // (`WorldSpaceBounds.IsPlaced`), which would make the out-of-view test pass for the
            // wrong reason. Thirty units about the origin, clear of a camera 200 units away.
            HeaderBounds: new StudioBox(-30f, -30f, -30f, 30f, 30f, 30f),
            Ragdoll: ragdoll);
    }
}
