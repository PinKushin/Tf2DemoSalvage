using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// WHEN an entity's animation events are walked — <c>C_BaseAnimating::Simulate</c>, the caller of the walk
/// <c>AnimationEventFiringConformanceTests</c> covers.
/// </summary>
/// <remarks>
/// **<c>c_baseanimating.cpp:5162</c>**: <c>if ( gpGlobals->frametime != 0.0f ) DoAnimationEvents( GetModelPtr() );</c>.
/// The client's <c>frametime</c> is <c>CClientState::GetFrameTime</c>, which answers zero while <c>IsPaused()</c>, and
/// that is true while <c>demoplayer->IsPlaybackPaused()</c> (engine.dll FUN_1800a2d40 and FUN_1800a34c0, disassembly).
/// So a paused demo walks nothing and, as important, MOVES nothing: <c>m_flPrevEventCycle</c> is written only inside
/// <c>DoAnimationEvents</c> (<c>:3634</c>, <c>:3703</c>, <c>:3734</c>), so the first frame after the pause walks from
/// where the last unpaused frame left it (B541).
///
/// **The fixture's cycle is its demo time**: one sequence a second long, advanced by the client, so
/// <c>seconds: 0.6</c> is cycle 0.6.
/// </remarks>
public sealed class AnimationEventWalkConformanceTests
{
    /// <summary>The sequence the prop plays. Not zero, so the walk's first frame sees a sequence it has not walked.</summary>
    private const int Run = 1;

    private const int Entity = 4;

    private const string Model = "models/props_gameplay/resupply_locker.mdl";

    [Test]
    public void Instances_OnAFrameWithNoFrameTime_FiresNoEventAndLeavesTheWalkWhereItWas()
    {
        EntityModelSet models = Loaded();

        Fired(models, 0.1d).ShouldBe(["zero"], "the control: the walk is live, and has restarted at cycle zero");

        models.Paused = true;

        Fired(models, 0.6d).ShouldBeEmpty("frametime is zero, so Simulate does not call DoAnimationEvents");

        models.Paused = false;

        Fired(models, 0.7d).ShouldBe(["half"], "the paused frame did not move m_flPrevEventCycle past the event at 0.5");
    }

    /// <remarks>The control for the test above: the same two frames with time moving cross the event on the first.</remarks>
    [Test]
    public void Instances_OnAFrameWithFrameTime_FiresTheEventItsCycleCrossed()
    {
        EntityModelSet models = Loaded();

        Fired(models, 0.1d);

        Fired(models, 0.6d).ShouldBe(["half"]);
        Fired(models, 0.7d).ShouldBeEmpty();
    }

    /// <remarks>
    /// <c>c_baseanimating.cpp:3621-3635</c>: <c>m_nEventSequence != GetSequence()</c> sets <c>flEventCycle = 0.0f</c>
    /// and <c>m_flPrevEventCycle = -0.01</c>, so that frame fires only what is authored at cycle zero however far in
    /// the sequence is, and leaves the previous cycle at ZERO — the next frame fires everything in <c>(0, cycle]</c>.
    /// </remarks>
    [Test]
    public void Instances_OnTheFirstFrameOfASequence_FiresOnlyCycleZeroAndTheNextFrameFiresUpToItsCycle()
    {
        EntityModelSet models = Loaded();

        Fired(models, 0.6d).ShouldBe(["zero"], "the first frame walks (-0.01, 0], not (-0.01, 0.6]");
        Fired(models, 0.7d).ShouldBe(["half"], "and the next walks (0, 0.7]");
    }

    /// <remarks>
    /// **A static prop walks no events** (B560, D255): `DoAnimationEvents` runs from `C_BaseAnimating::Simulate`, which
    /// `SimulateEntities` calls for the entity list only (`cdll_client_int.cpp:2010-2015`), and a static prop's draw
    /// (`engine.dll` `0x1800f1bd0`) has no animation step. The same model and sequence as an entity fires "zero" on its
    /// first frame (the tests above).
    /// </remarks>
    [Test]
    public void Instances_AStaticPropOnASequenceWithEvents_FiresNone()
    {
        SceneProp placed = PropModels.StaticModel(new Content.Bsp.BspStaticProp(Model, 0f, 0f, 0f, 0f, 0f, 0f, 1f), 0);
        SceneProp still = placed with { Pose = placed.Pose with { Sequence = Run } };
        EntityModelSet models = new() { Geometry = _ => Built(), StaticProps = [still] };
        List<FiredAnimationEvent> fired = [];

        // The production order: the moment packs, and with it the level's static props, before it poses.
        models.Add([], _ => Built());

        models.Instances([], [], seconds: 0.1d);
        models.TakeFiredEvents(fired);
        models.Instances([], [], seconds: 0.6d);
        models.TakeFiredEvents(fired);

        models.FrameOf(still.EntityIndex).ShouldNotBeNull("the static prop is posed, so the walk could have run")
            .Sequence.ShouldBe(Run, "on the sequence that declares the events");
        fired.ShouldBeEmpty();
    }

    /// <remarks>
    /// What a model's sound precache walks (`CModelSoundsCache::BuildAnimationEventSoundList`,
    /// `game/shared/ModelSoundsCache.cpp:131-218`): every event of every sequence of every model packed, without posing
    /// anything. The control is the same set before the model is packed.
    /// </remarks>
    [Test]
    public void AnimationEvents_OfAPackedModel_AreEverySequencesEventsWithoutPosing()
    {
        new EntityModelSet().AnimationEvents().ShouldBeEmpty("nothing packed, nothing to walk");

        Loaded().AnimationEvents().Select(static one => one.Options).ShouldBe(["zero", "half"]);
    }

    /// <summary>Poses the prop at a demo time and takes what its walk fired, by the events' options.</summary>
    private static string[] Fired(EntityModelSet models, double seconds)
    {
        List<FiredAnimationEvent> fired = [];

        models.Instances([Playing()], [], seconds: seconds);
        models.TakeFiredEvents(fired);

        return [.. fired.Select(static one => one.Event.Options)];
    }

    private static StudioEvent At(float cycle, string options) =>
        new(cycle, StudioEvent.OldSystemClientId + 4, 0, options);

    internal static SceneProp Playing() =>
        new(
            Entity,
            Model,
            ScenePropTrack.Classify(Model),
            new ScenePose { Sequence = Run, Cycle = 0f },
            null,
            ClientSideAnimated: true);

    private static EntityModelSet Loaded()
    {
        EntityModelSet models = new() { Geometry = _ => Built() };

        models.Add([Playing()], _ => Built());

        return models;
    }

    /// <summary>Two sequences, the second declaring a client event at cycle zero and one at a half.</summary>
    internal static PropModels.ModelFrames Built()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.WithFlags(("idle", 0), ("run", 0));
        List<StudioSequence> sequences = [.. model.Groups[0].Sequences];

        sequences[Run] = sequences[Run] with { Events = [At(0f, "zero"), At(0.5f, "half")] };

        List<(int Group, IReadOnlyList<StudioSequence> Sequences)> groups = [(0, sequences)];

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
                Groups = groups,
                Sequences = StudioSequenceTable.Merge(groups),
                Models = [AnimatedStudioBytes.OneSecondLoop(animations: 2, sequences: 2)],
            });
    }
}
