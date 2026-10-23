using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene;

public sealed partial class EntityModelSet
{
    /// <summary>The skeleton's own IK error list, emptied for this simulate: made once, then reused.</summary>
    /// <param name="posed">The skeleton the errors are gathered for.</param>
    /// <returns>The list <paramref name="posed"/> holds as its <c>IkErrors</c>, empty.</returns>
    /// <remarks>
    /// **The engine's IK context owns its rule lists** (<c>m_ikChainRule</c>, emptied each setup by
    /// <c>CIKContext::Init</c>, <c>bone_setup.cpp:3229</c>; one chain is cleared by a full-strength rule in
    /// <c>AddDependencies</c>, <c>:3319</c>) and allocates nothing per frame. A new list for every prop on
    /// every simulate was 448 MB per 20 s of f12, most of it on the corpse worker (finding 88, B567). Nothing reads
    /// <c>IkErrors</c> past the build that follows its simulate, so reusing it changes no answer.
    /// </remarks>
    internal static List<(StudioIkRule, Vector3, Quaternion, float)> ReusedIkErrors(SkeletonPose posed)
    {
        if (posed.IkErrors is not List<(StudioIkRule, Vector3, Quaternion, float)> held)
        {
            held = [];
            posed.IkErrors = held;
        }

        held.Clear();

        return held;
    }

    /// <summary>Whether a pose walks animation events — false for a pass the engine never makes, such as a decal replay.</summary>
    /// <remarks>
    /// The walk's state is the previous frame's cycle (`m_flPrevEventCycle`), so a pass of our own that walked would move
    /// it to a moment the engine's client never rendered, and the next real frame would cross the wrong span (B504).
    /// </remarks>
    public bool WalksEvents { get; set; } = true;

    /// <summary>Whether this frame covers no demo time — `gpGlobals->frametime == 0`, a paused demo — so no event is walked (B541).</summary>
    /// <remarks>
    /// `C_BaseAnimating::Simulate` calls `DoAnimationEvents` only `if ( gpGlobals->frametime != 0.0f )`
    /// (`c_baseanimating.cpp:5162`), and the client's frametime is zero while `demoplayer->IsPlaybackPaused()`
    /// (`CClientState::GetFrameTime`, engine.dll FUN_1800a2d40). **Not while it skips**: `IsPlaybackPaused` answers
    /// false for the length of a skip whatever the pause flag says (FUN_1800729f0), so a skip's frames walk. The walk's
    /// state is untouched by a paused frame, so the first frame time moves again walks from the last one it did.
    /// </remarks>
    public bool Paused { get; set; }

    /// <summary>Whether a pose only advances cycles and walks events — a skip's frame, which draws nothing (B504).</summary>
    public bool SimulatesOnly { get; set; }

    /// <summary>Brings every entity's animation state up to date, before any bones are built.</summary>
    /// <param name="props">What exists at this tick.</param>
    /// <param name="seconds">Demo time, for advancing cycles.</param>
    /// <param name="fireEvents">Whether these entities are visible, so `DoAnimationEvents` walks them.</param>
    /// <remarks>
    /// **A separate pass, and it is NOT an ordering pass.** This is the engine's own phase split:
    /// <c>SimulateEntities()</c> runs, then <c>ThreadedBoneSetup()</c>, then rendering
    /// (<c>cdll_client_int.cpp:2206-2210</c>). Updating what an entity is DOING is a different job
    /// from deciding when its bones get built, and separating them is what lets the second job be
    /// demand-driven.
    ///
    /// It matters here for a concrete reason: with no ordering, a prop can be reached through a
    /// merge before the loop gets to it, so its sequence and placement must already be right when
    /// that happens. The old code guaranteed that with a sort. This guarantees it by doing all the
    /// state first, which is both simpler and what the engine does.
    /// </remarks>
    private void Simulate(IReadOnlyList<SceneProp> props, double seconds, bool fireEvents = true)
    {
        // **Nothing composed while the pass's entities are brought up to date is kept** (B551) — a parent's bones may
        // not be posed yet. Set and cleared here, around the whole of it, so no return or throw can leave it set.
        _simulating = true;

        try
        {
            SimulatePass(props, seconds, fireEvents);
        }
        finally
        {
            _simulating = false;
        }
    }

