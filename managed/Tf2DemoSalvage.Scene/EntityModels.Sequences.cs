using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>
    /// The content era of the demo being drawn, from its network protocol; null reads every sequence
    /// number against today's models (B380, D160).
    /// </summary>
    /// <remarks>
    /// Set when a demo opens, like the rest of the per-demo state, and left null for a protocol the
    /// table has no era for — 24 today, until snapshots from 2013 onward are in.
    /// </remarks>
    public string? SequenceEra { get; set; }

    /// <summary>The era table, the one this program ships unless a test supplies another.</summary>
    public SequenceEras Eras { get; set; } = SequenceEras.Shipped;

    /// <summary>Takes each prop's networked sequence numbers from its era's model to today's (B380, D160).</summary>
    /// <param name="props">The props about to be posed; rewritten in place.</param>
    /// <exception cref="ArgumentNullException"><paramref name="props"/> is null.</exception>
    /// <remarks>
    /// **The main sequence AND every layer**, because a layer's <c>m_nSequence</c> indexes the same merged
    /// list. **After the models are loaded and before <see cref="Instances"/>**, whose first act is
    /// <c>Simulate</c>, which advances each cycle by its sequence's frame rate — translated afterwards,
    /// the first frame would already have been advanced by the wrong animation.
    ///
    /// **A model the era does not list is untouched**, which is every model whose list is the same
    /// today; so is every prop when <see cref="SequenceEra"/> is null. A model not yet loaded, or baked
    /// rather than skinned, has no table to translate into and is left as it came.
    /// </remarks>
    public void TranslateEraSequences(IList<SceneProp> props)
    {
        ArgumentNullException.ThrowIfNull(props);

        // Stryker disable once : a mutant that empties the guard body leaves 'era'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (SequenceEra is not { } era)
        {
            return;
        }

        for (int index = 0; index < props.Count; index++)
        {
            SceneProp prop = props[index];

            // Stryker disable all : a mutant that empties the guard body leaves 'old' and
            // 'skinned' unassigned below (CS0165), and Safe Mode then drops every mutation in
            // this method — B410.
            if (Eras.For(era, prop.ModelPath) is not { } old ||
                !_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? frames) ||
                frames.Skinned is not { } skinned)
            {
                continue;
            }

            // Stryker restore all

            int sequence = EraSequenceTranslation.Translate(
                old, prop.Pose.Sequence, skinned.SequenceByLabel, skinned.SequenceWithActivity);

            IReadOnlyList<SceneAnimationLayer> layers = prop.Pose.Layers;

            if (layers.Count > 0)
            {
                SceneAnimationLayer[] translated = new SceneAnimationLayer[layers.Count];

                for (int layer = 0; layer < layers.Count; layer++)
                {
                    translated[layer] = layers[layer] with
                    {
                        Sequence = EraSequenceTranslation.Translate(
                            old, layers[layer].Sequence, skinned.SequenceByLabel, skinned.SequenceWithActivity),
                    };
                }

                layers = translated;
            }

            props[index] = prop with { Pose = prop.Pose with { Sequence = sequence, Layers = layers } };
        }
    }

    /// <summary>The first sequence of a model whose activity contains a fragment.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="fragment">Part of an activity name, such as <c>VM_IDLE</c>.</param>
    /// <returns>A merged sequence number, or −1 when nothing claims it.</returns>
    /// <remarks>
    /// **Asked by NAME because a demo's sequence number cannot be checked against anything.** The
    /// viewmodel arms were playing merged sequence 1, which on an arms model is <c>r_handposes</c> —
    /// a one-frame pose holder that leaves the root bone at identity and the model in its authored
    /// Y-up space. The animations a viewmodel actually plays begin at 2 and carry
    /// <c>ACT_*_VM_IDLE</c> and its neighbours.
    /// </remarks>
    public int SequenceByActivity(string modelPath, string fragment)
    {
        if (!_frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames))
        {
            // **Three ways to answer "no" and they are different faults**, which is the whole
            // reason they are separated: the model was never packed, the model was packed without
            // a skeleton, or the skeleton simply has no such activity. A single −1 sent this
            // investigation at a sequence list that turned out to contain exactly what was being
            // looked for.
            _render.LogWarning(
                "no packed frames for {Model}, so no activity lookup", modelPath);
            return -1;
        }

        // Stryker disable once : a mutant that empties the guard body leaves 'skinned'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (frames.Skinned is not { } skinned)
        {
            _render.LogWarning(
                "{Model} was baked rather than skinned, so it has no sequence table", modelPath);
            return -1;
        }

        return skinned.SequenceByActivity(fragment);
    }

    /// <summary>The activity every player model's resting corpse pose answers to (B316).</summary>
    /// <remarks>
    /// **Measured: all nine class models ship exactly ONE sequence with it**, `'ragdoll'`, so the
    /// weighted pick `SelectWeightedSequence` performs has nothing to pick between here — which is
    /// why taking the highest-weighted candidate rather than drawing among ties is not a divergence
    /// on this input.
    /// </remarks>
    private const string RagdollRestActivity = "ACT_DIERAGDOLL";

    /// <summary>Which sequence a corpse rests in (B316).</summary>
    /// <param name="prop">The corpse, whose model has been loaded by now.</param>
    /// <returns>The sequence to pose it with, or the one it already carries.</returns>
    /// <remarks>
    /// **The engine sets this TWICE and the second one wins**, which is the whole finding.
    /// `CreateTFRagdoll` copies the player's sequence —
    ///
    /// <code>
    ///   m_flAnimTime = pPlayer-&gt;m_flAnimTime;
    ///   SetSequence( pPlayer-&gt;GetSequence() );
    ///   m_flPlaybackRate = pPlayer-&gt;GetPlaybackRate();
    /// </code>
    ///
    /// `c_tf_player.cpp:757-766` — and then, unless a death animation was chosen,
    /// `InitAsClientRagdoll` overwrites it:
    ///
    /// <code>
    ///   m_nRestoreSequence = GetSequence();
    ///   SetSequence( SelectWeightedSequence( ACT_DIERAGDOLL ) );
    ///   m_nPrevSequence = GetSequence();
    ///   m_flPlaybackRate = 0;
    /// </code>
    ///
    /// `c_baseanimating.cpp:4931`. The copied sequence survives only as bone VELOCITY into the
    /// physics solver (`RagdollApplyAnimationAsVelocity`); what a settled corpse is POSED with is
    /// `ACT_DIERAGDOLL` at playback rate zero, and `BuildTransformations` applies it to every bone
    /// the physics does not drive.
    ///
    /// **This is why the corpse stood up straight.** B316 was filed as "copy the player's
    /// sequence", which needs this project's own client-side animation state and is the hard path;
    /// it is also the wrong branch for the resting pose. Sequence zero was neither rule — it is
    /// what a `ScenePose` holds when nothing has set it.
    ///
    /// **The death animation still wins where there is one**, because the engine's own condition
    /// for reaching `InitAsClientRagdoll` at all is `!m_bDeathAnim`.
    /// </remarks>
    private int CorpseSequence(SceneProp prop)
    {
        // Stryker disable all : a mutant that empties the guard body leaves 'corpse'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (!_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? bodies) ||
            bodies.Skinned is not { } corpse)
        {
            return prop.Pose.Sequence;
        }

        // Stryker restore all

        // `ResetSequence( iDeathSeq )` (`c_tf_player.cpp:851`); the label comes from
        // `LookupSequence`, so it is matched by label and never by activity.
        if (prop.DeathSequence is { Length: > 0 } wanted &&
            corpse.SequenceByLabel(wanted) is >= 0 and int death)
        {
            return death;
        }

        // **The player's OWN sequence, which is what the engine actually copies** (B316).
        //
        //     m_flAnimTime = pPlayer->m_flAnimTime;
        //     SetSequence( pPlayer->GetSequence() );
        //     m_flPlaybackRate = pPlayer->GetPlaybackRate();
        //
        // `c_tf_player.cpp:757-766`, under `!pPlayer->IsLocalPlayer()` — and a SourceTV recording
        // has no local player, so every corpse in one takes that branch. `ACT_DIERAGDOLL` below is
        // NOT this: it is a stiff rest pose, and a corpse seeded from it starts its physics with
        // its limbs spread instead of crumpling from whatever the player was mid-way through.
        // Measured on `z1800`: an engineer's corpse stood bolt upright with its arms out for the
        // first tenth of a second before the solve pulled it down — the owner's *"contorted t pose
        // like state"*.
        //
        // **The inputs come from the corpse rather than the player**, because a player's speed is
        // never on the wire; `RagdollProps` reads them off `m_vecRagdollVelocity` and `m_bOnGround`,
        // which are the same two facts the player's own activity was chosen from.
        if (prop.Pose.Speed is { } speed &&
            SequenceFor(
                prop.ModelPath,
                speed,
                prop.Pose.Flags,

                // The corpse is drawn, so the animation is chosen the way a drawn player's is;
                // `alive` here selects the standing activities rather than gating on life.
                alive: true,
                slot: prop.Pose.Slot ?? "PRIMARY") is >= 0 and int copied)
        {
            return copied;
        }

        // **A negative answer is left alone rather than written.** −1 means "this model has no such
        // sequence", and storing it would freeze the corpse on frame zero of nothing.
        int resting = corpse.ForActivity(RagdollRestActivity);

        return resting >= 0 ? resting : prop.Pose.Sequence;
    }

    /// <summary>Chooses each drawn player's sequence, now that their models are loaded.</summary>
    /// <param name="drawn">The draw list, updated in place.</param>
    /// <exception cref="ArgumentNullException"><paramref name="drawn"/> is null.</exception>
    /// <remarks>
    /// **Named for Valve's own pass, because it IS Valve's own pass.**
    /// <c>C_BaseAnimating::UpdateClientSideAnimations()</c> (<c>c_baseanimating.cpp:6368</c>) is a
    /// static batch walk over <c>g_ClientSideAnimationList</c> calling
    /// <c>UpdateClientSideAnimation()</c> on each — a loop over a list, exactly this shape, rather
    /// than something each entity does for itself. Per entity it reaches
    /// <c>CMultiPlayerAnimState::ComputeMainSequence</c> (<c>multiplayer_animstate.cpp:1125</c>),
    /// which is what <see cref="SequenceFor"/> stands in for.
    ///
    /// **And the ORDER is Valve's too**, which is worth stating because it is the part that is easy
    /// to get wrong later. `cdll_client_int.cpp:2188-2210` runs
    /// `UpdateClientSideAnimations()` → `SimulateEntities()` → `ThreadedBoneSetup()`, so sequence
    /// selection happens BEFORE simulation and before any bone is built. Ours matches: this runs
    /// before <see cref="Instances"/>, which does <c>Simulate</c> and then the bones.
    ///
    /// **After the models are loaded, and that is a real constraint rather than a convenience.**
    /// Nothing on the wire carries a player's sequence, and choosing one needs the model's merged
    /// sequence table — which does not exist until <see cref="Add(IReadOnlyList{SceneProp})"/> has
    /// read it. Asked earlier it
    /// answers -1, and -1 is a real answer meaning "no such sequence", so an early call looks like a
    /// lookup that failed rather than one that ran too soon.
    ///
    /// Lived in <c>MainForm.ShowMoment</c> until 2026-08-25 (B188).
    /// </remarks>
    public void UpdateClientSideAnimations(IList<SceneProp> drawn)
    {
        ArgumentNullException.ThrowIfNull(drawn);

        for (int index = 0; index < drawn.Count; index++)
        {
            SceneProp prop = drawn[index];

            // **A corpse's death animation, resolved here because this is where a prop's sequence is
            // decided** (B323). It comes BEFORE the speed guard rather than after: a corpse carries
            // no speed — it is not a player and never reaches the activity state machine — so a
            // corpse placed below that `continue` would be skipped and the branch would be another
            // no-op with a green suite.
            //
            // `ResetSequence( iDeathSeq )` is what the engine does with the answer
            // (`c_tf_player.cpp:851`); the label comes from `LookupSequence`, so it is matched by
            // label and never by activity.
            if (string.Equals(prop.ClassName, RagdollProps.RagdollClassName, StringComparison.Ordinal))
            {
                // Copied only when the sequence changes — `SetSequence` writes one field, and a settled corpse's
                // whole-prop copy every tick was the corpse worker's second-largest allocation (B574).
                int sequence = CorpseSequence(prop);

                if (sequence != prop.Pose.Sequence)
                {
                    drawn[index] = prop with { Pose = prop.Pose with { Sequence = sequence } };
                }

                continue;
            }

            // Stryker disable once : a mutant that empties the guard body leaves 'speed'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (prop.Pose.Speed is not { } speed)
            {
                continue;
            }

            // Stryker disable once : as above, an emptied guard leaves 'skinned' unassigned — B410.
            if (!_frames.TryGetValue(prop.ModelPath, out PropModels.ModelFrames? frames) || frames.Skinned is not { } skinned)
            {
                continue;
            }

            int chosen = DesiredSequence(prop, skinned, speed);

            // **`if ( animDesired < 0 ) animDesired = 0;`** (`multiplayer_animstate.cpp:1174-1177`): a failed lookup is
            // sequence 0 outright, and only a real answer is rolled by weight.
            chosen = MainSequence(prop, skinned, Math.Max(0, chosen), roll: chosen >= 0);

            if (chosen != prop.Pose.Sequence)
            {
                drawn[index] = prop with { Pose = prop.Pose with { Sequence = chosen } };
            }
        }
    }

    /// <summary>`TranslateActivity( idealActivity )`'s heaviest answer for a drawn player: what his activity is.</summary>
    /// <returns>A merged sequence number, or −1 when nothing answers.</returns>
    /// <remarks>One function, because the seek scan (<see cref="ActivityBegan"/>) must ask exactly what the frame asks.</remarks>
    private static int DesiredSequence(SceneProp prop, PropModels.SkinnedModel skinned, float speed) =>
            PlayerAnimation.ForOrMissing(
                skinned,
                speed,
                prop.Pose.Flags,

                // **True because the dead never reach here, not because death is ignored.**
                // `PlayerProps.ModelFor` refuses a player the engine would not draw, and TF2 turns
                // a dead player off with EF_NODRAW while a separate CTFRagdoll becomes the corpse.
                //
                // An earlier comment claimed a ragdoll was already doing that job, which was false
                // in both directions: nothing here draws ragdolls, and dead players WERE reaching
                // this call. With their ground flag clear they were then given ACT_MP_JUMP_FLOAT,
                // so seventeen seconds of a respawn drew a soldier falling through the air.
                alive: true,

                // The weapon's suffix, or the primary forms when nothing resolved it — which is
                // what the engine falls back to as well.
                slot: prop.Pose.Slot ?? "PRIMARY",

                // HandleJumping's answer, which outranks everything below it (B437).
                jumping: prop.Pose.JumpActivity,

                // Waist deep turns a jump into a swim.
                waterLevel: prop.Pose.WaterLevel,

                // The player's own table before the weapon's, and the winner's stand after it (B437).
                table: prop.Pose.ActivityOverride,
                competitiveWinnerClass: prop.Pose.CompetitiveWinnerClass,

                // The held item's own replacements, between the weapon and the winner (B437).
                item: prop.Pose.ItemActivities,

                // TF's HandleDucking and HandleMoving inputs (B437).
                posture: prop.Pose.Posture);

    /// <summary>Each player's main sequence as `ComputeMainSequence` last left it, the tick it was last asked and on which model.</summary>
    /// <remarks>
    /// **A cache, not the state** (D233): the pick is a function of (entity, activity, the tick the activity began), and
    /// <see cref="ActivityBegan"/> recovers that tick from the timeline whenever this cannot vouch for it — the clock ran
    /// back, jumped forward past <see cref="ContinuityTicks"/>, or the model changed. So a seek and a play-through run the
    /// same code and land on the same variant.
    /// </remarks>
    /// <remarks>`Recovered` is false for what a skip's frame wrote without scanning (<see cref="SimulatesOnly"/>).</remarks>
    private readonly Dictionary<int, (int Sequence, double Tick, string Model, bool Recovered)> _mainSequences = [];

    /// <summary>The player's prop as it would be drawn at an earlier whole tick, or null when he was not drawn then (D233).</summary>
    /// <remarks>
    /// Side-effect free — set by <c>MomentScene</c> over the timeline, never over the live per-frame sampler, whose
    /// viewpoint and prediction caches a backward scan would reset. Null keys a roll on the tick it is made at, which is
    /// right only for a demo played through from where it is first seen.
    /// </remarks>
    public Func<double, int, SceneProp?>? PlayerPropAt { get; set; }

    /// <summary>How far a frame may advance and still hold: past this the hold is re-derived, as for a seek.</summary>
    /// <remarks>
    /// Four ticks spans a frame at about 16.5 fps on a 66-tick server. A bigger forward gap — a lower frame rate, fast
    /// playback, a seek — scans the skipped ticks (<see cref="HoldsAcross"/>), which allocates; within it nothing is
    /// scanned and nothing allocated.
    /// </remarks>
    internal const double ContinuityTicks = 4d;

    /// <summary>How far back <see cref="ActivityBegan"/> looks: 2048 ticks, 31 s at 66 ticks a second.</summary>
    /// <remarks>
    /// **The guarantee's ceiling.** Play-through holds a roll however long the activity lasts; a seek into a stretch
    /// that began more than this before the landing keys on the earliest tick inside the window instead, and may show
    /// another variant. Cost per seek: `docs/measurements/2026-10-25-d233-activity-tuples-cost.md`.
    /// </remarks>
    internal const int ActivityScanTicks = 2048;

    /// <summary>The whole tick the player's current activity began, recovered from the timeline (D233).</summary>
    /// <param name="prop">The player as drawn this frame.</param>
    /// <param name="skinned">His model.</param>
    /// <param name="desired">This frame's heaviest answer for his activity.</param>
    /// <param name="wanted">Its activity name.</param>
    /// <returns>
    /// The earliest whole tick of the stretch that ends now: every tick back to it on which he was drawn shows the same
    /// activity on the same model. A tick he was not drawn on — dead, not yet joined, before the timeline — is passed
    /// over rather than ending the stretch, because a dead player is not animated and `ClearAnimationState` leaves the
    /// main sequence alone (`multiplayer_animstate.cpp:136-146`, `:1381-1395`), so he respawns into the variant he died
    /// in. When the frame is between ticks and the whole tick under it still shows the old activity, the answer is the
    /// next whole tick — the first that will show the new one.
    /// </returns>
    private long ActivityBegan(SceneProp prop, PropModels.SkinnedModel skinned, int desired, string wanted) =>
        PlayerPropAt is null
            ? (long)Math.Floor(CurrentTick)
            : Scan(prop, skinned, desired, wanted, (long)Math.Floor(CurrentTick) - ActivityScanTicks).Began;

    /// <summary><see cref="ActivityBegan"/>'s walk, from now back to just above <paramref name="above"/>.</summary>
    /// <returns>The earliest tick of the stretch found, and whether a tick of another activity or model ended it.</returns>
    private (long Began, bool Broke) Scan(SceneProp prop, PropModels.SkinnedModel skinned, int desired, string wanted, long above)
    {
        long now = (long)Math.Floor(CurrentTick);
        long began = now + 1;

        for (long tick = now; tick > above; tick--)
        {
            if (PlayerPropAt!(tick, prop.EntityIndex) is not { Pose.Speed: { } speed } then)
            {
                continue;
            }

            if (!string.Equals(then.ModelPath, prop.ModelPath, StringComparison.Ordinal) ||
                !SameActivity(skinned, Math.Max(0, DesiredSequence(then, skinned, speed)), desired, wanted))
            {
                return (began, true);
            }

            began = tick;
        }

        return (began, false);
    }

    /// <summary>
    /// Whether a hold survives a forward jump past <see cref="ContinuityTicks"/> — low frame rate or fast playback: only
    /// the skipped whole ticks are scanned, and an unbroken gap keeps the roll a full scan would have keyed the same way.
    /// </summary>
    private bool HoldsAcross(SceneProp prop, PropModels.SkinnedModel skinned, int desired, string wanted, double since) =>
        PlayerPropAt is not null &&
        CurrentTick - since <= ActivityScanTicks &&
        !Scan(prop, skinned, desired, wanted, (long)Math.Floor(since)).Broke;

    /// <summary>`GetSequenceActivity( a ) == GetSequenceActivity( b )`, with an activity-less sequence equal only to itself.</summary>
    private static bool SameActivity(PropModels.SkinnedModel skinned, int sequence, int desired, string wanted) =>
        sequence == desired ||
        (wanted.Length > 0 && string.Equals(skinned.ActivityOf(sequence), wanted, StringComparison.OrdinalIgnoreCase));

    /// <summary>The roll for a player entering an activity: the engine's `RandomInt`, reproducible per demo (D233).</summary>
    /// <param name="entity">The player's entity index.</param>
    /// <param name="activity">The activity entered, compared ignoring case as the activity map does.</param>
    /// <param name="tick">The demo tick the activity began.</param>
    /// <returns>A 32-bit value; <see cref="PropModels.SkinnedModel.SelectWeightedSequence"/> reduces it by the total weight.</returns>
    /// <remarks>
    /// **The one addition over the engine**, whose `RandomInt` differs every client run. A viewer seeks and replays, and
    /// an unseeded roll would re-pick on every seek; a hash of (entity, activity, tick) makes the same moment show the
    /// same variant. It decides which value comes up, never how often each comes up — the distribution test holds that.
    /// No separate per-demo seed is mixed in (RISKS B584). A seek into an activity keys on the tick it began, recovered
    /// by <see cref="ActivityBegan"/>, so it matches playing through. SplitMix64's finaliser over an
    /// FNV-1a of the upper-cased name: allocation-free and stable across runs, which `HashCode` is not.
    /// </remarks>
    internal static uint ActivityPickSeed(int entity, string activity, long tick)
    {
        ulong name = 14695981039346656037UL;

        foreach (char c in activity)
        {
            name = (name ^ char.ToUpperInvariant(c)) * 1099511628211UL;
        }

        ulong z = name ^ ((ulong)(uint)entity << 32) ^ (ulong)tick;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (uint)z;
    }

    /// <summary>Players whose next <see cref="Advance"/> starts the cycle at zero.</summary>
    private readonly HashSet<int> _restartCycles = [];

    /// <summary>The activities the animstate enters or leaves through `RestartMainSequence`: jumping, air-walking, swimming.</summary>
    private static bool RestartsTheCycle(string activity) =>
        activity.Contains("_JUMP", StringComparison.OrdinalIgnoreCase) ||
        activity.Contains("_AIRWALK", StringComparison.OrdinalIgnoreCase) ||
        activity.Contains("_SWIM", StringComparison.OrdinalIgnoreCase);

    /// <summary>`CMultiPlayerAnimState::ComputeMainSequence`'s keep: the current sequence stays while its activity is the one wanted.</summary>
    /// <remarks>
    /// <code>
    /// int animDesired = SelectWeightedSequence( TranslateActivity( idealActivity ) );
    /// if ( GetSequenceActivity( GetSequence() ) == GetSequenceActivity( animDesired ) ) return;
    /// ResetSequence( animDesired );
    /// </code>
    /// (`multiplayer_animstate.cpp:1168`). A sequence change bumps `m_nResetEventsParity`, which restarts the event walk
    /// at cycle 0 while the cycle runs on, so the next frame fires every event up to it — the engine's own burst,
    /// which only an activity change may cause (B172).
    ///
    /// **The roll is kept only on an activity change, so it happens once and then holds** (D233): `desired` is the
    /// heaviest answer, which stands for its activity; on a change the sequence is rolled by weight from
    /// <see cref="ActivityPickSeed"/> keyed on the tick the activity began, which <see cref="ActivityBegan"/> recovers
    /// from the timeline — so a seek shows what playing through showed. The roll passes no current sequence: on a
    /// change the current one belongs to another activity, so the negative-weight keep (`animation.cpp:291-297`)
    /// cannot fire, and a seek has none.
    /// </remarks>
    private int MainSequence(SceneProp prop, PropModels.SkinnedModel skinned, int desired, bool roll)
    {
        bool cached = _mainSequences.TryGetValue(prop.EntityIndex, out (int Sequence, double Tick, string Model, bool Recovered) last) &&
            CurrentTick >= last.Tick &&
            string.Equals(last.Model, prop.ModelPath, StringComparison.Ordinal) &&
            (last.Recovered || SimulatesOnly);
        string wanted = skinned.ActivityOf(desired);

        // **A skip's frames scan nothing** (B504's 99-packet frames, never drawn): they hold across any gap and roll on
        // their own tick, and what they write is marked unrecovered, so the landing frame recovers every player once.
        bool held = cached &&
            (CurrentTick - last.Tick <= ContinuityTicks ||
                (SameActivity(skinned, last.Sequence, desired, wanted) &&
                    (SimulatesOnly || HoldsAcross(prop, skinned, desired, wanted, last.Tick))));
        int current = held ? last.Sequence : -1;

        if (held)
        {
            string was = skinned.ActivityOf(current);

            if (SameActivity(skinned, current, desired, wanted))
            {
                _mainSequences[prop.EntityIndex] = (current, CurrentTick, prop.ModelPath, last.Recovered && !SimulatesOnly);
                return current;
            }

            // **`RestartMainSequence` — `SetCycle( 0 )` — at every jump, landing and swim edge** (`tf_playeranimstate.cpp`
            // 1189, 1452, 1459, 1487, 1494, 1503). Everywhere else the cycle runs on through a sequence change.
            if (RestartsTheCycle(was) || RestartsTheCycle(wanted))
            {
                _restartCycles.Add(prop.EntityIndex);
            }
        }

        if (roll && wanted.Length > 0)
        {
            desired = skinned.SelectWeightedSequence(
                wanted,
                -1,
                ActivityPickSeed(
                    prop.EntityIndex,
                    wanted,
                    SimulatesOnly ? (long)Math.Floor(CurrentTick) : ActivityBegan(prop, skinned, desired, wanted)));
        }

        _mainSequences[prop.EntityIndex] = (desired, CurrentTick, prop.ModelPath, !SimulatesOnly);
        return desired;
    }

    /// <summary>Which sequence a player of this model should play.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <param name="speed">Horizontal speed in units a second.</param>
    /// <param name="flags">
    /// The player's <c>m_fFlags</c>, carrying the crouch and ground bits, or null when the recording
    /// did not say. Not a POV-versus-SourceTV distinction, as this said before B103: the property is
    /// on <c>DT_BasePlayer</c> and reaches every player in the PVS.
    /// </param>
    /// <param name="alive">Whether the player is alive.</param>
    /// <param name="slot">The table the held weapon drives, such as <c>SECONDARY</c>.</param>
    /// <param name="jumping">HandleJumping's answer, or null when it returned false (B437).</param>
    /// <param name="waterLevel">How deep in water they are; 2 or more is waist deep.</param>
    /// <param name="table">The player's own activity table (B437).</param>
    /// <param name="competitiveWinnerClass">The class of a competitive winner, else null (B437).</param>
    /// <param name="item">The held item's `animation_replacement` rows, or null (B437).</param>
    /// <param name="posture">TF's HandleDucking and HandleMoving inputs (B437).</param>
    /// <returns>A merged sequence number, or −1 when the model is not skinned.</returns>
    /// <remarks>
    /// Asked of the set rather than of the model directly, because only the set knows whether a
    /// model was loaded skinned - a baked model has no merged sequence table to search.
    /// </remarks>
    public int SequenceFor(
        string modelPath,
        float speed,
        int? flags = null,
        bool alive = true,
        string slot = "PRIMARY",
        PlayerActivity? jumping = null,
        int? waterLevel = null,
        PlayerActivityOverride table = PlayerActivityOverride.None,
        int? competitiveWinnerClass = null,
        IReadOnlyDictionary<string, string>? item = null,
        TfPosture posture = default) =>
        _frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames) &&
        frames.Skinned is { } skinned
            ? PlayerAnimation.For(
                skinned, speed, flags, alive, slot, jumping, waterLevel, table, competitiveWinnerClass, item, posture)
            : -1;

    /// <summary>The pieces a model breaks into, empty when it declares none (B371).</summary>
    /// <param name="modelPath">The model.</param>
    /// <returns>Its <c>break</c> blocks, or an empty list.</returns>
    /// <remarks>
    /// **Empty rather than null for a model that is not loaded YET**, which is a real state: a
    /// corpse can be drawn before its class model has been read. A gibbed corpse then draws no
    /// pieces for those frames, which is right — the alternative would be to draw its body, and the
    /// engine has already removed that.
    /// </remarks>
    public IReadOnlyList<PhysicsBreakPiece> BreakPiecesOf(string modelPath) =>
        _frames.TryGetValue(modelPath, out PropModels.ModelFrames? frames)
            ? frames.BreakPieces ?? []
            : [];

    /// <summary>Every baked frame's batches for one model.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <returns>One entry per baked frame, each a list of runs.</returns>
    public IReadOnlyList<IReadOnlyList<WorldBatch>> AllFrames(string modelPath) =>
        _byModel.TryGetValue(modelPath, out List<List<WorldBatch>>? frames) ? frames : [];

    /// <summary>A body number with one named body part set to one of its alternatives.</summary>
    /// <param name="modelPath">The model whose parts are being addressed.</param>
    /// <param name="group">The part's name, as <c>FindBodygroupByName</c> takes it.</param>
    /// <param name="value">Which alternative.</param>
    /// <param name="body">The body number to start from.</param>
    /// <returns>The new body number, or <paramref name="body"/> when this model has no such part.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// **The pair of engine functions, together, because separately they invite the wrong
    /// arithmetic.** `FindBodygroupByName` (`shared/animation.cpp:927`) turns a name into an index
    /// and `SetBodygroup` (`:863`) folds a value into the number:
    ///
    /// <code>
    ///   int iCurrent = ( body / pbodypart-&gt;base ) % pbodypart-&gt;nummodels;
    ///   body = ( body - ( iCurrent * pbodypart-&gt;base ) + ( iValue * pbodypart-&gt;base ) );
    /// </code>
    ///
    /// Parts share one integer like digits of a mixed-radix number, so this is not an OR — and
    /// **that is why the number to fold into is a parameter** (B352). A player wearing two items
    /// that both hide `hat` must land on 1; contributions returned separately and added land on 2,
    /// which overflows the part's digit and carries into the NEXT part, removing a piece the
    /// arithmetic never mentioned. The arithmetic itself lives in <see cref="StudioModel"/>, once.
    ///
    /// **The body unchanged when the model is not loaded YET**, which is a real state: a model is
    /// packed on first sight, so the first frame a spy appears on answers zero and the next answers
    /// correctly. The alternative would be to block the scene on a model load, which is worse than
    /// one frame of an unmasked spy.
    /// </remarks>
    public int WithBodygroup(string modelPath, string group, int value, int body) =>
        SetBodygroup(modelPath, FindBodygroup(modelPath, group), value, body);

    /// <inheritdoc/>
    /// <remarks>
    /// <c>FindBodygroupByName</c>, <c>shared/animation.cpp:927</c> — a linear search of the model's
    /// part names, case-insensitive, answering -1 for a model that has no such part. **-1 is an
    /// ordinary answer rather than an error**: `medic.mdl` ships with two body parts and neither is
    /// called `hat`, so every medic cosmetic that names one gets it (B352).
    ///
    /// **Also -1 when the model is not loaded YET**, which is a real state: a model is packed on
    /// first sight, so the first frame a player appears on resolves nothing and the next resolves
    /// correctly.
    /// </remarks>
    public int FindBodygroup(string modelPath, string group)
    {
        ArgumentNullException.ThrowIfNull(modelPath);
        ArgumentNullException.ThrowIfNull(group);

        if (!_frames.TryGetValue(modelPath, out PropModels.ModelFrames? model)
            || model.BodyPartNames is not { Count: > 0 } names)
        {
            return -1;
        }

        for (int part = 0; part < names.Count; part++)
        {
            if (string.Equals(names[part], group, StringComparison.OrdinalIgnoreCase))
            {
                return part;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>SetBodygroup</c>, <c>shared/animation.cpp:863</c>. The arithmetic itself lives in
    /// <see cref="StudioModelInfo"/>, once, because a second copy of the subtraction corrupts a
    /// DIFFERENT part's digit and so shows up on a piece the mistaken line never mentions.
    /// </remarks>
    public int SetBodygroup(string modelPath, int group, int value, int body)
    {
        ArgumentNullException.ThrowIfNull(modelPath);

        if (group < 0
            || !_frames.TryGetValue(modelPath, out PropModels.ModelFrames? model)
            || model.BodyParts is not { Count: > 0 } parts
            || group >= parts.Count)
        {
            return body;
        }

        (int place, int count) = parts[group];

        return StudioModelInfo.WithBodygroup(body, place, count, value);
    }

    /// <summary>Which material paints which skinref, for one skin family.</summary>
    /// <param name="modelPath">The model's path.</param>
    /// <param name="skin">Which family; out of range falls back to zero, as the engine does.</param>
    /// <returns>The family's row, keyed by skinref, or null for a model with no table.</returns>
    /// <remarks>
    /// **Indexed by family, and keyed by SKINREF** (B229). Both were off by one design: the list
    /// held families 1..N so this subtracted one, and each entry was keyed on family zero's
    /// RESOLVED material index — which made family zero load-bearing for every other family and
    /// left a model whose family-zero texture is not shipped undrawable at every skin.
    ///
    /// A mesh's <c>mstudiomesh_t::material</c> is a skinref and
    /// <c>g_skinref[skin][skinref]</c> turns it into a texture index
    /// (<c>utils/motionmapper/motionmapper.h:134</c>), so the row is the whole answer and family
    /// zero is a row like any other. Returned even for skin zero, because the caller then has no
    /// special case to get wrong.
    /// </remarks>
    public IReadOnlyDictionary<int, int>? SkinSwap(string modelPath, int skin)
    {
        if (!_swaps.TryGetValue(modelPath, out IReadOnlyList<IReadOnlyDictionary<int, int>>? swaps)
            || swaps.Count == 0)
        {
            return null;
        }

        // `props_shared.cpp:1079` — a skin the model does not have falls back to family zero rather
        // than being refused. A demo names an entity's `m_nSkin` and this project does not control
        // what it says.
        return swaps[skin >= 0 && skin < swaps.Count ? skin : 0];
    }

    /// <summary>Which baked frame a prop's sequence and cycle select.</summary>
    /// <param name="prop">The prop, carrying the sequence and cycle the demo networked.</param>
    /// <param name="seconds">Demo time, for advancing the cycle the server does not send.</param>
    /// <returns>A frame index for <see cref="Batches(string, int)"/>.</returns>
    public int FrameFor(SceneProp prop, double seconds) => SelectFor(prop, seconds).Frame;

    /// <summary>Which baked frames a prop falls between, and how far.</summary>
    /// <param name="prop">The prop, carrying the sequence and cycle the demo networked.</param>
    /// <param name="seconds">Demo time, for advancing the cycle the server does not send.</param>
    /// <returns>The frame to draw, the one after it, and the blend between them.</returns>
    public (int Frame, int Next, float Blend) SelectFor(SceneProp prop, double seconds)
    {
        ArgumentNullException.ThrowIfNull(prop);

        return SelectWith(_frames.GetValueOrDefault(prop.ModelPath), prop, seconds);
    }

    /// <summary><see cref="SelectFor"/> with the model already resolved.</summary>
    private static (int Frame, int Next, float Blend) SelectWith(PropModels.ModelFrames? frames, SceneProp prop, double seconds)
    {
        return
        frames is not null
            ? frames.Select(
                prop.Pose.Sequence,
                prop.Pose.Cycle,
                seconds,
                prop.Pose.PlaybackRate,

                // **The stamp, which this call was not passing** (B237). The timeline records when
                // each animation began — `AnimationStartSeconds`, from the parity counter and the
                // client-side frame reset — and the SKINNED path in `Simulate` has used it since it
                // was added. This one, which every baked prop takes, did not, so a cabinet's `open`
                // was measured from the start of the recording and clamped to its final frame
                // before it drew once.
                prop.Pose.AnimationStartSeconds)
            : (0, 0, 0f);
    }

    /// <summary>Whether a model kind has geometry this renderer can draw.</summary>
    /// <param name="kind">What the model reference resolved to.</param>
    /// <returns>Whether it can be packed and drawn.</returns>
    /// <remarks>
    /// **One predicate, because it was two tests that had to agree and nothing made them.** The
    /// packing loop and the draw loop each carried their own <c>Kind != Studio</c>, so admitting
    /// brush entities meant changing the same rule in two places — and the two failures are not
    /// alike: a model packed but never drawn is silent, while one drawn but never packed is a
    /// lookup miss reported as a load failure.
    ///
    /// A sprite is a camera-facing quad with no geometry of its own and is still not drawn.
    /// Unknown is <c>mod_bad</c>: the reference never resolved, so there is nothing to look up.
    /// </remarks>
    private static bool IsDrawable(SceneModelKind kind) =>
        kind is SceneModelKind.Studio or SceneModelKind.Brush;

    /// <summary>Whether this prop names something that can be drawn at all.</summary>
    /// <param name="prop">The prop.</param>
    /// <returns>Whether it has a drawable kind AND a model path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prop"/> is null.</exception>
    /// <remarks>
    /// **The engine's precondition, not a policy of ours**: an entity whose <c>GetModel()</c> is
    /// null is never added to a renderable list, so nothing downstream of visibility — bones,
    /// lighting, the draw — ever sees it. A drawable KIND with an empty PATH is exactly that case:
    /// a cosmetic whose model the wire never carried and the item schema has not supplied.
    ///
    /// **Extracted so the probes ask the same question production does** (PARITY-AUDIT finding 5).
    /// `DrawnPropProbe` labelled a prop DRAWN off the visibility rules alone, and an empty-path
    /// `CTFWearableRobotArm` therefore reported as drawn while the renderer produced nothing —
    /// the probe reporting rather than the renderer failing. A probe that reimplements the rule it
    /// is checking agrees with whoever wrote the probe, so this is the one definition and both
    /// sides read it.
    /// </remarks>
    public static bool CanDraw(SceneProp prop)
    {
        ArgumentNullException.ThrowIfNull(prop);

        return IsDrawable(prop.Kind) && prop.ModelPath.Length > 0;
    }
}
