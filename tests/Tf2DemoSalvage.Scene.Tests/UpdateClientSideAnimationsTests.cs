using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// Choosing each drawn player's sequence, once their models are loaded.
/// </summary>
/// <remarks>
/// **This is Valve's pass under Valve's name.** <c>C_BaseAnimating::UpdateClientSideAnimations()</c>
/// (<c>c_baseanimating.cpp:6368</c>) is a static batch walk over <c>g_ClientSideAnimationList</c>,
/// and the engine runs it BEFORE simulation and bones —
/// <c>UpdateClientSideAnimations() → SimulateEntities() → ThreadedBoneSetup()</c> at
/// <c>cdll_client_int.cpp:2188-2210</c>.
///
/// **These tests were written after the move, not before it**, because the behaviour already
/// existed inside <c>MainForm.ShowMoment</c> where nothing could reach it without a window (B188).
/// Each is verified by sabotage instead: the guard is broken on purpose and the right test reddens.
/// What they pin is the two ways this silently corrupts a pose — writing a sequence for a prop that
/// never asked for one, and storing a -1.
/// </remarks>
public sealed class UpdateClientSideAnimationsTests
{
    [Test]
    public void UpdateClientSideAnimations_APropWithNoSpeed_LeavesItsSequenceAlone()
    {
        // **Speed is what marks a prop as a player here.** A crate, a health pack and a door all
        // reach this list, and none of them has a movement activity to choose — writing one would
        // replace whatever sequence the demo actually networked for them.
        //
        // **The model MUST be packed and MUST resolve an activity, or this test cannot fail.**
        // Written first against an empty set, it passed with the speed guard deliberately removed:
        // with nothing packed `SequenceFor` answers -1 and the OTHER guard blocks the write, so
        // correct and broken predicted the same observation. The fix is the input, not the
        // assertion — a standing player wants ACT_MP_STAND_PRIMARY, so a model carrying that label
        // makes the guard the only thing standing between this prop and a rewritten sequence.
        EntityModelSet models = new();

        models.Add([PropAt(sequence: 7, speed: null)], Standing);

        List<SceneProp> drawn = [PropAt(sequence: 7, speed: null)];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.Sequence.ShouldBe(7);
    }

    [Test]
    public void UpdateClientSideAnimations_APropWithSpeed_TakesTheResolvedSequence()
    {
        // **The control for the pair above.** Without it, "never writes anything" passes that test,
        // and the whole pass could be deleted with the suite still green.
        EntityModelSet models = new();

        models.Add([PropAt(sequence: 7, speed: 0f)], Standing);

        List<SceneProp> drawn = [PropAt(sequence: 7, speed: 0f)];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.Sequence.ShouldBe(0, "the only sequence the packed model has");
    }

    [Test]
    public void UpdateClientSideAnimations_AModelWithNoSequenceTable_LeavesItAlone()
    {
        // **-1 is an ANSWER, not a failure to answer**, and storing it replaces a working sequence
        // with one that decodes to nothing — a model frozen on frame zero, which reads as a broken
        // animation rather than as a lookup that found no such activity.
        //
        // A model that was never packed is the cheapest way to reach that answer, and it is also
        // the real case: `SequenceFor` returns -1 for anything it has no merged table for.
        EntityModelSet models = new();

        List<SceneProp> drawn = [PropAt(sequence: 4, speed: 250f)];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.Sequence.ShouldBe(4, "a -1 answer must not be written");
    }

    [Test]
    public void UpdateClientSideAnimations_WithNothingDrawn_DoesNothing()
    {
        // The empty case is the one a first frame actually hits, before any prop has arrived.
        EntityModelSet models = new();

        List<SceneProp> drawn = [];

        models.UpdateClientSideAnimations(drawn);

        drawn.ShouldBeEmpty();
    }

    [Test]
    public void UpdateClientSideAnimations_LeavesEveryOtherFieldOfThePoseUntouched()
    {
        // **The bystander.** This rewrites a pose with `with`, so every other field is copied by
        // the compiler — and a hand-written copy that forgot one would lose it silently. Only the
        // sequence is this pass's business.
        EntityModelSet models = new();

        List<SceneProp> drawn = [PropAt(sequence: 2, speed: 100f)];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.X.ShouldBe(10f);
        drawn[0].Pose.Yaw.ShouldBe(90f);
        drawn[0].Pose.Skin.ShouldBe(1);
        drawn[0].Pose.Speed.ShouldBe(100f);
        drawn[0].EntityIndex.ShouldBe(3);
    }