    /// <summary><see cref="Simulate"/>'s body.</summary>
    private void SimulatePass(IReadOnlyList<SceneProp> props, double seconds, bool fireEvents)
    {
        _simulatedSeconds = seconds;

        // `if ( gpGlobals->frametime != 0.0f ) DoAnimationEvents(...)` (`c_baseanimating.cpp:5162`) — see `Paused`.
        bool walks = fireEvents && WalksEvents && !Paused;

        // **Indexed first, because a placement follows its parent chain up** and the chain can
        // point anywhere in the list. This is what CalcAbsolutePosition walks (c_baseentity.cpp:4387).
        _propsByEntity.Clear();

        // Resolved attachments belong to this pass's camera, not to the entity — see the field.
        _attachments.Clear();
        _absolute.Clear();

        // Events are a per-FRAME answer; the WALK's state is per entity and survives. **Cleared when the time moves, not
        // per pass**: a frame runs this three times (drawn, undrawn, viewmodel), and clearing per pass left only the
        // viewmodel's events, so no player's footstep ever reached a listener (B172).
        // By bits: the question is "the very value this frame handed every pass", not nearness.
        long frame = BitConverter.DoubleToInt64Bits(seconds);

        if (frame != _firedFrame)
        {
            _fired.Clear();
            _walked.Clear();
            _firedFrame = frame;
        }

        foreach (SceneProp prop in props)
        {
            _propsByEntity[prop.EntityIndex] = prop;
        }

        SimulateEach(props, seconds, walks);

        // Parent links second, so every entity exists before anything points at one.
        _drawnPlacements.Clear();
        _parentPlacements.Clear();

        SettleEach(props, seconds);
    }

