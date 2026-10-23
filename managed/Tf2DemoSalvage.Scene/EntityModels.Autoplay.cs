using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>A layer's cycle, recovered from the frame it was resolved to.</summary>
    /// <remarks>
    /// **A `PoseLayer` carries a FRAME, and an IK rule's envelope is in CYCLE.** The layer list is
    /// built for bone sampling, which wants a frame; the rules want the fraction of the animation
    /// that frame sits at. Recomputing it from the frame is exact enough for the envelope, whose
    /// resolution is the same frame count.
    ///
    /// **A one-frame sequence is cycle zero rather than a division by zero**, which is also what
    /// the engine's `numframes - 1` scaling implies for it.
    /// </remarks>
    /// <summary>Valve's <c>cycle = cycle - (int)cycle</c>, for a realtime sequence.</summary>
    /// <param name="cycle">Time times cycles a second.</param>
    /// <returns>Its fractional part.</returns>
    /// <remarks>
    /// **A C cast to <c>int</c> truncates toward zero**, so this is not `Math.Floor` and the two
    /// disagree for a negative input. Demo time never runs backwards past zero, so the difference
    /// cannot arise here — written the engine's way regardless, because the one that matches is
    /// free and the one that nearly matches is a bug waiting for a caller.
    ///
    /// **No test can tell this from <c>ClampCycle(x, true)</c>, and a sabotage said so rather than
    /// a reading.** Swapping one for the other reddened nothing: the two are the same function for
    /// every <c>x >= 0</c>, and every value this branch has ever seen is a product of demo seconds
    /// and a positive cycle rate. The distinguishing input is a NEGATIVE product, which nothing
    /// constructs. Stated here instead of chased, because a test written to force it would be
    /// asserting against an input the program cannot produce.
    /// </remarks>
    private static float Fraction(float cycle) => cycle - (int)cycle;

    private static float FractionOf(PropModels.SkinnedModel skinned, PoseLayer layer)
    {
        int frames = skinned.Frames(layer.Sequence);

        return frames > 1 ? (layer.Frame + layer.FrameFraction) / (frames - 1) : 0f;
    }

    /// <summary>The sequences one sequence automatically layers over itself.</summary>
    /// <param name="skinned">The model.</param>
    /// <param name="sequence">The merged sequence being played.</param>
    /// <param name="cycle">Where its cycle stands, wrapped.</param>
    /// <param name="weight">The weight that sequence is being accumulated at.</param>
    /// <param name="values">The pose parameters in force, for a <c>STUDIO_AL_POSE</c> layer.</param>
    /// <param name="local">Which of Valve's two passes to run.</param>
    /// <param name="budget">How much deeper the recursion may go.</param>
    /// <param name="seconds">
    /// Demo time, for a layer whose sequence carries <c>STUDIO_REALTIME</c> and therefore takes its
    /// cycle from the clock rather than from the parent's (B309).
    /// </param>
    /// <param name="layers">Appended with a layer per autolayer whose envelope is open, in file order.</param>
    /// <remarks>
    /// **Two passes over one array, and every autolayer belongs to exactly one of them.**
    /// `AddSequenceLayers` (<c>bone_setup.cpp:2125</c>) skips a layer carrying `STUDIO_AL_LOCAL`;
    /// `AddLocalLayers` (<c>:2218</c>) skips one without it, and returns immediately unless the
    /// SEQUENCE carries `STUDIO_LOCAL`.
    ///
    /// **They differ in where and when they compose.** The local pass goes into the sequence's own
    /// pose at weight ONE before that pose is blended in; the other goes onto the accumulator
    /// afterwards at the parent's weight. Both are here because both are used by real TF2 content
    /// (B294): `sentry3`'s idle and `c_rocketpack`'s deploy layer non-locally, and
    /// `c_engineer_arms`' `throw_draw`, `throw_idle` and `throw_fire` layer locally.
    ///
    /// **The envelope is skipped entirely when <c>start == end</c>**, which is not a degenerate
    /// case to guard against but the common one: four of the seven autolayers measured have a
    /// window of all zeros, and Valve's `if (pLayer->start != pLayer->end)` then leaves the layer
    /// at the parent's own cycle and weight.
    ///
    /// **Under <c>STUDIO_AL_POSE</c> the window is in the POSE PARAMETER's range, not the cycle's**
    /// — the same four numbers mean something different — and the layer's cycle is NOT rewritten,
    /// where the cycle-driven case remaps it into the window. One flag changing the meaning of five
    /// values is the part most likely to be read past.
    /// </remarks>
    private static void AutoLayersFor(
        PropModels.SkinnedModel skinned,
        int sequence,
        float cycle,
        float weight,
        IReadOnlyList<float> values,
        bool local,
        int budget,
        double seconds,
        List<PoseLayer> layers)
    {
        // `if (!(seqdesc.flags & STUDIO_LOCAL)) return;` — the local pass is gated on the SEQUENCE,
        // so a sequence declaring local autolayers without the flag has layers nothing applies.
        if (budget <= 0 || (local && !skinned.IsLocal(sequence)))
        {
            return;
        }

        foreach (StudioAutoLayer entry in skinned.AutoLayersOf(sequence))
        {
            if (entry.IsLocal != local)
            {
                continue;
            }

            int target = skinned.RelativeSequence(sequence, entry.Sequence);

            if (target < 0)
            {
                continue;
            }

            // `float layerCycle = cycle; float layerWeight = flWeight;` before the window is
            // considered at all — and the local pass is called with a weight of 1.0 by its caller.
            float layerCycle = cycle;
            float layerWeight = weight;

            // **Three exact comparisons, and all three are the engine's** — `if (pLayer->start !=
            // pLayer->end)`, `pLayer->start != pLayer->peak`, `pLayer->end != pLayer->tail`. The
            // analyser wants a tolerance and a tolerance would be wrong: these are two numbers an
            // animator typed, written into the file unchanged, and the test asks whether they are
            // THE SAME NUMBER rather than whether they are close. Four of the seven autolayers
            // measured in TF2 have a window of all zeros, so the first comparison is the common
            // path and not a degenerate guard; the other two divide by the difference immediately
            // afterwards, which is what they exist to prevent.
#pragma warning disable S1244
            if (entry.Start != entry.End)
            {
                // **`STUDIO_AL_POSE` belongs to the non-local pass alone** (B307). They are two
                // separate engine functions and only one of them has the branch:
                // `AddSequenceLayers` (`bone_setup.cpp:2148`) chooses between the cycle and a pose
                // parameter, while `AddLocalLayers` (`bone_setup.cpp:2244`) reads `cycle` and
                // nothing else — no `index`, no `iPose`, no `m_flPoseParameter`.
                //
                // **Not an oversight of Valve's: a local layer's window is in CYCLE units.** The
                // local pass composes into the sequence's own pose before that pose is blended, so
                // the only variable that has walked anywhere by then is the sequence's own cycle.
                float index = entry.DrivenByPose && !local
                    ? PoseIndex(skinned, target, entry, values)
                    : cycle;

                if (index < entry.Start || index >= entry.End)
                {
                    continue;
                }

                float ramp = 1f;

                if (index < entry.Peak && entry.Start != entry.Peak)
                {
                    ramp = (index - entry.Start) / (entry.Peak - entry.Start);
                }
                else if (index > entry.Tail && entry.End != entry.Tail)
                {
                    ramp = (entry.End - index) / (entry.End - entry.Tail);
                }

                if (entry.IsSpline)
                {
                    ramp = (3f * ramp * ramp) - (2f * ramp * ramp * ramp);
                }

                // **The cross-fade arm applies only past the TAIL**, on the way out, and is one at
                // ramp one whatever the parent weighs — which is the point of Valve's comment about
                // a second layer also accumulating.
                if (entry.CrossFades && index > entry.Tail)
                {
                    layerWeight = ramp * weight / (1f - weight + (ramp * weight));
                }
                else if (entry.IgnoresWeight)
                {
                    layerWeight = ramp;
                }
                else
                {
                    layerWeight = weight * ramp;
                }

                // **Not remapped for a pose-driven layer**, whose cycle stays the parent's — and
                // `AddSequenceLayers` is the only pass that says so. `AddLocalLayers` ends with a
                // bare `layerCycle = (cycle - pLayer->start) / (pLayer->end - pLayer->start);`
                // under no guard at all, because its window was in cycle units to begin with.
                if (!entry.DrivenByPose || local)
                {
                    layerCycle = (cycle - entry.Start) / (entry.End - entry.Start);
                }
            }
#pragma warning restore S1244

            // **`flWeight = clamp( flWeight, 0.0f, 1.0f );`** — `AccumulatePose`'s own first act
            // (`bone_setup.cpp:2408`), under an Assert and a comment saying it should not be
            // necessary. It is: the ramp is extrapolated outside its window whenever `start` and
            // `peak` differ, so a layer just below its own start computes a negative weight. Valve
            // clamps it to zero and `SlerpBones` then skips it on `if (s2 <= 0.0f) continue`, which
            // is what dropping it here reproduces.
            layerWeight = Math.Clamp(layerWeight, 0f, 1f);

            if (layerWeight <= 0f)
            {
                continue;
            }

            // **`STUDIO_REALTIME` on the layered sequence, and this is where TF2 actually puts it**
            // (B309). All 32 sequences carrying the flag are named `layer_*` — MvM bot
            // `layer_primary_jump_floatNoise` and its neighbours — so an autolayer target is the
            // case the flag exists for. The window arithmetic above still decides WHETHER the layer
            // plays and at what weight; the flag decides only where in the animation it is sampled.
            float sampled = skinned.Realtime(target)
                ? Fraction((float)(seconds * skinned.CyclesPerSecond(target)))
                : StudioSequences.ClampCycle(layerCycle, skinned.Loops(target));

            (int frame, float fraction) = StudioSequences.FrameAt(
                sampled,
                skinned.Frames(target),
                skinned.Loops(target));

            layers.Add(new PoseLayer(
                target,
                frame,
                fraction,
                layerWeight,
                skinned.BoneWeights(target),
                Delta: skinned.IsDelta(target),
                Post: skinned.IsPost(target),
                Locks: skinned.LocksOf(target)));

            // **A layered sequence can layer further sequences**, because Valve reaches them through
            // `AccumulatePose` and that calls both passes again. Bounded rather than trusted: the
            // recursion is over data from a file, and a model whose layers cycle would not
            // terminate.
            AutoLayersFor(
                skinned,
                target,
                layerCycle,
                layerWeight,
                values,
                local: false,
                budget - 1,
                seconds,
                layers);
        }
    }

    /// <summary>Where a pose-driven autolayer sits in its parameter's own range.</summary>
    /// <remarks>
    /// **<c>index = m_flPoseParameter[iPose] * (Pose.end - Pose.start) + Pose.start</c>** — the
    /// stored value is normalised and the window is in the parameter's authored units, so the
    /// normalised number has to be expanded before it can be compared. Using it directly would
    /// compare a nought-to-one figure against a window in degrees.
    ///
    /// **Zero when the parameter does not resolve**, which is Valve's own `else index = 0`.
    /// </remarks>
    private static float PoseIndex(
        PropModels.SkinnedModel skinned,
        int sequence,
        StudioAutoLayer entry,
        IReadOnlyList<float> values)
    {
        int shared = skinned.SharedPoseParameter(sequence, entry.PoseParameter);

        if (shared < 0 || shared >= skinned.PoseParameters.Count || shared >= values.Count)
        {
            return 0f;
        }

        StudioPoseParameter parameter = skinned.PoseParameters[shared];

        return (values[shared] * (parameter.End - parameter.Start)) + parameter.Start;
    }

    /// <summary>The sequences a model plays on its own, off the clock.</summary>
    /// <param name="skinned">The model.</param>
    /// <param name="seconds">Demo time now, which is Valve's <c>flRealTime</c>.</param>
    /// <param name="layers">Appended with a layer per autoplay sequence, at full weight.</param>
    /// <remarks>
    /// **`CalcAutoplaySequences`, <c>bone_setup.cpp:4457</c>**, whose whole body is:
    ///
    /// <code>
    ///   int count = m_pStudioHdr->GetAutoplayList( &amp;pList );
    ///   for (i = 0; i &lt; count; i++)
    ///   {
    ///       int sequenceIndex = pList[i];
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
    /// **This is how a model animates part of itself with nothing driving it** — a flag in the
    /// wind, a chain, an idle machine. For such a model it is not decoration: the entity's own
    /// sequence is the idle it is already holding, so autoplay is the only thing that moves.
    ///
    /// **The cycle comes from REAL TIME rather than from the entity**, which is what separates this
    /// from every other layer here. It runs on an entity standing still, it runs on an entity that
    /// is not client-side animated, and two copies of one model are always in step because both
    /// read the same clock rather than their own state.
    ///
    /// **The wrap is `cycle - (int)cycle`, C's truncation toward zero**, which for a demo's
    /// non-negative time is the same as a floor. Written through
    /// <see cref="StudioSequences.ClampCycle(float, bool)"/>'s looping arm, which is that
    /// expression with the negative guard Valve applies to a looping cycle.
    ///
    /// **Weight is a literal 1.0 and never fades.** There is no queue, no lifetime and no
    /// auto-kill: an autoplay sequence is on for as long as the model is drawn.
    /// </remarks>
    private static void AutoplayFor(PropModels.SkinnedModel skinned, double seconds, List<PoseLayer> layers)
    {
        foreach (int sequence in skinned.AutoplaySequences())
        {
            float rate = skinned.CyclesPerSecond(sequence);

            // `if (weight[i] > 0 && panim[i]->numframes > 1)` — Studio_CPS sums nothing for a
            // one-frame animation and returns zero, and `cycle = flRealTime * 0` is zero. That is
            // the sequence's single frame, held, which is what a one-frame autoplay means.
            float cycle = rate > 0f
                ? StudioSequences.ClampCycle((float)(seconds * rate), loops: true)
                : 0f;

            (int frame, float fraction) = StudioSequences.FrameAt(
                cycle, skinned.Frames(sequence), skinned.Loops(sequence));

            layers.Add(new PoseLayer(
                sequence,
                frame,
                fraction,

                // `AccumulatePose( pos, q, sequenceIndex, cycle, 1.0, … )` — the literal.
                1f,
                skinned.BoneWeights(sequence),
                Delta: skinned.IsDelta(sequence),
                Post: skinned.IsPost(sequence),
                Locks: skinned.LocksOf(sequence)));
        }
    }

    /// <summary>Keeps an entity's outgoing sequences alive while they fade.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model.</param>
    /// <param name="sequence">The sequence it is playing now.</param>
    /// <param name="cycle">Where that sequence's cycle stands.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <returns>A layer per sequence still fading, oldest first.</returns>
    /// <remarks>
    /// **`CSequenceTransitioner` and `MaintainSequenceTransitions` together**
    /// (`sequence_Transitioner.cpp:17` and `c_baseanimating.cpp:1815`). Without them every sequence
    /// change is a CUT — a player who stops running snaps out of the run pose in one frame, and a
    /// door that starts opening jumps to its first frame (B286).
    ///
    /// **On a change, the outgoing sequence is pushed with a fade window taken from BOTH
    /// sequences**: `MIN( prevseqdesc.fadeouttime, seqdesc.fadeintime )`. `STUDIO_SNAP` empties the
    /// queue instead, which is how an authored cut stays a cut.
    ///
    /// **Each fading sequence keeps PLAYING**, advanced by
    /// `dt * m_flPlaybackRate * GetSequenceCycleRate( … )` before it is accumulated
    /// (`c_baseanimating.cpp:1853`) — freezing it would blend toward a still frame.
    ///
    /// **Weights come from `GetFadeout`'s spline**, and an entry at or below zero is removed rather
    /// than accumulated at nothing, which is what bounds the queue.
    ///
    /// **Not reproduced: `m_nNewSequenceParity`.** The engine also forces a transition when the
    /// parity counter moves, which restarts a sequence that has not changed number. This project
    /// carries that counter on the pose as `ResetEventsParity`'s neighbour and uses it for the
    /// animation start, so a restart already resets the cycle — but a restart of the SAME sequence
    /// does not currently cross-fade.
    /// </remarks>
    private List<PoseLayer> TransitionsFor(
        SceneProp prop, PropModels.SkinnedModel skinned, int sequence, float cycle, double seconds)
    {
        if (!_transitions.TryGetValue(prop.EntityIndex, out List<FadingSequence>? queue))
        {
            queue = [];
            _transitions[prop.EntityIndex] = queue;
        }

        if (!_currentSequence.TryGetValue(
            prop.EntityIndex, out (int Sequence, float Cycle, double StartedAt, double JumpedAt) was))
        {
            _currentSequence[prop.EntityIndex] =
                (sequence, cycle, prop.Pose.AnimationStartSeconds,
                    prop.Pose.DiscontinuitySeconds);

            return [];
        }

        // **A sequence can begin again without its NUMBER changing, and Valve's second term is
        // exactly for that.** `CheckForSequenceChange` triggers on
        // `currentblend->m_nSequence != nCurSequence || bForceNewSequence`
        // (`sequence_Transitioner.cpp:38`), and `bForceNewSequence` is
        // `m_nNewSequenceParity != m_nPrevNewSequenceParity` (`c_baseanimating.cpp:1831`) — a
        // counter the server bumps on every `SetSequence`, so a cabinet opened twice restarts twice
        // and only the counter says the second one began.
        //
        // **Reaching here the restart is already an `AnimationStartSeconds`**, which the timeline
        // stamps from that same parity (and, for a client-side entity, from
        // `m_bClientSideFrameReset`). Comparing it is the same event one hop later rather than a
        // second derivation of it — the parity itself is not carried this far, and adding a second
        // copy of the signal is how two answers to one question start disagreeing.
        //
        // **Exact, deliberately.** This is not a computed quantity being tested for near-equality:
        // it is a stamp the timeline wrote once and this copies verbatim, so it either is the same
        // stamp or names a different run. A tolerance here would merge two restarts a frame apart,
        // which is the case a repeated gesture produces.
#pragma warning disable S1244
        bool restarted = prop.Pose.AnimationStartSeconds != was.StartedAt;
#pragma warning restore S1244

        // **The other half of the same guard, and it was quoted here while only half of it ran**
        // (B346). `bInterpolate` is `!IsNoInterpolationFrame()` (`c_baseanimating.cpp:1832`), which
        // is `m_ubOldInterpolationFrame != m_ubInterpolationFrame` (`c_baseentity.h:2166`) — a
        // networked parity whose declaration says what it is for: "Call this to cause a
        // discontinuity (teleport)" (`baseentity.h:878`).
        //
        // **Exact, for the reason the restart stamp above is exact**: a time the timeline wrote
        // once and this copies verbatim, not a computed quantity. A tolerance would merge two
        // discontinuities a frame apart.
#pragma warning disable S1244
        bool jumped = prop.Pose.DiscontinuitySeconds != was.JumpedAt;
#pragma warning restore S1244

        if (was.Sequence != sequence || restarted)
        {
            // `if ((seqdesc.flags & STUDIO_SNAP) || !bInterpolate) m_animationQueue.RemoveAll();`
            //
            // **A teleport CUTS.** TF2 bumps the parity when a dead player becomes respawnable,
            // straight after `StopAnimation()` (`tf_player.cpp:14005`), and `CBaseEntity::Teleport`
            // bumps it for every entity given a new position (`baseentity.cpp:4955`) — every
            // teleporter in the game. A transition only exists when the sequence changed, so this
            // bites exactly where the two coincide, which is what a respawn is: without it the
            // pre-death pose fades into the spawn animation instead of cutting.
            if (skinned.SnapsTo(sequence) || jumped)
            {
                // **Counted where the clear HAPPENS, not recomputed by a second route** (B243).
                // The unit tests prove the guard clears a queue when it is called with a jump;
                // only this says a real demo ever reaches it, and it separates the two halves so a
                // reader can tell a teleport from an authored cut rather than seeing one total.
                if (jumped)
                {
                    QueuesClearedByADiscontinuity++;
                }
                else
                {
                    QueuesClearedBySnap++;
                }

                queue.Clear();
            }
            else
            {
                float window = MathF.Min(
                    skinned.FadeOut(was.Sequence), skinned.FadeIn(sequence));

                if (window > 0f)
                {
                    TransitionsCreated++;

                    queue.Add(new FadingSequence(
                        was.Sequence, was.Cycle, seconds, window, prop.Pose.PlaybackRate));
                }
            }

            // **The CONTROL for the two clear counters, and it is not optional here.** An absence
            // means nothing without something that must be present: zero clears can mean the guard
            // never fires or that nothing ever changed sequence, and only this separates them. The
            // first instrument written for B346 reported "0 and 0" for a demo that certainly
            // contains both, and the cause was the probe sampling a single tick — a fact about the
            // measurement rather than about the code.
            SequenceChangesSeen++;
        }

        _currentSequence[prop.EntityIndex] =
            (sequence, cycle, prop.Pose.AnimationStartSeconds, prop.Pose.DiscontinuitySeconds);

        if (queue.Count == 0)
        {
            return [];
        }

        List<PoseLayer> fading = [];

        for (int index = queue.Count - 1; index >= 0; index--)
        {
            FadingSequence leaving = queue[index];

            double elapsed = seconds - leaving.LeftAtSeconds;

            float weight = StudioSequenceFade.Fadeout(elapsed, leaving.FadeOutSeconds);

            // `if (s > 0) … else m_animationQueue.Remove( i )` — a finished entry leaves the queue
            // rather than being accumulated at no weight, which is what stops it growing.
            if (weight <= 0f)
            {
                queue.RemoveAt(index);
                continue;
            }

            double advanced = leaving.Cycle +
                (elapsed * skinned.CyclesPerSecond(leaving.Sequence) * leaving.PlaybackRate);

            // **The FOURTH site of `STUDIO_REALTIME`, found by asking where else `AccumulatePose`
            // runs** (B309). `MaintainSequenceTransitions` ends with
            // `boneSetup.AccumulatePose( pos, q, blend->m_nSequence, flCycle, … )`
            // (`c_baseanimating.cpp:1863`), so a sequence FADING OUT goes through `CalcPoseSingle`
            // like every other and takes the clock if it is flagged. The engine computes and clamps
            // `flCycle` just above that call and `CalcPoseSingle` then discards it — which is easy
            // to read as the clamp being the final word.
            float wrapped = skinned.Realtime(leaving.Sequence)
                ? Fraction((float)(seconds * skinned.CyclesPerSecond(leaving.Sequence)))
                : StudioSequences.ClampCycle((float)advanced, skinned.Loops(leaving.Sequence));

            (int at, float part) = StudioSequences.FrameAt(
                wrapped, skinned.Frames(leaving.Sequence), skinned.Loops(leaving.Sequence));

            fading.Add(new PoseLayer(
                leaving.Sequence,
                at,
                part,
                weight,
                skinned.BoneWeights(leaving.Sequence),
                Delta: skinned.IsDelta(leaving.Sequence),
                Post: skinned.IsPost(leaving.Sequence),
                Locks: skinned.LocksOf(leaving.Sequence)));
        }

        // Oldest first, because the engine walks its queue from the front and each accumulates onto
        // the last — and the loop above ran backwards so it could remove as it went.
        fading.Reverse();

        return fading;
    }

    /// <summary>The bone a model spins procedurally, by the name the engine looks it up under.</summary>
    /// <remarks>
    /// **Two weapons and two names**: `LookupBone("barrel")` for the minigun
    /// (<c>tf_weapon_minigun.cpp:1048</c>) and `LookupBone("procedural_chamber")` for the grenade
    /// launcher (<c>tf_weapon_grenadelauncher.cpp:602</c>). Only the first is driven here; the
    /// second turns on a keyframed spline over `m_iGoalTube`, which is a different mechanism and is
    /// filed rather than half-built.
    ///
    /// **Cached per model, because the lookup is a string compare over every bone** and the engine
    /// does it once in `OnNewModel` rather than per frame.
    /// </remarks>
    private readonly Dictionary<(string Model, string Name), int> _barrelBones = [];

    /// <summary>Each spinning barrel's velocity and angle, integrated across frames.</summary>
    private readonly Dictionary<int, (float Velocity, float Angle)> _barrelSpin = [];

    /// <summary>Demo time at the previous pose pass, so a frame's duration can be differenced.</summary>
    /// <remarks>
    /// **`gpGlobals->frametime`, which the engine has and this layer has to derive.** Clamped at
    /// zero rather than allowed negative: frametime never is, so scrubbing BACKWARDS holds the
    /// barrel where it is instead of unwinding it — the engine has no opinion about a case it
    /// cannot reach, and unwinding would be ours.
    /// </remarks>
    private double _barrelSeconds = double.NaN;

    /// <summary>Spins a minigun's barrel bone, as its own blending rules do.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model, for the bone list.</param>
    /// <param name="posed">The pose being built.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <remarks>
    /// **Per FRAME, which is TF2's arithmetic rather than a misreading.** `UpdateBarrelMovement`
    /// steps the velocity by a literal 0.1 once per call and is reached from
    /// `StandardBlendingRules`, so a minigun genuinely winds up faster at a higher framerate. That
    /// is reproduced rather than corrected — a per-second rate would be our number, not the game's,
    /// and `docs/DECISIONS.md` D89 settles which one this project wants.
    /// </remarks>
    private void SpinBarrel(
        SceneProp prop, PropModels.SkinnedModel skinned, SkeletonPose posed, double seconds)
    {
        // Stryker disable once : a mutant that empties the guard body leaves 'state'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (prop.Pose.MinigunState is not { } state)
        {
            return;
        }

        int bone = BoneNamed(skinned, prop.ModelPath, "barrel");

        if (bone < 0)
        {
            return;
        }

        float frame = double.IsNaN(_barrelSeconds)
            ? 0f
            : (float)Math.Max(seconds - _barrelSeconds, 0d);

        _barrelSpin.TryGetValue(prop.EntityIndex, out (float Velocity, float Angle) spin);

        // **`CanHolsterWhileSpinning` is an econ ATTRIBUTE — the Tomislav's** — and this layer does
        // not read attributes, so the ordinary 0.1 is used for every minigun. Named rather than
        // silently assumed: a Tomislav winds up five times faster and will look slow here.
        spin.Velocity = MinigunBarrel.Approach(
            MinigunBarrel.TargetVelocity((MinigunState)state),
            spin.Velocity,
            MinigunBarrel.Acceleration(canHolsterWhileSpinning: false));

        spin.Angle = MinigunBarrel.Advance(spin.Angle, spin.Velocity, frame);

        _barrelSpin[prop.EntityIndex] = spin;

        posed.BarrelBone = bone;
        posed.BarrelRotation = MinigunBarrel.Rotation(spin.Angle);

        FurthestBarrelAngle = MathF.Max(FurthestBarrelAngle, MathF.Abs(spin.Angle));
    }

    /// <summary>Turns a grenade launcher's chamber, as its own blending rules do.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model, for the bone list.</param>
    /// <param name="posed">The pose being built.</param>
    /// <param name="seconds">Demo time now.</param>
    /// <remarks>
    /// **Driven by absolute time, not a per-frame integration** (B348), which is the difference from
    /// the minigun beside it. `UpdateBarrelMovement` computes
    /// `tVal = ( gpGlobals->curtime - m_flBarrelRotateBeginTime ) / cProceduralBarrelRotationTime`
    /// (<c>tf_weapon_grenadelauncher.cpp:645</c>) — a fraction of a fixed-length animation, so the
    /// same tick always gives the same angle however the viewer arrived at it.
    ///
    /// **The tube advances locally when the animation ends**, exactly as the engine does
    /// (<c>:687</c>): `m_iCurrentTube = m_iGoalTube;`. Without it the chamber would hold its old
    /// base angle until the wire restated the tube, which is a visible stall rather than a jump.
    /// </remarks>
    private void TurnChamber(
        SceneProp prop, PropModels.SkinnedModel skinned, SkeletonPose posed, double seconds)
    {
        // Stryker disable once : a mutant that empties the guard body leaves the fields of
        // 'chamber' ('Current', 'Goal', 'StartedSeconds') unassigned (CS0170), and Safe Mode then
        // drops every mutation in this method — B410.
        if (prop.Pose.Chamber is not { } chamber)
        {
            return;
        }

        int bone = BoneNamed(skinned, prop.ModelPath, "procedural_chamber");

        if (bone < 0)
        {
            return;
        }

        float fraction = ChamberRotation.Fraction(seconds - chamber.StartedSeconds);

        // **Turning only while the two differ**, which is the engine's own guard (`:641`). A settled
        // chamber still gets its bone written, at the base angle of the tube it sits on — the
        // override is outside that test, so a chamber that has stopped is not a chamber the
        // animation reclaims.
        bool turning = chamber.Current != chamber.Goal;

        float partial = turning ? ChamberRotation.Degrees(fraction) : 0f;

        int tube = turning && fraction >= 1f ? chamber.Goal : chamber.Current;

        posed.BarrelBone = bone;
        posed.BarrelRotation = MinigunBarrel.Rotation(ChamberRotation.Angle(tube, partial));

        TurnedChambers++;
        FurthestChamberAngle = MathF.Max(
            FurthestChamberAngle, MathF.Abs(ChamberRotation.Angle(tube, partial)));
    }

    /// <summary>The index of a model's bone with the given name, or -1.</summary>
    /// <remarks>
    /// **The name is a parameter because the engine looks up a different one per weapon** —
    /// `LookupBone( "barrel" )` (<c>tf_weapon_minigun.cpp:1048</c>) and
    /// `LookupBone( "procedural_chamber" )` (<c>tf_weapon_grenadelauncher.cpp:602</c>). Keyed by
    /// model AND name, since one model may be asked for either.
    /// </remarks>
    private int BoneNamed(PropModels.SkinnedModel skinned, string model, string name)
    {
        // **A tuple key, not a concatenation.** Joining the two strings makes
        // `("a", "bc")` and `("ab", "c")` the same entry, and it allocates on every lookup of a
        // path this asks for once per frame per prop.
        (string Model, string Name) key = (model, name);

        if (_barrelBones.TryGetValue(key, out int cached))
        {
            return cached;
        }

        int found = -1;

        for (int bone = 0; bone < skinned.Bones.Count; bone++)
        {
            if (string.Equals(skinned.Bones[bone].Name, name, StringComparison.Ordinal))
            {
                found = bone;
                break;
            }
        }

        _barrelBones[key] = found;
        return found;
    }

    /// <summary>What sequence each entity was playing last frame, and where its cycle stood.</summary>
    /// <remarks>
    /// **`UpdateCurrent`'s job**, kept per entity for the same reason the queue is. The cycle is
    /// remembered as well as the number because a sequence being left has to carry on from where it
    /// was rather than restarting.
    /// </remarks>
    private readonly Dictionary<int, (int Sequence, float Cycle, double StartedAt, double JumpedAt)>
        _currentSequence = [];

    /// <summary>Walks one entity's events for this frame.</summary>
    /// <param name="prop">The entity.</param>
    /// <param name="skinned">Its model, for the MERGED sequence list.</param>
    /// <param name="sequence">The sequence it is playing.</param>
    /// <param name="phase">Its cycle now, after the client's own advance.</param>
    private void AnimationEvents(
        SceneProp prop, PropModels.SkinnedModel skinned, int sequence, float phase)
    {
        // **Through the merged table, because a TF2 player model declares no events at all.** They
        // live in the included `<class>_animations.mdl`, so asking the root model answers "none"
        // for every player in every demo.
        IReadOnlyList<StudioEvent> events = skinned.Events(sequence);

        if (events.Count == 0)
        {
            return;
        }

        _eventStates.TryGetValue(
            prop.EntityIndex, out (AnimationEventState State, int Parity) remembered);

        _firedScratch.Clear();

        AnimationEventState next = AnimationEventFiring.Fired(
            events,
            sequence,
            remembered.State,
            phase,
            resetEvents: remembered.Parity != prop.Pose.ResetEventsParity,
            into: _firedScratch);

        if (_firedScratch.Count > 0 && _render.IsEnabled(LogLevel.Debug))
        {
            _render.LogDebug(
                "{Message}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"event walk entity {prop.EntityIndex} model {prop.ModelPath} sequence {remembered.State.Sequence}->{sequence} cycle {remembered.State.PreviousCycle:0.000}->{phase:0.000} parity {remembered.Parity}->{prop.Pose.ResetEventsParity}: {_firedScratch.Count} fired"));
        }

        _eventStates[prop.EntityIndex] = (next, prop.Pose.ResetEventsParity);

        foreach (StudioEvent fired in _firedScratch)
        {
            _fired.Add(new FiredAnimationEvent(
                prop.EntityIndex,
                fired,
                (prop.Pose.X, prop.Pose.Y, prop.Pose.Z),
                sequence,
                phase));
        }
    }

    /// <summary>The frame and fraction an entity's skeleton was last posed at.</summary>
    /// <param name="entityIndex">Slot in the entity table.</param>
    /// <returns>What `Simulate` computed, or null if the entity has not been posed.</returns>
    /// <remarks>
    /// **The value CARRIED, for the same reason as <see cref="PoseValuesOf"/>** (B243). Whether a
    /// player's cycle is advancing cannot be read off the pose — a player's cycle is not on the
    /// wire, so the pose says zero for ever — and cannot be recomputed by a probe without the
    /// probe becoming a second implementation of `Simulate`. This is the number the sampler was
    /// handed, per frame, which is the only thing that can distinguish "gliding" from "animating"
    /// (B280).
    /// </remarks>
    public (int Sequence, int Frame, float Fraction)? FrameOf(int entityIndex) =>
        _entities.TryGetValue(entityIndex, out AnimatingEntity? animating) &&
        animating.Pose is SkeletonPose posed
            ? (posed.Sequence, posed.Frame, posed.FrameFraction)
            : null;

    /// <summary>The animation layers an entity's skeleton was handed, or null.</summary>
    /// <param name="entityIndex">The entity.</param>
    /// <returns>Its layers, or null when it has no skeleton here.</returns>
    /// <remarks>
    /// **Carried, not recomputed** (B243), and for the same reason as <see cref="FrameOf"/>: a
    /// gesture resolves to a sequence only if the model has that activity, so asking the model a
    /// second time answers what COULD have been layered rather than what was. This is the list the
    /// pose actually accumulated.
    ///
    /// **As last BUILT, not as of this frame** (finding 102): the layers are built at bone setup, so an entity the frame
    /// never set up still answers its last build's list, which may be from an earlier frame.
    /// </remarks>
    /// <summary>One entity's duck-jump offset, in units, or null when it has no skeleton.</summary>
    /// <remarks>
    /// **Per entity rather than summed, because the question is which player is corrected** — an
    /// aggregate would report a number while saying nothing about whether the airborne crouching
    /// one got it and the standing one did not (B314).
    /// </remarks>
    public float? DuckJumpOffsetOf(int entityIndex) =>
        _entities.TryGetValue(entityIndex, out AnimatingEntity? entity) &&
        entity.Pose is SkeletonPose skeleton
            ? skeleton.DuckJumpOffset
            : null;

    public IReadOnlyList<PoseLayer>? LayersOf(int entityIndex) =>
        _entities.TryGetValue(entityIndex, out AnimatingEntity? animating) &&
        animating.Pose is SkeletonPose posed
            ? posed.Layers
            : null;
}
