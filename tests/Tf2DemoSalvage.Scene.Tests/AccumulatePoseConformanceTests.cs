using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>Every accumulated sequence accumulates its own autolayers, nested inside it (B624, B626).</summary>
/// <remarks>
/// **`CBoneSetup::AccumulatePose` runs `AddSequenceLayers` for EVERY sequence it accumulates** (`bone_setup.cpp:2449`),
/// and every entry point is `AccumulatePose`: the main sequence (`c_baseanimating.cpp:1986`), each fading transition
/// (`:1863`) and each overlay — a networked layer or a TF2 gesture (`c_baseanimatingoverlay.cpp:369`). So a reload
/// gesture's arms layer and a fading run's aim matrix are accumulated straight after their parent, inside its bracket.
/// `AddSequenceLayers` hands `AccumulatePose` the window's weight with no test (`:2209`), so a weight-0 autolayer is still
/// accumulated, and its `STUDIO_AL_NOBLEND` child takes `layerWeight = s` whatever the parent weighs (`:2195`).
///
/// **The order and the nesting are the claim**, read off <see cref="EntityModelSet.LayersOf"/> — what the skeleton was
/// handed (B243). The autolayers are written into the studio BYTES, the path production reads.
/// </remarks>
public sealed class AccumulatePoseConformanceTests
{
    /// <summary>The sequence that declares an autolayer.</summary>
    private const int Parent = 1;

    /// <summary>The sequence it layers.</summary>
    private const int Child = 2;

    /// <summary>An all-zero window: the layer plays at its parent's cycle and weight (`start == end`).</summary>
    private static readonly StudioAutoLayer Unwindowed = new(Child, 0, 0, 0f, 0f, 0f, 0f);

    /// <remarks>
    /// **An overlay is an `AccumulatePose`** (`c_baseanimatingoverlay.cpp:369`): a networked layer on sequence 1 at weight
    /// 0.5 brings sequence 2 straight after it at 0.5, the parent's weight through an unwindowed autolayer, nested in it.
    /// </remarks>
    [Test]
    public void LayersFor_AWireLayerWhoseSequenceAutolayers_AccumulatesTheAutolayerInsideIt()
    {
        EntityModelSet models = new();
        SceneProp[] props =
        [
            new(
                9,
                "models/props_gameplay/resupply_locker.mdl",
                ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
                new ScenePose
                {
                    Sequence = 0,
                    Layers = [new SceneAnimationLayer(Order: 0, Sequence: Parent, Cycle: 0.25f, Weight: 0.5f)],
                },
                null),
        ];

        models.Add(props, _ => Model(("a", 0x0001), ("b", 0x0001), ("c", 0x0001)));
        models.Instances(props, [], seconds: 1d);

        Shape(models.LayersOf(9)).ShouldBe([(Parent, 0.5f, 1), (Child, 0.5f, 0)]);
    }

    /// <remarks>
    /// **A gesture is an overlay too.** `AddToGestureSlot` sets `m_flWeight = 1.0f` and `AccumulateLayers` accumulates it
    /// (`c_baseanimatingoverlay.cpp:369`), so a reload's arms layer — `ReloadStand_PRIMARY_loop` →
    /// `layer_reload_standing_arms_loop` on a real soldier — comes with it at weight one.
    /// </remarks>
    [Test]
    public void LayersFor_AGestureWhoseSequenceAutolayers_AccumulatesTheAutolayerInsideIt()
    {
        EntityModelSet models = new();
        List<SceneProp> drawn = [Reloading()];

        models.Add(drawn, _ => Model(("ACT_MP_RUN_PRIMARY", 0), ("ACT_MP_RELOAD_STAND_PRIMARY", 0), ("ACT_MP_STAND_PRIMARY", 0)));
        models.Instances(drawn, [], seconds: 0.05d);

        Shape(models.LayersOf(4)).ShouldBe([(Parent, 1f, 1), (Child, 1f, 0)]);
    }