    /// <summary>The per-entity half of <see cref="SimulatePass"/>, for whichever props reach it.</summary>
    /// <remarks>
    /// **Split out so the map's static props can reach it from the in-view gather alone** (finding 83 item 1): a
    /// <c>CStaticProp</c> is never on the client entity list, so <c>SimulateEntities</c> never visits one, and it is
    /// reached only by <c>BuildRenderablesList</c>. <see cref="Simulated"/> is counted here, in the loop itself (B243).
    /// </remarks>
    private void SimulateEach(IReadOnlyList<SceneProp> props, double seconds, bool walks)
    {
        foreach (SceneProp prop in props)
        {
            Simulated++;

            // Stryker disable all : a mutant that empties the guard body, or that short-circuits
            // past the TryGetValue, leaves 'skinned' unassigned below (CS0165), and Safe Mode then
            // drops every mutation in this method — B410.
            if (ResolvedFor(prop).Frames is not { } entry ||
                entry.Skinned is not { } skinned)
            {
                continue;
            }

            // Stryker restore all

            AnimatingEntity animating = EntityFor(prop, skinned);

            // Stryker disable once : a mutant that empties the guard body leaves 'posed'
            // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
            if (animating.Pose is not SkeletonPose posed)
            {
                continue;
            }

            // **The engine's list membership, applied as a test** (B259).
            // `UpdateClientSideAnimations` walks `g_ClientSideAnimationList`, and
            // `C_BaseAnimating::PostDataUpdate` puts an entity on that list only when
            // `m_bClientSideAnimation` is set, taking it off again when it is not
            // (`c_baseanimating.cpp:4689`). An entity that did not ask takes its cycle off the wire
            // as an ordinary interpolated value and the client advances nothing for it.
            //
            // **BELOW `EntityFor`, and the first attempt put it above.** This loop does two jobs:
            // it creates the animating entity that the pose step later looks up, and it advances
            // the cycle. Only the second is `UpdateClientSideAnimation` — gating both stopped every
            // prop being posed at all, which four tests said immediately.
            //
            // **A test rather than a maintained list, and the difference is honest.** The engine
            // keeps a list because it streams; this project holds the whole demo and can seek, so a
            // list mutated across frames would be wrong the moment somebody scrubs backwards. The
            // membership RULE is the same and is what decides the work.
            ScenePose where = prop.Pose;
            int sequence = Math.Max(0, where.Sequence);

            // **Advanced from demo time, because nothing networks a player's cycle.** The client
            // runs its own in C_BaseAnimating::FrameAdvance and treats any sent cycle as a
            // correction; a player's is never sent at all, so replaying it holds one frame of a
            // real animation — a convincing statue.
            // **A viewmodel measures its cycle from when its animation STARTED, not from demo time.**
            // `C_BaseViewModel::UpdateAnimationParity` (`c_baseviewmodel.cpp:467`) sets
            // `SetCycle( 0 )` and `m_flAnimTime = curtime` on a parity change, so a restarted
            // animation begins at frame zero rather than wherever a free-running clock happens to
            // be. Everything else leaves `AnimationStartSeconds` at zero and is unaffected.
            double elapsed = seconds - where.AnimationStartSeconds;

            // **Advanced only for an entity that asked the CLIENT to run its cycle** (B259).
            // `UpdateClientSideAnimations` walks `g_ClientSideAnimationList`, which
            // `C_BaseAnimating::PostDataUpdate` joins only when `m_bClientSideAnimation` is set and
            // leaves when it is not (`c_baseanimating.cpp:4689`). Everything else takes `m_flCycle`
            // off the wire as an ordinary interpolated value, so advancing it here runs a
            // server-animated entity at demo time on top of the cycle the server already stated.
            //
            // **The gate is here and not around the loop, which the first attempt tried.** This
            // body also creates the animating entity the pose step looks up and sets
            // `EntityTransform`, the placement — both of which the engine does for every entity
            // regardless. Skipping them stopped every prop being posed, and four tests said so.
            // **Times `m_flPlaybackRate`, which is the third factor in the engine's own line and
            // was missing here** (B281). `C_BaseAnimating::FrameAdvance`, `c_baseanimating.cpp:5493`:
            //
            //     float cyclerate = GetSequenceCycleRate( hdr, GetSequence() );
            //     float addcycle = flInterval * cyclerate * m_flPlaybackRate;
            //
            // The same product appears in `Interpolate` (`c_baseanimating.cpp:5351`) and in the
            // viewmodel's advance (`c_baseviewmodel.cpp:197`), so it is the engine's definition of
            // an advance rather than one function's detail. The field has been decoded since B237
            // and reaches here on the pose; only the BAKED vertex path multiplied by it, so every
            // skinned entity played at rate 1 whatever the demo said.
            float[] poseValues = PoseValues(skinned, where, sequence);
            double advanced = prop.ClientSideAnimated
                ? Advance(prop, elapsed, seconds, skinned.BlendedCyclesPerSecond(sequence, poseValues) * where.PlaybackRate)
                : where.Cycle;

            // **Wrapped only if the sequence LOOPS** (`C_BaseAnimating::ClampCycle`,
            // `c_baseanimating.cpp:1431`). This was `advanced - Math.Floor(advanced)`, which is the
            // looping branch applied to everything — so a one-shot sequence that finished started
            // again, for ever. The owner's report: *"the health cab is in a animation loop, it
            // doesnt stop"*; `resupply_locker.mdl`'s `idle`, `open` and `close` are all flags 0x0.
            //
            // `FrameFor` holds a finished one-shot on its last frame and takes the loop flag to do
            // it, and could never run that branch, because the wrap here had already erased the
            // evidence that the cycle went past one.
            // **`STUDIO_REALTIME` discards all of that** (B309). It is `CalcPoseSingle`'s FIRST
            // branch, before anything else the function does with a cycle
            // (`bone_setup.cpp:1955`):
            //
            //     if (seqdesc.flags & STUDIO_REALTIME)
            //     {
            //         float cps = Studio_CPS( pStudioHdr, seqdesc, sequence, poseParameter );
            //         cycle = flTime * cps;
            //         cycle = cycle - (int)cycle;
            //     }
            //
            // The flag's own words are *"cycle index is taken from a real-time clock, not the
            // animations cycle index"* — so the entity's cycle is not corrected, it is ignored,
            // and a SERVER-animated entity still animates when its sequence carries this.
            //
            // **The wrap is a truncation and not `ClampCycle`**, which is why this is its own
            // branch: `cycle - (int)cycle` ignores `STUDIO_LOOPING`, so a non-looping realtime
            // sequence wraps where an ordinary one would be held on its last frame.
            float phase = skinned.Realtime(sequence)
                ? Fraction((float)(seconds * skinned.CyclesPerSecond(sequence)))
                : StudioSequences.ClampCycle((float)advanced, skinned.Loops(sequence));

            posed.Sequence = sequence;

            // **The frame AND how far past it, which is `CalcPoseSingle`'s two lines** (B279).
            // `iFrame = (int)fFrame; s = (fFrame - iFrame);` — the fraction is what the bone
            // sampling blends with, and without it an animation plays its authored frames and
            // nothing between them.
            (posed.Frame, posed.FrameFraction) = StudioSequences.FrameAt(
                phase, skinned.Frames(sequence), skinned.Loops(sequence));

            // **A frame that draws nothing stops here** (B504): what follows — layers, transitions, IK, the barrel, the duck
            // jump — is `SetupBones`' input, and the engine builds bones only for what it draws. `C_BaseAnimating::Simulate`
            // advances the cycle and walks the events, which is all a skip's frame needs.
            if (SimulatesOnly)
            {
                if (walks && _walked.Add(prop.EntityIndex))
                {
                    AnimationEvents(prop, skinned, sequence, phase);
                }

                continue;
            }

            // **`MaintainSequenceTransitions` first, then `AccumulateLayers`**, which is the order
            // `StandardBlendingRules` runs them in (`c_baseanimating.cpp:1957`) — and the order
            // matters, because each accumulates onto the result of the last. The sequence being
            // faded out is part of the BODY; a gesture goes over the top of whatever the body
            // settled on (B286).

            // **`AddLocalLayers` FIRST, at weight one, because it composes into the sequence's own
            // pose before that pose is blended in** (`bone_setup.cpp:2439`, called with a literal
            // 1.0 under a comment admitting the weight is wrong for IK). For the main sequence that
            // pose IS the base here, so a local layer at full weight over it is exact — see B294
            // for the case this does not reproduce.
            //
            // **Into the skeleton's own list, emptied and refilled** (B574): the engine accumulates into the bone
            // setup's arrays, and a new list here each frame was the corpse worker's `PoseLayer[]` churn. Every reader
            // of `Layers` reads it within the frame.
            // **The layers and IK rules are built when the bones are** (finding 102): `StandardBlendingRules` runs inside
            // `SetupBones` (`c_baseanimating.cpp:1953`, `:2796`), so an entity nobody draws or asks for builds none this
            // frame. Only `TransitionsFor` runs here, because its queue watches every frame's sequence for a change and
            // moving it would change what an entity re-entering the view blends — a divergence kept, not ported (D131).
            PendingBlend pending = PendingFor(prop.EntityIndex, animating);
            pending.Transitions = TransitionsFor(prop, skinned, sequence, phase, seconds);
            pending.Set(prop, skinned, sequence, phase, poseValues, seconds);


            // **`DoAnimationEvents`, and it has to be HERE** (B275). The walk asks what the cycle
            // crossed since last frame, and for a player the cycle is not on the wire at all — the
            // client advances it, which is the `phase` computed a few lines up. A probe reading
            // `m_flCycle` off the pose instead reported zero events on a demo full of them, because
            // for every player that value is a constant.
            //
            // The engine's own call site is the same place: `C_BaseAnimating::FrameAdvance` runs
            // the cycle forward and then dispatches, so an event is noticed on the frame the
            // animation reached it rather than on the packet that mentioned the sequence.
            if (walks && _walked.Add(prop.EntityIndex))
            {
                AnimationEvents(prop, skinned, sequence, phase);
            }

            // Computed once above for the autolayer envelopes and reused here rather than asked
            // twice: the two-pass move_x rescale inside it opens the model.
            posed.PoseValues = poseValues;

            // **`CalcBoneAdj`'s two halves, which come from different places** (B288). The model
            // says which bone each input drives and over what range; the demo says what the input
            // is, networked as eleven bits over nought to one. Neither alone bends anything, and
            // both have been read and dropped until now.
            posed.Controllers = skinned.Controllers;
            posed.BoneControllers = where.BoneControllers;

            // **`CTFMinigun::StandardBlendingRules`, which overwrites one bone after everything
            // else** (B347). The barrel's angle is not on the wire — the client integrates it from
            // `m_iWeaponState` alone — so this is the same shape as the burn clock: watch the
            // networked state and run the engine's own arithmetic from it.
            SpinBarrel(prop, skinned, posed, seconds);

            // **The grenade launcher's chamber, the other weapon in this override family** (B348).
            // A different mechanism from the minigun's — a fixed-length keyframed animation rather
            // than an integrated velocity — and it runs after, because only one of the two can
            // apply to any model and the second call finds no bone.
            TurnChamber(prop, skinned, posed, seconds);

            // **The ROOT model's bytes, for the jiggle bones' own parameters** (B293).
            // `mstudiobone_t::pProcedure()` is an offset from the BONE, and the bones being posed
            // are `Models[0]`'s — reading it against an included animation model's bytes would land
            // on arbitrary floats and produce springs with nonsense constants rather than an error.
            // The same index-space rule the bone controllers follow.
            // **The null arm is spelled out, and it has to be** (B333). `Models[0]` is a `byte[]`,
            // so a conditional against a bare `null` takes `byte[]` as its own type and lifts the
            // RESULT into the property — turning the absent case into `ReadOnlyMemory.Empty`, which
            // is PRESENT. Both of `SkeletonPose`'s `JiggleSource is not { }` guards were dead for
            // that reason.
            posed.JiggleSource = skinned.Models.Count > 0
                ? skinned.Models[0]
                : (ReadOnlyMemory<byte>?)null;

            // **The IK rules, resolved here because the BLEND WEIGHTS live here** (B296). A rule's
            // influence is accumulated across the same up-to-three animations the sequence blends,
            // and only this side knows which they are; the skeleton is handed the answer rather
            // than the means to recompute it.
            // **A SCALED model opts out of IK entirely, and the engine says why**
            // (`c_baseanimating.cpp:2841`):
            //
            //     // NOTE: For model scaling, we need to opt out of IK because it will mark the
            //     // bones as already being calculated
            //     if ( !IsModelScaled() ) { ...allocate m_pIk... }
            //     else                    { if ( m_pIk ) { delete m_pIk; m_pIk = NULL; } }
            //
            // Deleting the context is not a saving, it is a correctness measure: IK marks bones as
            // already calculated, and a scaled skeleton's are not where an unscaled solve put them.
            //
            // **The test is against FLT_EPSILON rather than exact** — `m_flModelScale >
            // 1.0f+FLT_EPSILON || m_flModelScale < 1.0f-FLT_EPSILON` (`c_baseanimating.h:780`) —
            // so a scale a float's last bit away from one is NOT scaled, which matters because a
            // value off the wire and one written as a literal need not be bit-identical.
            //
            // Nothing in the committed corpus is scaled; TF2 scales MvM giants and some Halloween
            // bosses. Implemented because the engine does it.
            bool scaled =
                prop.Pose.Scale > 1f + float.Epsilon || prop.Pose.Scale < 1f - float.Epsilon;

            posed.IkChains = scaled ? [] : skinned.IkChains;

            // **The MAIN sequence's locks** (B311). A layer carries its own; this is the first
            // `AccumulatePose`'s bracket, whose "before" is the bind pose.
            //
            // **Dropped for a scaled model for the same reason the chains are** (B301): a solver
            // measures the chain's links from the posed skeleton, so a model the entity has resized
            // reports link lengths that do not match the animation's, and the solve reaches for a
            // point the leg can no longer make.
            posed.Locks = scaled ? [] : skinned.LocksOf(sequence);

            // **The three per-BONE scales, carried from the wire** (B312). Unlike the IK above,
            // these are NOT dropped for a scaled model: `m_flModelScale` and `m_flHeadScale` are
            // different mechanisms that TF2 applies together — a mini-sentry at 0.75 with a
            // Halloween head is both.
            // **The duck-jump correction** (B314). Ducking in mid-air shrinks the player's hull
            // from 82 units to 62, which moves their ORIGIN — so the engine draws the skeleton the
            // difference lower at that instant and eases it to zero over 0.15 seconds. Without it
            // the model pops twenty units up on every crouch jump, and roughly a fifth of the
            // player states in a real demo are airborne and ducking.
            //
            // **State per entity, because the answer depends on when the duck began** and when it
            // last held rather than on this frame — none of which the demo carries, since the
            // client derives all three from the flags over time.
            if (!_duckJumps.TryGetValue(prop.EntityIndex, out DuckJump? duck))
            {
                duck = new DuckJump();
                _duckJumps[prop.EntityIndex] = duck;
            }

            // `VEC_HULL_MAX_SCALED( this )` — the hulls scale with the model, so a mini-sentry's
            // rider or a resized player gets a proportional correction rather than a fixed one.
            posed.DuckJumpOffset = duck.Update(
                prop.Pose.Flags is { } flags && (flags & PlayerActivityState.Ducking) != 0,
                prop.Pose.Flags is { } state && (state & PlayerActivityState.OnGround) == 0,
                seconds) * DuckHullDifference * prop.Pose.Scale;

            posed.HeadScale = prop.Pose.HeadScale;
            posed.TorsoScale = prop.Pose.TorsoScale;
            posed.HandScale = prop.Pose.HandScale;

            // **Every accumulated sequence contributes its rules, not just the main one** (B297).
            // `AccumulatePose` calls `AddDependencies` for each sequence it accumulates and
            // `AddSequenceLayers` then recurses, so a layer's rules count at the layer's own
            // weight. This is not a detail: TF2's aim matrices are AUTOLAYERS of the movement
            // sequences — `stand_PRIMARY` layers `PRIMARY_aimmatrix_idle`, `run_PRIMARY` layers
            // `PRIMARY_aimmatrix_run` — and every solving IK rule in the game lives on them. Read
            // from the main sequence alone, IK finds nothing but releases for ever.
            // **The placement, which is what makes the built bones WORLD space.** A merged item
            // takes its wearer's bones and those already carry the wearer's placement, so nothing
            // downstream has to know where a wearer stands — see D88 and finding 35 section 7a.
            posed.EntityTransform = PlacementOf(prop);

            // **B515: a left-handed viewmodel's mirror, on its ROOT bones** — `C_BaseViewModel::ApplyBoneMatrixTransform`
            // through the player's view. Not on a bone-merged `c_` weapon: its merged bones arrive mirrored, and
            // `C_ViewmodelAttachmentModel` inherits the base body, which mirrors nothing. Its jiggle still takes the
            // flag, through `C_BaseAnimating::ShouldFlipViewModel`'s parent walk (`c_baseanimating.cpp:2097`).
            posed.RootReflection = prop.FlipViewModel && prop.AttachedTo is null && ViewmodelProjection is { } view
                ? ViewmodelFlip.Reflection(view.Eye, view.Right)
                : null;
            posed.Flipped = prop.FlipViewModel;

            // **IsEnabled first, because the KEY allocates.** `path + "#skin"` builds a string per
            // prop per frame before the set is even consulted, and this sits in Simulate, which
            // walks every prop (B191).
            if (_render.IsEnabled(LogLevel.Debug) && _reportedFrames.Add(prop.ModelPath + "#skin"))
            {
                _render.LogDebug(
                    "{Message}",
                    $"skinned {prop.ModelPath}: sequence {sequence}" +
                    $"{(skinned.IsDelta(sequence) ? " DELTA" : string.Empty)}" +
                    $"{skinned.UnimplementedFor(sequence)}, " +
                    $"{skinned.Frames(sequence)} frames at " +
                    $"{skinned.CyclesPerSecond(sequence):0.###} cycles a second, " +
                    $"phase {phase:0.###} -> frame {posed.Frame}");
            }
        }
    }