    /// <summary>A packed model carrying the one activity a standing player asks for.</summary>
    /// <remarks>
    /// <c>ACT_MP_STAND_PRIMARY</c> — `PlayerActivity.NameOf(StandIdle, "PRIMARY")`. Without a label
    /// the resolver can match, `SequenceFor` answers -1 and every test here passes for the wrong
    /// reason.
    /// </remarks>
    private static PropModels.ModelFrames? Standing(string path) =>
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
            Skinned: SyntheticSkinnedModel.With("ACT_MP_STAND_PRIMARY"));

    private static SceneProp PropAt(int sequence, float? speed) =>
        new(
            3,
            "models/player/soldier.mdl",
            SceneModelKind.Studio,
            new ScenePose
            {
                X = 10f,
                Yaw = 90f,
                Scale = 1f,
                Skin = 1,
                Sequence = sequence,
                Speed = speed,
            });

    /// <remarks>
    /// **A corpse rests in `ACT_DIERAGDOLL`, and sequence ZERO is the model's reference pose**
    /// (B316). `InitAsClientRagdoll` overwrites the sequence `CreateTFRagdoll` copied from the
    /// player —
    ///
    /// <code>
    ///   m_nRestoreSequence = GetSequence();
    ///   SetSequence( SelectWeightedSequence( ACT_DIERAGDOLL ) );
    ///   m_flPlaybackRate = 0;
    /// </code>
    ///
    /// `c_baseanimating.cpp:4931` — so the copied one survives only as bone velocity into the
    /// solver, and what a settled corpse is POSED with is this.
    ///
    /// **The fixture's sequence 0 is `ref` because a real one's is.** Measured on all nine class
    /// models: `[0] g0 'ref', [1] g0 'ragdoll' act ACT_DIERAGDOLL`. That is what made the defect
    /// invisible as a lookup failure and visible as a T-pose — `ref` IS the reference pose — and it
    /// is why the assertion below has to distinguish 1 from 0 rather than from −1.
    /// </remarks>
    [Test]
    public void UpdateClientSideAnimations_ACorpse_RestsInTheRagdollActivityRatherThanTheReferencePose()
    {
        EntityModelSet models = new();

        models.Add([Corpse(death: null)], Corpses);

        List<SceneProp> drawn = [Corpse(death: null)];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.Sequence.ShouldBe(1, "'ragdoll', the ACT_DIERAGDOLL sequence");
    }

    /// <remarks>
    /// **The death animation still wins**, because the engine's own condition for reaching
    /// `InitAsClientRagdoll` at all is `!m_bDeathAnim` (`c_tf_player.cpp:857`). Without this case,
    /// "always ACT_DIERAGDOLL" would pass the test above and silently delete B323.
    ///
    /// It is matched by LABEL — `ResetSequence( iDeathSeq )` takes what `LookupSequence` returned —
    /// so the fixture's third sequence answers to its name and not to an activity.
    /// </remarks>
    [Test]
    public void UpdateClientSideAnimations_ACorpseWithADeathAnimation_KeepsItRatherThanResting()
    {
        EntityModelSet models = new();

        models.Add([Corpse(death: "primary_death_headshot")], Corpses);

        List<SceneProp> drawn = [Corpse(death: "primary_death_headshot")];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.Sequence.ShouldBe(2, "the death sequence, matched by label");
    }

    /// <remarks>
    /// **A model with no such activity keeps what it had rather than taking −1.** `ForActivity`
    /// answers −1 for "no sequence answers to this", and storing that freezes the corpse on frame
    /// zero of nothing — the same rule the speed path already keeps. `Standing` carries only
    /// `ACT_MP_STAND_PRIMARY`, so it is the cheapest way to reach that answer.
    /// </remarks>
    [Test]
    public void UpdateClientSideAnimations_ACorpseWhoseModelHasNoRagdollActivity_LeavesItAlone()
    {
        EntityModelSet models = new();

        models.Add([Corpse(death: null)], Standing);

        List<SceneProp> drawn = [Corpse(death: null)];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.Sequence.ShouldBe(9);
    }

    /// <remarks>
    /// The control: a prop that is not a corpse must not be given a corpse's pose. Without it,
    /// "writes ACT_DIERAGDOLL onto everything" passes every case above.
    /// </remarks>
    [Test]
    public void UpdateClientSideAnimations_APropThatIsNotACorpse_IsNotGivenTheRagdollActivity()
    {
        EntityModelSet models = new();

        models.Add([PropAt(sequence: 7, speed: null)], Corpses);

        List<SceneProp> drawn = [PropAt(sequence: 7, speed: null)];

        models.UpdateClientSideAnimations(drawn);

        drawn[0].Pose.Sequence.ShouldBe(7);
    }

    /// <summary>A packed model shaped like a real player model's sequence table.</summary>
    /// <remarks>
    /// Measured on all nine classes: `[0] g0 'ref', [1] g0 'ragdoll' act ACT_DIERAGDOLL`. The third
    /// entry is a death animation, which is matched by label rather than by activity.
    /// </remarks>
    private static PropModels.ModelFrames? Corpses(string path) =>
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
            // **Label and activity DIFFER here, and that is the whole point of this fixture.**
            // `With` sets both to one string, and a corpse test written against it cannot fail:
            // measured, it stayed green with `ForActivity` swapped for `SequenceByLabel`. A real
            // model's ragdoll sequence is LABELLED `ragdoll` and answers to the ACTIVITY
            // `ACT_DIERAGDOLL`, so only a fixture that separates them can tell the two lookups
            // apart — and the death animation below answers to a label and to no activity at all.
            Skinned: SyntheticSkinnedModel.WithActivities(
                ("ref", "ACT_REFERENCE"),
                ("ragdoll", "ACT_DIERAGDOLL"),
                ("primary_death_headshot", "")));

    /// <summary>A corpse, which is what `RagdollProps` builds for a `CTFRagdoll`.</summary>
    /// <remarks>
    /// **Sequence 9 rather than 0**, so "left alone" and "chose the reference pose" are different
    /// observations. With 0 the two are the same number and the no-activity case could not fail.
    /// </remarks>
    private static SceneProp Corpse(string? death) =>
        new(
            3,
            "models/player/soldier.mdl",
            SceneModelKind.Studio,
            new ScenePose
            {
                X = 10f,
                Yaw = 90f,
                Scale = 1f,
                Skin = 1,
                Sequence = 9,
            },
            ClassName: RagdollProps.RagdollClassName,
            DeathSequence: death);
}
