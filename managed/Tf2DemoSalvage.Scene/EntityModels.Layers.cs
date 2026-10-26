using System;
using System.Collections.Generic;
using System.Numerics;
using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>What an entity's event walk remembers, and the parity it last saw.</summary>
    private readonly Dictionary<int, (AnimationEventState State, int Parity)> _eventStates = [];

    /// <summary>Scratch for one entity's events, so the walk allocates nothing per frame.</summary>
    private readonly List<StudioEvent> _firedScratch = [];

    /// <summary>Every client animation event that fired this frame, with where it fired.</summary>
    private readonly List<FiredAnimationEvent> _fired = [];

    /// <summary>
    /// The entities whose events this frame has walked: `C_BaseAnimating::Simulate` calls `DoAnimationEvents` once per
    /// entity per frame, and this frame's passes can each hold one entity — walked again under a different sequence, the
    /// walk restarts and fires every event up to the cycle once more (B172).
    /// </summary>
    private readonly HashSet<int> _walked = [];

    /// <summary>Each player's cycle, and the demo time it was advanced to — `m_flCycle` as the client keeps it.</summary>
    private readonly Dictionary<int, (double Seconds, double Cycle)> _clientCycles = [];

    /// <summary>The longest step <see cref="Advance"/> integrates; past it, a seek, the cycle is recomputed.</summary>
    private const double LongestCycleStep = 1d;

    /// <summary>`C_BaseAnimating::FrameAdvance`: a client-animated cycle, advanced by this frame's rate.</summary>
    /// <remarks>
    /// **Integrated for a player, because a player's rate moves** (B172). `addcycle = flInterval * cyclerate *
    /// m_flPlaybackRate` each frame (`c_baseanimating.cpp:5493`), and a run's `Studio_CPS` follows the pose parameters.
    /// `start + elapsed × rate now` jumps the phase whenever the rate changes, which crossed footstep cycles that the
    /// engine's feet never reach. Everything else keeps the closed form: a viewmodel restarts from its own
    /// <c>AnimationStartSeconds</c>, and a prop's rate is constant. A seek, or the first frame, starts from the closed form.
    /// </remarks>
    private double Advance(SceneProp prop, double elapsed, double seconds, double rate)
    {
        double closed = prop.Pose.Cycle + (elapsed * rate);

        if (prop.Pose.Speed is null)
        {
            return closed;
        }

        if (_restartCycles.Remove(prop.EntityIndex))
        {
            _clientCycles[prop.EntityIndex] = (seconds, 0d);
            return 0d;
        }

        if (!_clientCycles.TryGetValue(prop.EntityIndex, out (double Seconds, double Cycle) last) ||
            Math.Abs(seconds - last.Seconds) > LongestCycleStep)
        {
            _clientCycles[prop.EntityIndex] = (seconds, closed);
            return closed;
        }

        // A frame a hair behind the last holds the cycle: the engine's never runs backwards, and restarting from the closed
        // form here jumped the phase across a footstep and back, over and over within one tick.
        if (seconds <= last.Seconds)
        {
            return last.Cycle;
        }

        double cycle = last.Cycle + ((seconds - last.Seconds) * rate);

        _clientCycles[prop.EntityIndex] = (seconds, cycle);
        return cycle;
    }

    /// <summary>The bits of the demo time <see cref="_fired"/> belongs to; NaN's before the first frame.</summary>
    private long _firedFrame = BitConverter.DoubleToInt64Bits(double.NaN);

    /// <summary>Moves every client animation event crossed since the last take into <paramref name="into"/>.</summary>
    /// <param name="into">Receives the events, in the order the engine fires them.</param>
    /// <remarks>
    /// **Taken, not read: `DoAnimationEvents` fires an event ONCE**, on the frame its cycle is crossed
    /// (<c>c_baseanimating.cpp:3550</c>). This was a property read every frame, and the list was only cleared when the
    /// demo time moved, so a paused demo handed the sound pass the same footsteps on every frame — 27,882 starts at one
    /// tick in a UI session's log. Order is the engine's, which on a loop is the old lap's tail before the new head.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="into"/> is null.</exception>
    public void TakeFiredEvents(ICollection<FiredAnimationEvent> into)
    {
        ArgumentNullException.ThrowIfNull(into);

        foreach (FiredAnimationEvent fired in _fired)
        {
            into.Add(fired);
        }

        _fired.Clear();
    }

    /// <summary>Resolves an entity's gestures into layers this model can actually play.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <returns>The layers, in slot order, or empty.</returns>
    /// <remarks>
    /// **This is <c>AddToGestureSlot</c> and <c>UpdateGestureLayer</c> together**
    /// (<c>multiplayer_animstate.cpp:616</c> and <c>:1275</c>), which is where the engine turns an
    /// activity into a sequence and advances the layer's cycle:
    ///
    /// <code>
    ///   int iGestureSequence = pPlayer-&gt;SelectWeightedSequence( iGestureActivity );
    ///   if ( iGestureSequence &lt;= 0 ) return;                       // abandoned, not drawn
    ///   ...
    ///   flCycle += GetSequenceCycleRate( … ) * gpGlobals-&gt;frametime * …;
    ///   if ( flCycle &gt; 1.0f ) { if ( bAutoKill ) ResetGestureSlot(…); else flCycle = 1.0f; }
    /// </code>
    ///
    /// **Resolved here rather than in the timeline because only this layer has the model**, and a
    /// gesture that resolves to nothing is dropped exactly as the engine drops it — a class whose
    /// model has no such activity plays no gesture rather than a wrong one.
    ///
    /// **The weight is one, always**, because <c>AddToGestureSlot</c> sets
    /// <c>m_pAnimLayer-&gt;m_flWeight = 1.0f</c> and nothing on a networked gesture lowers it:
    /// <c>BlendWeight</c> returns immediately unless <c>m_bClientBlend</c>
    /// (<c>animationlayer.h:183</c>), which is client-only state a demo never carries. The per-bone
    /// list does all the shaping.
    ///
    /// **Not reproduced:** <c>GetGesturePlaybackRate()</c>, which scales the advance for a taunt,
    /// and the layer's own <c>m_flPlaybackRate</c>, which the engine keeps per layer and a gesture
    /// leaves at one. Named rather than silently omitted.
    /// </remarks>
    /// <remarks>
    /// **An instance method because it COUNTS its own substitutions** (B350). It was static, and a
    /// counter kept anywhere else would measure this method's intent rather than its work — the
    /// fault B347 shipped and a sabotage caught.
    /// </remarks>
    private List<PoseLayer> LayersFor(
        SceneProp prop, PropModels.SkinnedModel skinned, double seconds)
    {
        List<PoseLayer> layers = [];

        // **The layers the entity itself sends, walked in `m_nOrder`** (B285). A player has none —
        // `tf_player.cpp:774` excludes the array — so this loop is empty for them and the gesture
        // loop below is empty for everything else. `AccumulateLayers` draws both the same way.
        //
        // **No activity resolution and no lifetime**, unlike a gesture: the server chose the
        // sequence, states the cycle and states the weight, so every question this layer needs is
        // already answered on the wire.
        foreach (SceneAnimationLayer sent in prop.Pose.Layers)
        {
            // `if (m_AnimOverlay[i].m_nSequence >= nSequences) continue;` — a sequence the model
            // does not have is skipped rather than clamped (`c_baseanimatingoverlay.cpp:341`).
            if (sent.Sequence < 0 || sent.Sequence >= skinned.Sequences.Count || sent.Weight <= 0f)
            {
                continue;
            }

            // `fCycle = ClampCycle( fCycle, IsSequenceLooping( … ) )`, the same wrap the main
            // sequence takes — and the same `STUDIO_REALTIME` override before it (B309).
            //
            // **The override belongs to every sequence, not to the main one.** Valve applies it
            // inside `CalcPoseSingle` (`bone_setup.cpp:1955`), which `AccumulatePose` runs for the
            // main sequence, for each layer and for each autolayer alike. Putting it only on the
            // main sequence would be half a mechanism — a realtime sequence would take the clock
            // when played and the wire's cycle when layered.
            float wrapped = skinned.Realtime(sent.Sequence)
                ? Fraction((float)(seconds * skinned.CyclesPerSecond(sent.Sequence)))
                : StudioSequences.ClampCycle(sent.Cycle, skinned.Loops(sent.Sequence));

            (int at, float part) = StudioSequences.FrameAt(
                wrapped, skinned.Frames(sent.Sequence), skinned.Loops(sent.Sequence));

            layers.Add(new PoseLayer(
                sent.Sequence,
                at,
                part,
                sent.Weight,
                skinned.BoneWeights(sent.Sequence),
                Delta: skinned.IsDelta(sent.Sequence),
                Post: skinned.IsPost(sent.Sequence),
                Locks: skinned.LocksOf(sent.Sequence)));
        }

        // Stryker disable once : a mutant that empties the guard body leaves 'gestures'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (prop.Pose.Gestures is not { Count: > 0 } gestures)
        {
            return layers;
        }

        // **The attack-and-reload slot's holder, decided before it is drawn** (B437): a voice command waiting behind it
        // takes the slot only if the holder is no longer active when the command arrives. Drawn when the walk moves past
        // the slot, so the layer keeps its place in slot order.
        (SceneGesture Gesture, int Sequence)? holder = null;

        foreach (SceneGesture gesture in gestures)
        {
            if (gesture.Slot == GestureSlot.AttackAndReload)
            {
                int resolved = gesture.ActivityName is { Length: > 0 } asked
                    ? GestureSequence(prop, skinned, TranslateGesture(prop, skinned, asked), gesture)
                    : 0;

                if (!gesture.OnlyIfSlotIdle ||
                    holder is not { } held ||
                    !ActiveAt(skinned, held.Sequence, held.Gesture, gesture.StartedSeconds))
                {
                    holder = (gesture, resolved);
                }

                continue;
            }

            if (holder is { } playing)
            {
                Layer(layers, skinned, playing.Sequence, playing.Gesture, seconds);
                holder = null;
            }

            // **A VCD gesture names a SEQUENCE, not an activity**, and so takes neither the weapon
            // rewrite nor the activity search below (B351). `C_TFPlayer::StartGestureSceneEvent` does
            // `info->m_nSequence = LookupSequence( event->GetParameters() )`
            // (`c_tf_player.cpp:9456`) — `LookupSequence` matches a sequence LABEL, which is what
            // `Find` does, where `SelectWeightedSequence` matches an activity. Putting a taunt
            // through the activity path resolves nothing, because no sequence is labelled with an
            // activity name.
            if (gesture.Taunt is { } taunt)
            {
                // **The scene's own clock, folded by its LOOP, decides which gesture is playing and
                // how far into it we are.** `DispatchProcessLoop` sets the scene's time back to the
                // loop's parameter (`c_sceneentity.cpp:594`), so a press-and-hold taunt crosses its
                // gesture event again and `StartGestureSceneEvent` restarts the layer — which in
                // closed form is folding the elapsed time into the loop's window.
                float scene = taunt.TimeAt((float)(seconds - gesture.StartedSeconds));

                if (taunt.At(scene) is { } staged)
                {
                    Layer(
                        layers,
                        skinned,
                        skinned.Find(staged.Gesture.Sequence),
                        gesture with { StartedSeconds = seconds - staged.Elapsed },
                        seconds);
                }

                continue;
            }

            // `SelectWeightedSequence( iGestureActivity )`, and its `<= 0` abandonment. An activity
            // number rather than a name is the two custom-gesture events, which carry the activity
            // on the wire; nothing resolves those yet, so they are skipped rather than guessed at.
            // Stryker disable once : a mutant that empties the guard body leaves 'named'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (gesture.ActivityName is not { Length: > 0 } named)
            {
                continue;
            }

            // **The weapon rewrites the activity, and it is a TABLE rather than a suffix** (B284).
            // `CTFPlayerAnimState::TranslateActivity` (`tf_playeranimstate.cpp:124`) calls
            // `pWeapon->ActivityOverride( … )`, which walks that weapon role's own `acttable_t` —
            // twelve of them in `tf_weaponbase.cpp:3660` onward. A model declares only the
            // rewritten names, so an activity that is not put through this resolves to −1 and draws
            // nothing.
            //
            // **Here rather than in Core, because only this layer knows the role.** It comes from
            // the installed game's own scripts through `Appearance.WeaponSuffix`, the same source
            // the main sequence uses.
            //
            // The first attempt at this appended the role as a string, which is right for reloads
            // and landings and wrong for every attack: `ACT_MP_ATTACK_STAND_PRIMARYFIRE` maps to
            // `ACT_MP_ATTACK_STAND_PRIMARY`, a rename rather than a suffix, and
            // `ACT_MP_CROUCH_DEPLOYED` maps to `ACT_MP_CROUCHWALK_DEPLOYED`.
            //
            // **The player's own table comes first** (B437): `TranslateActivity` asks `ActivityOverride` before the
            // weapon, so a humiliated loser's landing is `ACT_MP_JUMP_LAND_LOSERSTATE` and a carrying engineer's
            // voice gestures are the `_BUILDING` ones.
            string activity = TranslateGesture(prop, skinned, named);

            // **The activity's weighted roll (`GestureSequence`), not `Find`, and the difference is the whole mechanism.** `Find`
            // matches a sequence LABEL the way `Studio_LookupSequence` does; the engine resolves a
            // gesture through `SelectWeightedSequence( iGestureActivity )`, which matches the
            // ACTIVITY and picks among ties by `actweight`. A gesture names an activity —
            // `ACT_MP_GESTURE_FLINCH_CHEST` — and no sequence is labelled that, so asking `Find`
            // returns −1 for every gesture on every model. Measured: a flinch half a second old,
            // three gestures reaching the drawn prop, and zero layers.
            int sequence = GestureSequence(prop, skinned, activity, gesture);

            // **A FLINCH is the one gesture the engine substitutes rather than abandoning** (B350),
            // and it does so before `AddToGestureSlot` is ever reached
            // (`multiplayer_animstate.cpp:376`):
            //
            //     if ( iActivity != ACT_MP_GESTURE_FLINCH_CHEST &&
            //          GetBasePlayer()->SelectWeightedSequence( iActivity ) == -1 )
            //         RestartGesture( GESTURE_SLOT_FLINCH, ACT_MP_GESTURE_FLINCH_CHEST );
            //
            // **Measured, and it is most of them.** TF2's class models declare only
            // `ACT_MP_GESTURE_FLINCH_CHEST` in the merged table — no HEAD, LEFTARM, RIGHTARM,
            // LEFTLEG or RIGHTLEG. `tf2-2026-pub-pov-cheater` fires 55 chest flinches and 69
            // non-chest ones, every one of which this dropped.
            //
            // **Resolved here rather than at event time, which is where the engine decides.** The
            // substitution needs the model, and only this layer has it; the layer produced is the
            // same either way because nothing between the two moments can change which sequences a
            // model declares.
            //
            // **`== -1`, not "no answer"** (B460): a flinch found at sequence ZERO is not substituted — the engine restarts
            // it as asked and `AddToGestureSlot`'s `<= 0` below abandons it.
            if (sequence == -1 &&
                gesture.Slot == GestureSlot.Flinch &&
                !string.Equals(activity, FlinchChestActivity, StringComparison.Ordinal))
            {
                sequence = GestureSequence(prop, skinned, FlinchChestActivity, gesture);

                if (sequence > 0)
                {
                    SubstitutedFlinches++;
                }
            }

            // `if ( iGestureSequence <= 0 ) return;` — the engine abandons a gesture whose activity
            // this model does not have, rather than substituting one. That still holds for every
            // other slot, and for a flinch on a model carrying no flinch at all: the substitution
            // above reaches this same check with the chest activity and is abandoned on it.
            Layer(layers, skinned, sequence, gesture, seconds);
        }

        if (holder is { } last)
        {
            Layer(layers, skinned, last.Sequence, last.Gesture, seconds);
        }

        return layers;
    }

    /// <summary>`AddToGestureSlot`'s `SelectWeightedSequence( iGestureActivity )`: one roll, when the gesture starts (D233).</summary>
    /// <returns>A merged sequence number, or −1 when no sequence answers to the activity.</returns>
    /// <remarks>
    /// The engine rolls once at `multiplayer_animstate.cpp:633` and stores the answer in the layer, so the roll is seeded
    /// on what the gesture carries — the player, the activity, the moment it started — and every frame of the gesture,
    /// and a seek into it, lands on the same variant with nothing held. A `RestartGesture` of an activity the slot is
    /// already playing only resets the cycle (`:581-611`) and keeps the variant; ours re-rolls on the restart's new
    /// start (RISKS B584).
    /// </remarks>
    private static int GestureSequence(SceneProp prop, PropModels.SkinnedModel skinned, string activity, SceneGesture gesture) =>
        skinned.SelectWeightedSequence(
            activity, -1, ActivityPickSeed(prop.EntityIndex, activity, BitConverter.DoubleToInt64Bits(gesture.StartedSeconds)));

    /// <summary>`TranslateActivity` for a gesture of this player: their own table, the weapon's, the item's, the winner's (B437).</summary>
    private static string TranslateGesture(SceneProp prop, PropModels.SkinnedModel skinned, string activity) =>
        PlayerActivityTable.Translate(
            activity,
            prop.Pose.Slot ?? "PRIMARY",
            prop.Pose.ActivityOverride,
            prop.Pose.CompetitiveWinnerClass,
            prop.Pose.ItemActivities,
            name => skinned.ForActivity(name) >= 0);

    /// <summary>`IsGestureSlotActive`: whether a gesture still holds its slot at a moment (B437).</summary>
    /// <remarks>
    /// `m_bActive` is set when `AddToGestureSlot` finds a sequence and cleared only when an auto-kill gesture's cycle
    /// passes one (`UpdateGestureLayer`'s `ResetGestureSlot`); one that does not auto-kill holds until replaced. A
    /// gesture whose activity resolved to nothing never became active.
    /// </remarks>
    private static bool ActiveAt(PropModels.SkinnedModel skinned, int sequence, SceneGesture gesture, double seconds)
    {
        if (sequence <= 0)
        {
            return false;
        }

        float rate = skinned.CyclesPerSecond(sequence);

        return !gesture.AutoKill || (rate > 0f ? (seconds - gesture.StartedSeconds) * rate : 0d) <= 1d;
    }

    /// <summary>Turns one resolved gesture sequence into a pose layer, or into nothing.</summary>
    /// <param name="layers">Where the layer is appended, in place.</param>
    /// <param name="skinned">The model the sequence belongs to.</param>
    /// <param name="sequence">What the activity or the label resolved to; zero or below is abandoned.</param>
    /// <param name="gesture">The gesture, for its start and its auto-kill.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <remarks>
    /// **Shared by the activity path and the VCD path because everything after the lookup is
    /// identical.** The engine's own split is the same shape: `AddToGestureSlot` and
    /// `AddVCDSequenceToGestureSlot` differ only in how they arrive at `m_nSequence`, and then set
    /// the same `m_flWeight = 1.0f`, `m_flPlaybackRate = 1.0f` and `m_nOrder = iGestureSlot`
    /// (<c>multiplayer_animstate.cpp:718</c>). Two copies of the cycle arithmetic would be two
    /// chances for a taunt and a reload to age differently.
    /// </remarks>
    private static void Layer(
        List<PoseLayer> layers,
        PropModels.SkinnedModel skinned,
        int sequence,
        SceneGesture gesture,
        double seconds)
    {
        // `if ( iGestureSequence <= 0 ) return;` — the engine abandons a gesture the model does not
        // have rather than substituting one.
        if (sequence <= 0)
        {
            return;
        }

        float rate = skinned.CyclesPerSecond(sequence);

        // A sequence with no rate has one frame and nothing to advance through; the engine's
        // cycle would stay at zero, which is that single frame.
        double cycle = rate > 0f ? (seconds - gesture.StartedSeconds) * rate : 0d;

        if (cycle < 0d)
        {
            return;
        }

        if (cycle > 1d)
        {
            // `if ( pGesture->m_bAutoKill ) ResetGestureSlot(…)` — the slot is gone, so the
            // layer is not drawn at all. Otherwise it holds on its last frame, for ever, which
            // is what a `_BEGIN` gesture is for.
            if (gesture.AutoKill)
            {
                return;
            }

            cycle = 1d;
        }

        (int frame, float fraction) = StudioSequences.FrameAt(
            (float)cycle, skinned.Frames(sequence), skinned.Loops(sequence));

        layers.Add(new PoseLayer(
            sequence,
            frame,
            fraction,
            1f,
            skinned.BoneWeights(sequence),

            // **Every TF2 player gesture is a DELTA**, measured: `PRIMARY_reload_start` and
            // `jumpland_primary` both carry the bit. `SlerpBones` composes those additively
            // rather than blending toward them (B284).
            Delta: skinned.IsDelta(sequence),
            Post: skinned.IsPost(sequence),
            Locks: skinned.LocksOf(sequence)));
    }

    /// <summary>How deep an autolayer chain may go before it is abandoned.</summary>
    /// <remarks>
    /// **A bound on data from a file, not a guess at content.** Valve reaches a layered sequence's
    /// own layers through `AccumulatePose`, which has no depth limit at all and would not terminate
    /// on a model whose layers cycle. Four is far past anything measured — the deepest real case is
    /// one level — and a chain longer than that is a corrupt model rather than an animator's intent.
    /// </remarks>
    private const int LayerDepth = 4;

    /// <summary>Where each of a sequence's IK rules wants its chain, at this cycle.</summary>
    /// <param name="skinned">The model.</param>
    /// <param name="sequence">The merged sequence being played.</param>
    /// <param name="cycle">Where its cycle stands, wrapped.</param>
    /// <param name="influenceOfSequence">
    /// How much this sequence itself counts — one for the main sequence, a layer's own weight for a
    /// layer. `AddDependencies` takes exactly this as `flWeight`.
    /// </param>
    /// <param name="values">The pose parameters in force, which choose the blend corners.</param>
    /// <param name="errors">Where the rules that asked for something are added, in place.</param>
    /// <remarks>
    /// **<c>Studio_IKSequenceError</c> then <c>Studio_IKAnimationError</c>**
    /// (<c>bone_setup.cpp:3043</c> and <c>:2994</c>), which between them do two accumulations across
    /// the same up-to-four animations the sequence blends.
    ///
    /// **The ENVELOPE is accumulated first, weighted**, because the four animations may not agree
    /// about when the rule runs:
    ///
    /// <code>
    ///   ikRule.start += (pRule->start + dt) * weight[i];   // and peak, tail, end
    ///   if (ikRule.start > 1.0) { ikRule.start -= 1.0; … }
    /// </code>
    ///
    /// **The <c>dt</c> is the part that would be missed.** When one animation's rule starts near
    /// zero and another's near one, they are the same footstep either side of a loop — so Valve
    /// shifts a rule more than half a cycle from the first by a whole cycle before averaging, which
    /// is what stops the mean landing in the middle of the animation instead of at its edge.
    ///
    /// **Then the ERROR is accumulated, weighted, over the same animations**, each read at the
    /// frame the shared envelope picks.
    ///
    /// **Only the rules of the FIRST animation are enumerated**, which is Valve's own constraint:
    /// `if (iRule >= panim[i]->numikrules || panim[i]->numikrules != panim[0]->numikrules) return
    /// false;`. Blended animations are expected to declare matching rule lists, and one that does
    /// not is abandoned rather than reconciled.
    /// </remarks>

    internal static void IkFor(
        PropModels.SkinnedModel skinned,
        int sequence,
        float cycle,
        float influenceOfSequence,
        IReadOnlyList<float> values,
        List<(StudioIkRule, Vector3, Quaternion, float)> errors)
    {
        if (skinned.IkChains.Count == 0 || influenceOfSequence <= 0f)
        {
            return;
        }

        (int group, IReadOnlyList<(int Animation, float Weight)> blend) =
            skinned.BlendedAnimations(sequence, values);

        if (blend.Count == 0 || group >= skinned.Models.Count)
        {
            return;
        }

        ReadOnlyMemory<byte> model = skinned.Models[group];

        IReadOnlyList<StudioIkRule> rules = skinned.IkRules(group, blend[0].Animation);

        if (rules.Count == 0)
        {
            return;
        }

        for (int rule = 0; rule < rules.Count; rule++)
        {
            // The envelope, averaged across the corners, with Valve's loop-shift.
            float start = 0f;
            float peak = 0f;
            float tail = 0f;
            float end = 0f;
            float first = float.NaN;
            bool matched = true;

            for (int corner = 0; corner < blend.Count; corner++)
            {
                (int animation, float weight) = blend[corner];

                IReadOnlyList<StudioIkRule> theirs = skinned.IkRules(group, animation);

                if (rule >= theirs.Count || theirs.Count != rules.Count)
                {
                    matched = false;
                    break;
                }

                StudioIkRule other = theirs[rule];

                float shift = 0f;

                if (!float.IsNaN(first))
                {
                    if (other.Start - first > 0.5f)
                    {
                        shift = -1f;
                    }
                    else if (other.Start - first < -0.5f)
                    {
                        shift = 1f;
                    }
                }
                else
                {
                    first = other.Start;
                }

                start += (other.Start + shift) * weight;
                peak += (other.Peak + shift) * weight;
                tail += (other.Tail + shift) * weight;
                end += (other.End + shift) * weight;
            }

            if (!matched)
            {
                continue;
            }

            if (start > 1f)
            {
                start -= 1f;
                peak -= 1f;
                tail -= 1f;
                end -= 1f;
            }
            else if (start < 0f)
            {
                start += 1f;
                peak += 1f;
                tail += 1f;
                end += 1f;
            }

            StudioIkRule shared = rules[rule] with
            {
                Start = start, Peak = peak, Tail = tail, End = end,
            };

            bool carriesAnError =
                shared.Type is StudioIkRuleType.Self or StudioIkRuleType.World
                    or StudioIkRuleType.Ground or StudioIkRuleType.Attachment;

            // The error, accumulated over the same corners at the frame the shared envelope picks.
            Vector3 position = default;
            Quaternion rotation = default;
            float total = 0f;

            // The ENVELOPE weight, which every corner of one grid computes from the same shared
            // envelope and the same cycle — so it is one value, not a sum, exactly as Valve takes
            // it once before the corner loop.
            float envelope = 0f;

            for (int blended = 0; blended < blend.Count; blended++)
            {
                (int animation, float weight) = blend[blended];

                float influence = StudioIkRules.Weight(
                    shared,
                    skinned.FramesOfAnimation(group, animation),
                    cycle,
                    out int frame,
                    out float fraction);

                // `if (pRule->type != IK_GROUND && flWeight < 0.0001) return false;` — a rule with
                // no influence is skipped, except a ground rule, which has to keep tracking where
                // its foot was planted. TF2 declares no ground rules at all.
                if (influence < 0.0001f)
                {
                    continue;
                }

                // **Only four of the six types carry an error track, and Valve says so in a
                // comment** — `// only check rules with error values`, over the switch at
                // `bone_setup.cpp:3157`. `IK_SELF`, `IK_WORLD`, `IK_GROUND` and `IK_ATTACHMENT`
                // read one; `IK_RELEASE` and `IK_UNLATCH` fall to `default: total += weight[i];`,
                // which counts the corner's weight WITHOUT reading anything.
                //
                // **Requiring an error dropped every release**, and a release is the type TF2
                // declares most: 13359 against 1674 selves over every animation `z1800` draws. So
                // the corrections a release exists to give back were never given back, and every
                // self ran at full strength (B299).
                if (carriesAnError)
                {
                    if (!StudioIkRules.Error(
                        model,
                        animation,
                        rule,
                        frame,
                        fraction,
                        out (float X, float Y, float Z) at,
                        out (float X, float Y, float Z, float W) turn))
                    {
                        continue;
                    }

                    position += new Vector3(at.X, at.Y, at.Z) * weight;

                    // `QuaternionAccumulate( ikRule.q, weight[i], q1, ikRule.q )` — an aligned add
                    // rather than a slerp, which is what lets several corners sum.
                    Quaternion corner = new(turn.X, turn.Y, turn.Z, turn.W);

                    rotation += corner * (Quaternion.Dot(rotation, corner) < 0f ? -weight : weight);
                }

                // **`total += weight[i]`, the corner's blend weight ALONE** — `default:` and the
                // success arm of the switch both add exactly that (`bone_setup.cpp:3175`). It was
                // `weight * influence` here, which conflated the two quantities below.
                total += weight;

                envelope = influence;
            }

            // **`total` is a NORMALISER, and `flWeight` is the envelope. They are not the same
            // number and using one as the other is a divergence that hides in the common case.**
            // `Studio_IKSequenceError` computes the envelope once, up front, and returns false when
            // it is nothing:
            //
            //     ikRule.flWeight = Studio_IKRuleWeight( ikRule, flCycle );
            //     if (ikRule.flWeight <= 0.001f) { ...IK_GROUND looping special case... return false; }
            //
            // then uses `total` for one thing only, at `:3188`:
            //
            //     if (total <= 0.0001f) return false;
            //     if (total < 0.999f)
            //     {
            //         VectorScale( ikRule.pos, 1.0f / total, ikRule.pos );
            //         QuaternionScale( ikRule.q, 1.0f / total, ikRule.q );
            //     }
            //
            // and the weight the solver applies is `pRule->flWeight * pRule->flRuleWeight` — the
            // envelope times the sequence's influence, with `total` nowhere in it.
            //
            // **The two agree whenever every corner reports**, because the corner weights sum to
            // one; they part company when one corner's error read fails. Valve keeps the full
            // envelope weight and rescales the error by the share that DID report. Ours reduced the
            // weight instead, which asks for less correction rather than for the same correction
            // derived from less data.
            if (total <= 0.0001f || envelope <= 0.001f ||
                (carriesAnError && rotation.LengthSquared() <= 0f))
            {
                continue;
            }

            if (total < 0.999f)
            {
                position /= total;

                // **`QuaternionScale`, which scales the ANGLE rather than the components**
                // (`mathlib_base.cpp:1757`, `sinsom = sin( asin( sinom ) * t )`, carrying the sign
                // of `w` across under Valve's own note *"keep sign of rotation"*). A component
                // divide would be a different rotation, and `Quaternion.Normalize` — which this
                // used unconditionally — is a third thing again.
                (float qx, float qy, float qz, float qw) = StudioBones.Scale(
                    (rotation.X, rotation.Y, rotation.Z, rotation.W), 1f / total);

                rotation = new Quaternion(qx, qy, qz, qw);
            }

            // **Scaled by the SEQUENCE's own influence**, which is what `AddDependencies` receives
            // as `flWeight` — an autolayer at half weight asks for half the correction, exactly as
            // its pose contributes half.
            // `flWeight * flRuleWeight` — the envelope times the sequence's influence, which is
            // what `SolveDependencies` multiplies (`bone_setup.cpp:4103`). `total` is not in it.
            float asked = Math.Clamp(envelope * influenceOfSequence, 0f, 1f);

            // **A rule at full strength clears every rule already on its chain, and a RELEASE at
            // full strength is then not added at all** — `AddDependencies`, `bone_setup.cpp:3319`:
            //
            //     if (ikrule.flRuleWeight * ikrule.flWeight > 0.999)
            //     {
            //         if ( ikrule.type != IK_UNLATCH)
            //         {
            //             m_ikChainRule.Element( ikrule.chain ).RemoveAll( );
            //             if ( ikrule.type == IK_RELEASE) continue;
            //         }
            //     }
            //
            // So a chain whose release is fully in force reaches the solver EMPTY rather than
            // carrying a correction and its cancellation — which is both faster and, for the
            // rotation, not the same answer as blending the two.
            if (asked > 0.999f && shared.Type != StudioIkRuleType.Unlatch)
            {
                // In place, as the engine's `RemoveAll` on its own vector: a lambda capturing `shared` made a closure at
                // the top of every rule's iteration, the largest allocation on f12 (finding 86).
                int kept = 0;

                for (int held = 0; held < errors.Count; held++)
                {
                    if (errors[held].Item1.Chain != shared.Chain)
                    {
                        errors[kept++] = errors[held];
                    }
                }

                errors.RemoveRange(kept, errors.Count - kept);
                if (shared.Type == StudioIkRuleType.Release)
                {
                    continue;
                }
            }

            errors.Add((
                shared,
                position,
                carriesAnError ? Quaternion.Normalize(rotation) : Quaternion.Identity,
                asked));
        }
    }
}