    /// <summary>The placement half of <see cref="SimulatePass"/>: light points, parent and drawn placements, attachment.</summary>
    private void SettleEach(IReadOnlyList<SceneProp> props, double seconds)
    {
        foreach (SceneProp prop in props)
        {
            _lightPoints[prop.EntityIndex] = IlluminationPoint(prop, prop.Pose);

            ScenePose at = prop.Pose;

            PropTransform placed =
                new(at.X, at.Y, at.Z, at.Pitch, at.Yaw, at.Roll, at.Scale);

            // **Every entity, drawn or not, because a TRANSFORM is not a question about drawing**
            // (B231). `CalcAbsolutePosition` composes a child onto its parent's transform with no
            // reference to whether the parent is rendered — and on `cp_fulgur` the parent of every
            // gate is an invisible `func_door`, so the one map that matters here is the one that
            // includes entities nobody draws.
            _parentPlacements[prop.EntityIndex] = placed;

            // Recorded only for props that will actually be drawn, so "the wearer is not being
            // drawn" is answerable without depending on the draw loop's order — which is the whole
            // point of there no longer being one.
            //
            // **Kept SEPARATE from the map above, and the difference is Valve's.** A bone-merged
            // item follows `if ( baseDrawn )` — no wearer on screen, no hat — while a parented
            // entity follows its parent's transform regardless. Merging the two would either draw
            // hats on invisible players or delete props hung on invisible movers, and this project
            // has now done the second one for real.
            // The object's own resolution, so no path is hashed (finding 82); `Batches`' clamp, from the batches it holds.
            ResolvedModel model = ResolvedFor(prop);

            if (IsDrawable(prop.Kind) && model.Batches is { Count: > 0 } baked &&
                baked[Math.Clamp(SelectWith(model.Frames, prop, seconds).Frame, 0, baked.Count - 1)].Count > 0)
            {
                _drawnPlacements[prop.EntityIndex] = placed;
            }

            if (!_entities.TryGetValue(prop.EntityIndex, out AnimatingEntity? animating))
            {
                continue;
            }

            // **Followed only when bone-merged** — `IsFollowingEntity` is `EF_BONEMERGE && MOVETYPE_NONE && GetMoveParent()`
            // (the `IsFollowingEntity` note further down this file). A plain move child following its parent merged any bone it
            // shared by name and stood at the parent's attachment, not attachment × local (B551).
            animating.Follows = prop is { AttachedTo: { } wearer, BoneMerged: true }
                ? _entities.GetValueOrDefault(wearer)
                : null;

            Attach(prop, animating, seconds);
        }
    }

