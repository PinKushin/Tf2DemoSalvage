using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A sequence layers other sequences over itself — <c>AddSequenceLayers</c>.
/// </summary>
/// <remarks>
/// **<c>bone_setup.cpp:2125</c>**, called by `AccumulatePose` as its last step (<c>:2449</c>). Each
/// `mstudioautolayer_t` names a sequence and an envelope across the parent's cycle — ramping in
/// between `start` and `peak`, held to `tail`, ramping out to `end` — and the layer is accumulated
/// at the resulting weight.
///
/// **Measured before it was built, and it is used**: 1 of 76 sequences on `koth_harvest_final` and
/// 6 of 142 on `cp_fulgur` declare autolayers, where all four unimplemented `CalcProceduralBone`
/// rules measured zero on the same demos (B294). `sentry3`'s `idle_off` layers two sequences with an
/// all-zero window; `c_rocketpack`'s `deploy` layers one over 0.47 to 1.22.
///
/// **The all-zero window is the COMMON case, not a degenerate one.** Four of the seven real
/// autolayers have `start == end`, and Valve's `if (pLayer->start != pLayer->end)` then leaves the
/// layer at the parent's own cycle and weight — so a reader who treated that as "no layer" would
/// drop most of them.
/// </remarks>
public sealed class AutoLayerTests
{
    /// <summary>The sequence the prop plays, which declares the autolayers.</summary>
    private const int Parent = 0;

    /// <summary>The sequence it layers over itself.</summary>
    private const int Layered = 2;

    /// <summary>A second sequence, so ORDER between two layers is observable.</summary>
    private const int Other = 1;

    [Test]
    public void Instances_WithAnUnwindowedAutoLayer_AccumulatesItAtTheParentsWeight()
    {
        // start == end, so the envelope block is skipped whole: layerCycle stays the parent's cycle
        // and layerWeight stays the parent's weight, which for the main sequence is one.
        EntityModelSet models = Loaded(Window(0f, 0f, 0f, 0f));

        models.Instances([Playing()], [], seconds: 0.25d);

        PoseLayer layer = models.LayersOf(4).ShouldNotBeNull()
            .Single(entry => entry.Sequence == Layered);

        layer.Weight.ShouldBe(1f, "an unwindowed layer takes the parent's weight unchanged");
    }

    /// <remarks>
    /// **The control.** With three sequences in the model, "layered the one the autolayer names" and
    /// "layered everything" are the same observation on any assertion that only looks for one index.
    /// </remarks>
    [Test]
    public void Instances_WithAnUnwindowedAutoLayer_LeavesUnnamedSequencesAlone()
    {
        EntityModelSet models = Loaded(Window(0f, 0f, 0f, 0f));

        models.Instances([Playing()], [], seconds: 0.25d);

        models.LayersOf(4).ShouldNotBeNull()
            .Select(entry => entry.Sequence)
            .ShouldNotContain(1, "sequence 1 is named by no autolayer");
    }

    [Test]
    public void Instances_BeforeTheWindowOpens_LayersNothing()
    {
        // `if (index < pLayer->start) continue;` — the layer does not exist yet. The prop is at
        // cycle 0.1 against a window opening at 0.5.
        //
        // **`Start == Peak`, and that is the whole reason this test can fail.** With a rising ramp
        // the same formula extrapolated below `start` gives a NEGATIVE weight, which the separate
        // `layerWeight <= 0` guard drops anyway — so a window of 0.5/0.6/0.8/0.9 produced the right
        // answer with the gate removed entirely, and the test passed against code that had lost it.
        // A degenerate ramp-in leaves the ramp at its default one, so only the gate is left to
        // block the layer. Found by sabotage; the fix is the input, not the assertion.
        EntityModelSet models = Loaded(Window(0.5f, 0.5f, 0.8f, 0.9f));

        models.Instances([Playing()], [], seconds: 0.1d);

        models.LayersOf(4).ShouldNotBeNull()
            .Select(entry => entry.Sequence)
            .ShouldNotContain(Layered, "the cycle has not reached the layer's start");
    }

