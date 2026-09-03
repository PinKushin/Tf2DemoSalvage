using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// A sequence change cross-fades rather than cutting.
/// </summary>
/// <remarks>
/// **`CSequenceTransitioner` keeps the OUTGOING sequence alive** on a change, with a window taken
/// from both sequences — <c>MIN( prevseqdesc.fadeouttime, seqdesc.fadeintime )</c>
/// (<c>sequence_Transitioner.cpp:46</c>) — and `MaintainSequenceTransitions` accumulates it over
/// the new one until its weight reaches zero (<c>c_baseanimating.cpp:1815</c>).
///
/// **Without it every sequence change is a CUT** (B286): a player who stops running snaps out of
/// the run pose in one frame, and a door that starts opening jumps to its first frame. Nothing
/// below this level notices, because each frame on its own is a correct pose of a correct sequence.
///
/// These assert on <see cref="EntityModelSet.LayersOf"/> — what the skeleton was HANDED, carried
/// rather than recomputed (B243).
/// </remarks>
public sealed class SequenceTransitionTests
{
    [Test]
    public void Instances_AfterASequenceChange_KeepsTheOldSequenceFading()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [Playing(sequence: 0)];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 0d);

        models.LayersOf(4).ShouldNotBeNull().ShouldBeEmpty(
            "nothing has changed yet, so there is nothing to fade");

        drawn[0] = Playing(sequence: 1);

        models.Instances(drawn, [], seconds: 0.05d);

        IReadOnlyList<PoseLayer> fading = models.LayersOf(4).ShouldNotBeNull();

        fading.Count.ShouldBe(1, "the sequence just left is still fading out");
        fading[0].Sequence.ShouldBe(0, "and it is the OLD sequence, not the new one");

        fading[0].Weight.ShouldBeGreaterThan(
            0f, "a quarter of the way through a 0.2 second window it still has weight");
    }

    /// <remarks>
    /// **The control that makes the test above about TIME rather than about change.** Past the fade
    /// window the entry is removed rather than accumulated at no weight, which is what stops the
    /// queue growing — `else m_animationQueue.Remove( i )` (`sequence_Transitioner.cpp:113`).
    /// </remarks>
    [Test]
    public void Instances_OnceTheFadeWindowHasClosed_DropsTheOldSequence()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [Playing(sequence: 0)];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 0d);

        drawn[0] = Playing(sequence: 1);

        models.Instances(drawn, [], seconds: 0.05d);
        models.LayersOf(4).ShouldNotBeNull().ShouldNotBeEmpty("still inside the window");

        models.Instances(drawn, [], seconds: 5d);

        models.LayersOf(4).ShouldNotBeNull().ShouldBeEmpty(
            "five seconds is far past any authored fade, and a finished entry leaves the queue");
    }

    /// <remarks>
    /// **The weight falls as the window closes**, which is the difference between a cross-fade and
    /// simply drawing two sequences. A test that only checked presence would pass against a layer
    /// stuck at full weight, which would look like the old sequence never leaving.
    /// </remarks>
    [Test]
    public void Instances_AsTheWindowCloses_TheOldSequenceLosesWeight()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [Playing(sequence: 0)];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 0d);

        drawn[0] = Playing(sequence: 1);

        models.Instances(drawn, [], seconds: 0.05d);
        float early = models.LayersOf(4).ShouldNotBeNull().Single().Weight;

        models.Instances(drawn, [], seconds: 0.15d);
        float late = models.LayersOf(4).ShouldNotBeNull().Single().Weight;

        late.ShouldBeLessThan(early, "the fade is a curve, not a switch");
    }

    /// <remarks>
    /// **A sequence can change without its NUMBER changing, and that is what the parity is for.**
    /// `CheckForSequenceChange` triggers on
    /// <c>currentblend-&gt;m_nSequence != nCurSequence || bForceNewSequence</c>
    /// (<c>sequence_Transitioner.cpp:38</c>), and `bForceNewSequence` is
    /// <c>m_nNewSequenceParity != m_nPrevNewSequenceParity</c>
    /// (<c>c_baseanimating.cpp:1831</c>) — a counter the server bumps on every `SetSequence`, so a
    /// cabinet opened twice restarts twice and only the counter says the second one began.
    ///
    /// **Comparing sequence numbers alone makes a replay SNAP.** Reaching here, the restart has
    /// already been turned into a new `AnimationStartSeconds` by the timeline, which is the same
    /// event one hop later and the only copy of it this layer sees.
    /// </remarks>
    [Test]
    public void Instances_WhenTheSameSequenceRestarts_KeepsTheOutgoingInstanceFading()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [Playing(sequence: 0)];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 0d);

        drawn[0] = Playing(sequence: 0, startedAt: 0.05d);

        models.Instances(drawn, [], seconds: 0.05d);

        IReadOnlyList<PoseLayer> fading = models.LayersOf(4).ShouldNotBeNull();

        fading.Count.ShouldBe(1, "the instance that just ended is still fading out");
        fading[0].Sequence.ShouldBe(0, "and it is the same sequence, playing its previous run");
    }

    /// <remarks>
    /// **The control, and without it the test above passes on any prop seen twice.** The same
    /// sequence at the same start time is not a restart and must queue nothing — which is also
    /// what stops every entity accumulating a fade on every frame it is drawn.
    /// </remarks>
    [Test]
    public void Instances_WhenNothingRestarted_QueuesNothing()
    {
        EntityModelSet models = new() { Geometry = _ => Frames() };

        List<SceneProp> drawn = [Playing(sequence: 0)];

        models.Add(drawn, _ => Frames());
        models.Instances(drawn, [], seconds: 0d);
        models.Instances(drawn, [], seconds: 0.05d);

        models.LayersOf(4).ShouldNotBeNull().ShouldBeEmpty(
            "nothing began again, so there is nothing to fade");
    }

    /// <summary>A prop playing one sequence, client-side animated so its cycle advances.</summary>
    /// <param name="sequence">Which sequence it plays.</param>
    /// <param name="startedAt">
    /// When that run of the animation began — the timeline's record of the restart signal.
    /// </param>
    private static SceneProp Playing(int sequence, double startedAt = 0d) =>
        new(
            4,
            "models/props_gameplay/resupply_locker.mdl",
            ScenePropTrack.Classify("models/props_gameplay/resupply_locker.mdl"),
            new ScenePose
            {
                Sequence = sequence,
                Cycle = 0f,
                AnimationStartSeconds = startedAt,
            },
            null,
            ClientSideAnimated: true);

    /// <summary>A model with three sequences and real animation bytes behind them.</summary>
    /// <remarks>
    /// **Real bytes, because a transition is arithmetic on time.** The default empty studio bytes
    /// report one frame at zero cycles a second, so a faded sequence could not advance and the
    /// weight curve would be the only thing under test.
    ///
    /// **The fade window comes from the sequence descriptors**, which these bytes do not carry — so
    /// `FadeIn` and `FadeOut` read zero here and the transition would never start. The fixture
    /// writes both fields for every sequence, which is what makes this a test of the transition
    /// rather than of an absent one.
    /// </remarks>
    private static PropModels.ModelFrames Frames()
    {
        PropModels.SkinnedModel model = SyntheticSkinnedModel.With("first", "second", "third");

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
                Models = [AnimatedStudioBytes.OneSecondLoop(animations: 3, sequences: 3)],
            });
    }
}