    /// <summary>Places an item that hangs from a named attachment point rather than merging.</summary>
    /// <remarks>
    /// **A hat shares bone names with the player and takes their matrices; a halo, an MvM canteen, a
    /// spellbook and a spy's sapper share none** — the spellbook's only bone is called <c>mvm</c>
    /// and no player has one — so the merge matches nothing and the item would keep its own pose,
    /// which for an attached entity is the map origin (B82).
    ///
    /// The engine hangs those off the WEARER's attachment table:
    /// <c>ConcatTransforms( GetBone( iBone ), pattachment.local, world )</c>, one-based.
    ///
    /// **Resolved into the item's own placement rather than into a draw-time matrix**, which is the
    /// change D88 makes here. An attachment gives a WORLD transform, and world is where this
    /// project's bones now live — so it belongs where the entity's placement belongs, and the merge
    /// overwrites whatever bones do match on top of it. Setting both is correct precisely because
    /// the two cases do not overlap: an item either shares bone names or it does not.
    ///
    /// **Asking the wearer for bones here is Valve's own shape**, not a layering slip:
    /// <c>CalcAttachments()</c> is a call to <c>SetupBones( NULL, -1, BONE_USED_BY_ATTACHMENT,
    /// ... )</c>. Resolving an attachment IS a bone setup, and the cache makes the draw pass's
    /// repeat free.
    /// </remarks>
    private void Attach(SceneProp prop, AnimatingEntity animating, double seconds)
    {
        // **A move child that is NOT bone-merged is attachment × LOCAL, which `PlacementOf` already composed through
        // `Absolute`** (B551, `c_baseentity.cpp:4330-4401`); the bare attachment matrix below would drop its own offset.
        if (!prop.BoneMerged)
        {
            return;
        }

        // Stryker disable all : a mutant that empties the guard body leaves 'point', 'wearer',
        // 'posed', 'wearerModel' and 'attachments' unassigned below (CS0165), and Safe Mode then
        // drops every mutation in this method — B410.
        if (prop.AttachmentPoint is not { } point ||
            animating.Follows is not { } wearer ||
            animating.Pose is not SkeletonPose posed ||
            !_entityModels.TryGetValue(prop.AttachedTo ?? -1, out string? wearerModel) ||
            !_frames.TryGetValue(wearerModel, out PropModels.ModelFrames? worn) ||
            worn.Attachments is not { Count: > 0 } attachments ||
            point < 1 || point > attachments.Count)
        {
            return;
        }

        // Stryker restore all

        if (!wearer.SetupBones(StudioBoneFlags.UsedByAnything, seconds))
        {
            return;
        }

        // **The whole TABLE, once per entity, which is the shape of the engine's own call.**
        // `SetupBones_AttachmentHelper` (`c_baseanimating.cpp:2055`) loops every attachment the
        // model declares and caches each with `PutAttachment( i + 1, world )`, and `SetupBones`
        // runs it exactly once per entity per frame:
        //
        //     if( !( oldReadableBones & BONE_USED_BY_ATTACHMENT ) &&
        //         ( boneMask & BONE_USED_BY_ATTACHMENT ) )
        //
        // — "not readable before, wanted now". Resolving one attachment per CHILD, as this did,
        // gives the same arithmetic and repeats it: two items on one attachment point concatenated
        // the same matrices twice, and a wearer with several did the bone lookup once per item.
        float[][] resolved = AttachmentsOf(prop.AttachedTo ?? -1, wearer, attachments);

        // Stryker disable once : a mutant that empties the guard body leaves 'placement'
        // unassigned (CS0165), and Safe Mode then drops every mutation in this method — B410.
        if (point > resolved.Length || resolved[point - 1] is not { } placement)
        {
            return;
        }

        StudioAttachment attachment = attachments[point - 1];

        // Identity for the wearer's own transform, because its bones are already in world space —
        // the placement it used to need is folded into them (finding 35 section 7a).
        //
        // Back through MatrixConvention, because AttachmentPlacement returns a MODEL matrix and an
        // entity placement is a matrix3x4_t. Same boundary as PlacementOf, same one crossing point.
        posed.EntityTransform = placement;

        if (_props.IsEnabled(LogLevel.Debug) && _reportedPoses.Add(prop.ModelPath + "#attached"))
        {
            _props.LogDebug(
                "{Message}",
                $"attached {prop.ModelPath} to {attachment.Name} " +
                $"(point {point}, bone {attachment.Bone}) on {wearerModel}");
        }
    }