    /// <remarks>
    /// **A fading sequence is an `AccumulatePose`** (`c_baseanimating.cpp:1863`): the entity leaves sequence 1 for 0, and
    /// the outgoing 1 keeps its autolayer for as long as it fades, at the fade's own weight.
    /// </remarks>
    [Test]
    public void BuildBlend_AFadingSequenceWhoseSequenceAutolayers_AccumulatesTheAutolayerInsideIt()
    {
        EntityModelSet models = new();
        List<SceneProp> drawn = [Playing(Parent)];

        models.Add(drawn, _ => Model(("a", 0), ("b", 0), ("c", 0)));
        models.Instances(drawn, [], seconds: 0d);

        drawn[0] = Playing(0);
        models.Instances(drawn, [], seconds: 0.05d);

        IReadOnlyList<(int Sequence, float Weight, int Nested)> shape = Shape(models.LayersOf(4));

        shape.Select(layer => (layer.Sequence, layer.Nested)).ShouldBe([(Parent, 1), (Child, 0)]);
        shape[0].Weight.ShouldBeGreaterThan(0f, "a quarter of the way through the fade it still weighs something");
        shape[1].Weight.ShouldBe(shape[0].Weight, "an unwindowed autolayer takes its parent's weight");
    }

    /// <remarks>
    /// **B626 and the recursion in one.** The main sequence 0 layers 1 over a window opening at 0.2 with its peak at 0.6;
    /// at cycle exactly 0.2 the ramp is `(0.2 − 0.2) / 0.4 = 0`, so 1 is accumulated at weight 0 — not dropped. Sequence 1
    /// layers 2 with `STUDIO_AL_NOBLEND` over a window open across its whole cycle, so 2 takes `layerWeight = s = 1`
    /// regardless. The old guard dropped 1 and with it 2.
    /// </remarks>
    [Test]
    public void BuildBlend_AZeroWeightAutolayerWithANoBlendChild_AccumulatesBoth()
    {
        Dictionary<int, StudioAutoLayer[]> autoLayers = new()
        {
            [0] = [new StudioAutoLayer(Parent, 0, 0, 0.2f, 0.6f, 0.8f, 0.9f)],
            [Parent] = [new StudioAutoLayer(Child, 0, StudioAutoLayerFlags.NoBlend, 0f, 0f, 1f, 1f)],
        };

        EntityModelSet models = new();
        List<SceneProp> drawn = [Playing(0)];

        models.Add(drawn, _ => Model(autoLayers, ("a", 0), ("b", 0), ("c", 0)));
        models.Instances(drawn, [], seconds: 0.2d);

        Shape(models.LayersOf(4)).ShouldBe([(Parent, 0f, 1), (Child, 1f, 0)]);
    }

    /// <remarks>
    /// **The control: the main sequence's own autolayers were accumulated before B624**, so a model whose main sequence
    /// declares the autolayer shows it either way. Nesting depth two — 0 → 1 → 2 — is what the old recursion already
    /// reached; this pins that the nesting count is written for it.
    /// </remarks>
    [Test]
    public void BuildBlend_AMainSequenceAutolayerThatAutolayers_NestsTheGrandchild()
    {
        Dictionary<int, StudioAutoLayer[]> autoLayers = new()
        {
            [0] = [new StudioAutoLayer(Parent, 0, 0, 0f, 0f, 0f, 0f)],
            [Parent] = [Unwindowed],
        };

        EntityModelSet models = new();
        List<SceneProp> drawn = [Playing(0)];

        models.Add(drawn, _ => Model(autoLayers, ("a", 0), ("b", 0), ("c", 0)));
        models.Instances(drawn, [], seconds: 0.25d);

        Shape(models.LayersOf(4)).ShouldBe([(Parent, 1f, 1), (Child, 1f, 0)]);
    }

    /// <remarks>
    /// **B625 on the main sequence: its bracket encloses its own autolayers and nothing after them.** The main sequence's
    /// subtree is 0 → 1 → 2, two layers, and a networked layer on 2 follows as a root of its own. `SolveSequenceLocks`
    /// closes the main `AccumulatePose` after its `AddSequenceLayers` (`bone_setup.cpp:2449-2454`) and before
    /// `AccumulateLayers` begins (`c_baseanimating.cpp:1986-1992`), so the skeleton is handed 2 — not 0, and not all 3.
    /// </remarks>
    [Test]
    public void BuildBlend_AMainSequenceWithAutolayersAndAnOverlay_EnclosesOnlyItsOwnSubtree()
    {
        Dictionary<int, StudioAutoLayer[]> autoLayers = new()
        {
            [0] = [new StudioAutoLayer(Parent, 0, 0, 0f, 0f, 0f, 0f)],
            [Parent] = [Unwindowed],
        };

        EntityModelSet models = new();
        List<SceneProp> drawn =
        [
            new(
                4,
                "models/props_gameplay/resupply_locker.mdl",
                ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
                new ScenePose
                {
                    Sequence = 0,
                    Cycle = 0f,
                    Layers = [new SceneAnimationLayer(Order: 0, Sequence: Child, Cycle: 0.5f, Weight: 1f)],
                },
                null,
                ClientSideAnimated: true),
        ];

        models.Add(drawn, _ => Model(autoLayers, ("a", 0), ("b", 0), ("c", 0)));
        models.Instances(drawn, [], seconds: 0.25d);

        models.LayersOf(4).ShouldNotBeNull().Count.ShouldBe(3, "the control: the overlay follows the main subtree");
        models.BaseNestedOf(4).ShouldBe(2);
    }