    [Test]
    public void Instances_InsideTheRampIn_WeightsItByHowFarThrough()
    {
        // `s = (index - start) / (peak - start)`. At cycle 0.3 with start 0.2 and peak 0.6 that is
        // 0.25 exactly, and with the parent at weight one the layer weight IS the ramp.
        EntityModelSet models = Loaded(Window(0.2f, 0.6f, 0.8f, 0.9f));

        models.Instances([Playing()], [], seconds: 0.3d);

        models.LayersOf(4).ShouldNotBeNull()
            .Single(entry => entry.Sequence == Layered)
            .Weight.ShouldBe(0.25f, 1e-4d, "(0.3 - 0.2) / (0.6 - 0.2)");
    }

    [Test]
    public void Instances_InsideTheRampOut_WeightsItByHowFarLeft()
    {
        // `s = (end - index) / (end - tail)`. At cycle 0.85 with tail 0.8 and end 0.9 that is 0.5.
        EntityModelSet models = Loaded(Window(0.2f, 0.6f, 0.8f, 0.9f));

        models.Instances([Playing()], [], seconds: 0.85d);

        models.LayersOf(4).ShouldNotBeNull()
            .Single(entry => entry.Sequence == Layered)
            .Weight.ShouldBe(0.5f, 1e-4d, "(0.9 - 0.85) / (0.9 - 0.8)");
    }

    [Test]
    public void Instances_BetweenPeakAndTail_GivesItFullWeight()
    {
        // Neither ramp branch is taken, so `s` keeps its initial 1.0 — the plateau.
        EntityModelSet models = Loaded(Window(0.2f, 0.6f, 0.8f, 0.9f));

        models.Instances([Playing()], [], seconds: 0.7d);

        models.LayersOf(4).ShouldNotBeNull()
            .Single(entry => entry.Sequence == Layered)
            .Weight.ShouldBe(1f, 1e-4d, "between peak and tail the ramp is one");
    }

    [Test]
    public void Instances_WithASplinedRamp_EasesRatherThanRunningStraight()
    {
        // `s = SimpleSpline( s )`, which is 3s^2 - 2s^3. At a linear quarter that is
        // 3(0.0625) - 2(0.015625) = 0.15625 — below the straight line, which is the whole point.
        EntityModelSet models = Loaded(
            Window(0.2f, 0.6f, 0.8f, 0.9f) with { Flags = StudioAutoLayerFlags.Spline });

        models.Instances([Playing()], [], seconds: 0.3d);

        models.LayersOf(4).ShouldNotBeNull()
            .Single(entry => entry.Sequence == Layered)
            .Weight.ShouldBe(0.15625f, 1e-4d, "3s^2 - 2s^3 at s = 0.25");
    }

    [Test]
    public void Instances_WithNoBlend_IgnoresTheParentsWeightEntirely()
    {
        // `layerWeight = s`, rather than `flWeight * s`. The main sequence is at weight one here so
        // the two agree numerically — which is why this asserts the RAMP rather than a product, and
        // why the case that separates them belongs to a layer accumulated below full weight.
        EntityModelSet models = Loaded(
            Window(0.2f, 0.6f, 0.8f, 0.9f) with { Flags = StudioAutoLayerFlags.NoBlend });

        models.Instances([Playing()], [], seconds: 0.3d);

        models.LayersOf(4).ShouldNotBeNull()
            .Single(entry => entry.Sequence == Layered)
            .Weight.ShouldBe(0.25f, 1e-4d, "the ramp alone");
    }

    [Test]
    public void Instances_WithALocalAutoLayer_NeedsTheSequenceToDeclareTheLocalPass()
    {
        // `AddLocalLayers` returns immediately unless the SEQUENCE carries STUDIO_LOCAL
        // (`bone_setup.cpp:2229`), and `AddSequenceLayers` skips any layer that carries
        // STUDIO_AL_LOCAL — so a local layer on a sequence without the flag is applied by neither.
        // Measured real case: c_engineer_arms' throw_draw carries both.
        EntityModelSet without = Loaded(
            Window(0f, 0f, 0f, 0f) with { Flags = StudioAutoLayerFlags.Local }, localSequence: false);

        without.Instances([Playing()], [], seconds: 0.25d);

        without.LayersOf(4).ShouldNotBeNull()
            .Select(entry => entry.Sequence)
            .ShouldNotContain(Layered, "neither pass claims a local layer on a non-local sequence");

        EntityModelSet with = Loaded(
            Window(0f, 0f, 0f, 0f) with { Flags = StudioAutoLayerFlags.Local }, localSequence: true);

        with.Instances([Playing()], [], seconds: 0.25d);

        with.LayersOf(4).ShouldNotBeNull()
            .Select(entry => entry.Sequence)
            .ShouldContain(Layered, "with STUDIO_LOCAL the local pass applies it");
    }