    /// <summary>What each entity's next build blends, as this frame's simulate left it (finding 102).</summary>
    private readonly Dictionary<int, PendingBlend> _pendingBlends = [];

    /// <summary>The entity's pending blend, made once with the hook that builds it on the entity's next bone setup.</summary>
    private PendingBlend PendingFor(int entityIndex, AnimatingEntity animating)
    {
        if (!_pendingBlends.TryGetValue(entityIndex, out PendingBlend? pending) || pending.Entity != animating)
        {
            // A replaced entity lets go of its hook, so it does not hold the old frame's prop, model and pending alive.
            if (pending is not null)
            {
                pending.Entity.BeforeBuild = null;
            }

            pending = new PendingBlend(animating);
            PendingBlend made = pending;
            animating.BeforeBuild = () => BuildBlend(made);
            _pendingBlends[entityIndex] = pending;
        }

        return pending;
    }

    /// <summary>`StandardBlendingRules`' inputs for one entity, in its order (`c_baseanimating.cpp:1986-1996`), once a frame.</summary>
    private void BuildBlend(PendingBlend pending)
    {
        if (!pending.Ready || pending.Entity.Pose is not SkeletonPose posed)
        {
            return;
        }

        // Stryker disable once Boolean : an equivalent mutant for the picture — a second build in the frame rebuilds the same
        // inputs into the same list; the flag saves the work (only `LayersFor`'s B350 substitution counter would count twice).
        pending.Ready = false;

        (SceneProp prop, PropModels.SkinnedModel skinned, int sequence, float phase, float[] poseValues, double seconds) =
            (pending.Prop!, pending.Skinned!, pending.Sequence, pending.Phase, pending.Parameters!, pending.Seconds);

        // **`AddLocalLayers` FIRST, at weight one** (`bone_setup.cpp:2439`), into the skeleton's own list (B574).
        List<PoseLayer> composed = posed.Layers as List<PoseLayer> ?? [];
        composed.Clear();
        AutoLayersFor(skinned, sequence, phase, 1f, poseValues, local: true, LayerDepth, seconds, composed);

        // **Then `AddSequenceLayers` for the main sequence, before the transitions** (`:2449`).
        AutoLayersFor(skinned, sequence, phase, 1f, poseValues, local: false, LayerDepth, seconds, composed);

        composed.AddRange(pending.Transitions);
        composed.AddRange(LayersFor(prop, skinned, seconds));

        // **`CalcAutoplaySequences` LAST of the three** (`c_baseanimating.cpp:1996`), B291.
        AutoplayFor(skinned, seconds, composed);

        posed.Layers = composed;

        // **Every accumulated sequence contributes its IK rules, not just the main one** (B297).
        List<(StudioIkRule, Vector3, Quaternion, float)> asked = ReusedIkErrors(posed);

        if (posed.IkChains.Count > 0)
        {
            IkFor(skinned, sequence, phase, 1f, poseValues, asked);

            foreach (PoseLayer layer in composed)
            {
                IkFor(skinned, layer.Sequence, FractionOf(skinned, layer), layer.Weight, poseValues, asked);
            }
        }

        posed.IkErrors = asked;
    }
}