    /// <remarks>
    /// **The depth bound is ours, and it is the same for every root.** Valve has none (`AccumulatePose` →
    /// `AddSequenceLayers` → `AccumulatePose`, `bone_setup.cpp:2449`, `:2209`), so a cycle would recurse until the stack
    /// ran out. Sequence 1 layers ITSELF; the main sequence 0 layers 1, and a networked layer plays 1 as a root of its own.
    /// Below the main sequence the chain is four 1s; below the overlay root it is the root and four more. Equal depth
    /// below each starting sequence is the claim.
    /// </remarks>
    [Test]
    public void BuildBlend_ACyclicAutolayer_StopsFourLevelsBelowEveryRoot()
    {
        Dictionary<int, StudioAutoLayer[]> autoLayers = new()
        {
            [0] = [new StudioAutoLayer(Parent, 0, 0, 0f, 0f, 0f, 0f)],
            [Parent] = [new StudioAutoLayer(Parent, 0, 0, 0f, 0f, 0f, 0f)],
        };

        EntityModelSet models = new();
        List<SceneProp> drawn =
        [
            new(
                4,
                "models/props_gameplay/resupply_locker.mdl",
                ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
                new ScenePose
                {
                    Sequence = 0,
                    Cycle = 0f,
                    Layers = [new SceneAnimationLayer(Order: 0, Sequence: Parent, Cycle: 0.5f, Weight: 1f)],
                },
                null,
                ClientSideAnimated: true),
        ];

        models.Add(drawn, _ => Model(autoLayers, ("a", 0), ("b", 0), ("c", 0)));
        models.Instances(drawn, [], seconds: 0.25d);

        models.BaseNestedOf(4).ShouldBe(4, "four levels below the main sequence");
        Shape(models.LayersOf(4)).Select(layer => layer.Nested).ShouldBe(
            [3, 2, 1, 0, 4, 3, 2, 1, 0], "and the overlay root with four levels below it");
    }

    private static IReadOnlyList<(int Sequence, float Weight, int Nested)> Shape(IReadOnlyList<PoseLayer>? layers) =>
        [.. layers.ShouldNotBeNull().Select(layer => (layer.Sequence, layer.Weight, layer.Nested))];

    private static SceneProp Playing(int sequence) =>
        new(
            4,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = sequence, Cycle = 0f },
            null,
            ClientSideAnimated: true);

    /// <summary>A scout-shaped player reloading, as <c>GestureLayerWiringTests</c> builds one.</summary>
    private static SceneProp Reloading() =>
        new(
            4,
            "models/player/scout.mdl",
            ScenePropTrack.Classify("models/player/scout.mdl"),
            new ScenePose
            {
                Sequence = 0,
                Speed = 320f,
                Flags = PlayerActivityState.OnGround,
                Slot = "PRIMARY",
                Gestures = [new SceneGesture(GestureSlot.AttackAndReload, "ACT_MP_RELOAD_STAND", null, AutoKill: true, 0d)],
            },
            null,
            ClientSideAnimated: true);

    /// <summary>Three sequences with real bytes, sequence 1 declaring the unwindowed autolayer over 2.</summary>
    private static PropModels.ModelFrames Model(params (string Label, int Flags)[] sequences) =>
        Model(new Dictionary<int, StudioAutoLayer[]> { [Parent] = [Unwindowed] }, sequences);

    private static PropModels.ModelFrames Model(
        IReadOnlyDictionary<int, StudioAutoLayer[]> autoLayers, params (string Label, int Flags)[] sequences) =>
        new(
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
            Skinned: SyntheticSkinnedModel.WithFlags(sequences) with
            {
                Models = [AnimatedStudioBytes.OneSecondLoop(animations: 3, sequences: 3, autoLayers)],
            });
}