    [Test]
    public void Instances_WithALocalAutoLayer_PutsItAheadOfEverythingElse()
    {
        // **Order is the claim.** The local pass composes into the sequence's own pose BEFORE that
        // pose is blended in, so its layer has to come first in the list the skeleton accumulates —
        // ahead of the non-local layers, the transitions and the gestures. Applying it last would
        // be a different pose, and every individual layer would still look correct.
        //
        // **TWO layers, one of each kind, and with only one the test could not fail.** The
        // non-local pass filters the single local entry out and returns an empty list, so
        // `composed[0]` was the local layer whichever pass ran first — the test passed with the two
        // calls swapped. There has to be something for it to be ahead OF.
        EntityModelSet models = Loaded(
            [
                Window(0f, 0f, 0f, 0f) with
                {
                    Sequence = Other, Flags = 0,
                },
                Window(0f, 0f, 0f, 0f) with
                {
                    Flags = StudioAutoLayerFlags.Local,
                },
            ],
            localSequence: true);

        models.Instances([Playing()], [], seconds: 0.25d);

        IReadOnlyList<PoseLayer> composed = models.LayersOf(4).ShouldNotBeNull();

        composed.Select(entry => entry.Sequence).ShouldContain(
            Other, "the non-local layer is applied too, so there is something to be ahead of");

        composed[0].Sequence.ShouldBe(
            Layered, "the local pass is the first thing accumulated onto the base");
    }

    /// <summary>An autolayer naming <see cref="Layered"/> over the given window.</summary>
    private static StudioAutoLayer Window(float start, float peak, float tail, float end) =>
        new(
            Sequence: Layered,
            PoseParameter: 0,
            Flags: 0,
            Start: start,
            Peak: peak,
            Tail: tail,
            End: end);

    /// <summary>A prop playing the parent sequence, client-side animated so its cycle advances.</summary>
    private static SceneProp Playing() =>
        new(
            4,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = Parent, Cycle = 0f },
            null,
            ClientSideAnimated: true);

    /// <summary>A model set holding one prop whose sequence declares the given autolayer.</summary>
    private static EntityModelSet Loaded(StudioAutoLayer layer, bool localSequence = false) =>
        Loaded([layer], localSequence);

    /// <summary>The same, with more than one autolayer so their ORDER can be observed.</summary>
    private static EntityModelSet Loaded(StudioAutoLayer[] layers, bool localSequence = false)
    {
        PropModels.ModelFrames Frames() => Built(layers, localSequence);

        EntityModelSet models = new() { Geometry = _ => Frames() };

        models.Add([Playing()], _ => Frames());

        return models;
    }

    /// <summary>Three sequences, the first declaring one autolayer over the third.</summary>
    /// <remarks>
    /// **The autolayer is written into the studio BYTES**, not into a hand-built record, because
    /// that is the path production reads: `StudioAutoLayers.Read` opens the group's model and walks
    /// `autolayerindex` from the sequence structure. A fixture that set a field on the record would
    /// leave the byte reader untested and the test red against correct code — which is exactly what
    /// happened to the autoplay fixture, from the other direction.
    /// </remarks>
    private static PropModels.ModelFrames Built(StudioAutoLayer[] layers, bool localSequence)
    {
        // `STUDIO_LOCAL` is 0x0200 (`studio.h:3086`). Spelled as the literal because `StudioFlags`
        // is internal to the Content assembly; `StudioSequence.HasLocalLayers` is what reads it.
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithFlags(
            ("idle", localSequence ? 0x0200 : 0), ("open", 0), ("spin", 0));

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
                        animations: 3, sequences: 3, autoLayerOn: Parent, autoLayers: layers),
                ],
            });
    }
}
