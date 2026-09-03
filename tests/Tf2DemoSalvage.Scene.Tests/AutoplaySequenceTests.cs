using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A sequence flagged <c>STUDIO_AUTOPLAY</c> plays on its own, off the clock.
/// </summary>
/// <remarks>
/// **<c>CalcAutoplaySequences</c>, <c>bone_setup.cpp:4457</c>**, in full:
///
/// <code>
///   unsigned short *pList = NULL;
///   int count = m_pStudioHdr->GetAutoplayList( &amp;pList );
///   for (i = 0; i &lt; count; i++)
///   {
///       int sequenceIndex = pList[i];
///       mstudioseqdesc_t &amp;seqdesc = pSeqdesc( sequenceIndex );
///       if (seqdesc.flags &amp; STUDIO_AUTOPLAY)
///       {
///           float cps = Studio_CPS( m_pStudioHdr, seqdesc, sequenceIndex, m_flPoseParameter );
///           cycle = flRealTime * cps;
///           cycle = cycle - (int)cycle;
///           AccumulatePose( pos, q, sequenceIndex, cycle, 1.0, flRealTime, pIKContext );
///       }
///   }
/// </code>
///
/// **This is how a model animates part of itself with nothing driving it** — a flag in the wind, a
/// chain, an idle machine. It is not an optional flourish: for such a model it is the ONLY thing
/// that moves, because the entity's own sequence is the idle it is already holding.
///
/// **Three facts that separate it from every layer this project already builds.** Its cycle comes
/// from REAL TIME rather than from the entity's, so it runs on an entity standing still and two
/// copies of one model are always in step. Its weight is a literal 1.0, never faded. And there is
/// no list to parse: `CountAutoplaySequences` (<c>studio.cpp:658</c>) builds Valve's list by
/// walking every sequence and testing the flag, so the flag is the whole of the data.
/// </remarks>
public sealed class AutoplaySequenceTests
{
    /// <summary>The sequence the fixture flags.</summary>
    private const int Autoplaying = 2;

    /// <summary>The sequence the prop is actually playing, which must stay the base.</summary>
    private const int Held = 0;

    [Test]
    public void Instances_WithAnAutoplaySequence_AccumulatesIt()
    {
        EntityModelSet models = Loaded(Playing(Held));

        models.Instances([Playing(Held)], [], seconds: 0.5d);

        IReadOnlyList<PoseLayer> layers = models.LayersOf(4).ShouldNotBeNull();

        layers.Select(layer => layer.Sequence).ShouldContain(
            Autoplaying,
            "a sequence flagged STUDIO_AUTOPLAY is accumulated whatever the entity is doing");
    }

    /// <remarks>
    /// **The control, and without it the test above cannot fail for the right reason.** With three
    /// sequences in the model, "accumulated the flagged one" and "accumulated all of them" produce
    /// the same observation on any assertion that only looks for the flagged index.
    /// </remarks>
    [Test]
    public void Instances_WithAnAutoplaySequence_LeavesTheUnflaggedOnesAlone()
    {
        EntityModelSet models = Loaded(Playing(Held));

        models.Instances([Playing(Held)], [], seconds: 0.5d);

        IReadOnlyList<PoseLayer> layers = models.LayersOf(4).ShouldNotBeNull();

        layers.Select(layer => layer.Sequence).ShouldNotContain(
            1,
            "sequence 1 carries no flag, so nothing should accumulate it");
    }

    [Test]
    public void Instances_WithAnAutoplaySequence_GivesItFullWeight()
    {
        EntityModelSet models = Loaded(Playing(Held));

        models.Instances([Playing(Held)], [], seconds: 0.5d);

        PoseLayer autoplayed = models.LayersOf(4).ShouldNotBeNull()
            .Single(layer => layer.Sequence == Autoplaying);

        autoplayed.Weight.ShouldBe(
            1f, "AccumulatePose( pos, q, sequenceIndex, cycle, 1.0, … ) — a literal one, never faded");
    }

    [Test]
    public void Instances_AtTwoTimes_AdvancesTheAutoplayCycleOnTheClock()
    {
        // **The fixture is one cycle a second**, so a quarter second in is frame 0.25 of the way
        // through and three quarters is three times that. Asserting the two DIFFER is the honest
        // claim: the frame is an integer over 31 frames, so predicting exact frames here would be
        // asserting the rounding rather than the advance.
        EntityModelSet early = Loaded(Playing(Held));
        early.Instances([Playing(Held)], [], seconds: 0.1d);

        EntityModelSet late = Loaded(Playing(Held));
        late.Instances([Playing(Held)], [], seconds: 0.8d);

        int first = early.LayersOf(4).ShouldNotBeNull().Single(l => l.Sequence == Autoplaying).Frame;
        int second = late.LayersOf(4).ShouldNotBeNull().Single(l => l.Sequence == Autoplaying).Frame;

        second.ShouldBeGreaterThan(
            first, "cycle = flRealTime * cps, so an autoplay sequence advances with the clock");
    }

    [Test]
    public void Instances_WithAnAutoplaySequence_AdvancesOnAnEntityThatIsNotAnimated()
    {
        // **The distinguishing case, and the reason autoplay is a mechanism of its own.** This prop
        // is not client-side animated and holds cycle zero for ever, so every other layer this
        // project builds is frozen. If the autoplay cycle came from the entity rather than from the
        // clock, this would sit on frame zero at every time.
        EntityModelSet models = Loaded(Frozen(Held));

        models.Instances([Frozen(Held)], [], seconds: 0.5d);

        models.LayersOf(4).ShouldNotBeNull()
            .Single(layer => layer.Sequence == Autoplaying)
            .Frame.ShouldBeGreaterThan(
                0, "flRealTime drives it, not the entity's own cycle");
    }

    /// <summary>A prop playing one sequence, client-side animated so its own cycle advances.</summary>
    private static SceneProp Playing(int sequence) =>
        new(
            4,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose { Sequence = sequence, Cycle = 0f },
            null,
            ClientSideAnimated: true);

    /// <summary>The same prop with nothing advancing its own cycle.</summary>
    private static SceneProp Frozen(int sequence) =>
        Playing(sequence) with { ClientSideAnimated = false };

    /// <summary>A model set with the prop already loaded, so a pose can be built for it.</summary>
    private static EntityModelSet Loaded(SceneProp prop)
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        models.Add([prop], _ => Frames());

        return models;
    }

    /// <summary>A model with three sequences, of which one autoplays.</summary>
    private static PropModels.ModelFrames Frames()
    {
        // **`STUDIO_AUTOPLAY` is `0x0008`** (`studio.h:3081`). Spelled as the literal because
        // `StudioFlags` is internal to the Content assembly, with the citation standing in for the
        // name — and `idle` and `open` are left unflagged so that "accumulated the flagged one" and
        // "accumulated all three" are different observations.
        PropModels.SkinnedModel model =
            SyntheticSkinnedModel.WithFlags(("idle", 0), ("open", 0), ("spin", 0x0008));

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
                    AnimatedStudioBytes.OneSecondLoop(animations: 3, sequences: 3),
                ],
            });
    }
}
