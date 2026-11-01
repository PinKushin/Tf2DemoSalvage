# Plan 2026-11-01: fading sequences newest first (B633), and every `AccumulatePose` runs its local pass (B634)

Branch `fix/b633-b634-layer-order`, from `19efea71` (`perf/b597-gpu-split`, which carries B624's builder
`EntityModelSet.AccumulatePose`). Written before any production edit. Evidence tags as in
`2026-10-31-iklock-bracket-read.md`: **[src]** read from published source, **[meas]** measured with the instrument named,
**[arith]**, **[interp]**, **[pred]**.

## 1. Valve [src]

Read in all four trees under `F:/src`: `source-sdk-2013/src` (lines below unless named), `hl2sdk-tf2`, `hl2sdk` (Orange
Box) and `hl2sdk-episode1`. The structure is the same in every one.

### B633: the transition queue is walked newest first

- **Append at the tail.** `CSequenceTransitioner::CheckForSequenceChange` treats the tail as the current sequence
  (`game/shared/sequence_Transitioner.cpp:36`) and, on a change, `m_animationQueue.AddToTail()` pushes a new current
  (`:64`), so the outgoing sequences sit before it oldest first. Same lines in `hl2sdk-tf2` and `hl2sdk`
  (`game/shared/sequence_transitioner.cpp:33-65`) and `hl2sdk-episode1` (`game_shared/sequence_transitioner.cpp:33-65`).
- **Prune in place.** `UpdateCurrent` removes an entry whose `GetFadeout( flCurTime )` is `<= 0` with
  `m_animationQueue.Remove( i )` (`:101-114`). The relative order of the survivors is kept.
- **Walk from the newest fading entry down.** `MaintainSequenceTransitions`:
  `for (int i = m_SequenceTransitioner.m_animationQueue.Count() - 2; i >= 0; i--)` (`game/client/c_baseanimating.cpp:1848`),
  each `boneSetup.AccumulatePose( pos, q, blend->m_nSequence, flCycle, blend->m_flWeight, … )` (`:1863`). Same loop at
  `hl2sdk-tf2` `:1848`, `hl2sdk` `:1604`, `hl2sdk-episode1` `cl_dll/c_baseanimating.cpp:1435`.
- **So the newest fading sequence is accumulated FIRST and the oldest LAST**, each a partial slerp onto the result of
  the one before. Order matters whenever two are in flight: slerp(slerp(P, A, a), B, b) is not slerp(slerp(P, B, b), A, a).
- **The same walk is in `GetBlendedLinearVelocity`** (`hl2sdk` `c_baseanimating.cpp:4778`, `hl2sdk-episode1` `:4146`):
  newest first, `Lerp( flWeight, … )`. Ours has no such function (searched `managed/`), so there is no neighbour to fix.

### B634: `AddLocalLayers` runs inside every `AccumulatePose`

`CBoneSetup::AccumulatePose` (`public/bone_setup.cpp:2393-2455`; `hl2sdk-tf2` the same lines; `hl2sdk` the free function
at `:2318-2367`; `hl2sdk-episode1` `:1604-1658`), in order:

1. `flWeight = clamp( flWeight, 0.0f, 1.0f )` (`:2408`).
2. `seq_ik` opens on `pos`/`q` if the sequence locks (`:2424-2429`).
3. **`if (seqdesc.flags & STUDIO_LOCAL) ::InitPose( m_pStudioHdr, pos2, q2, m_boneMask );`** (`:2433`) — the sequence's
   OWN pose starts at bind.
4. `if (CalcPoseSingle( …, pos2, q2, … ))` (`:2436`) then **`AddLocalLayers( pos2, q2, seqdesc, sequence, cycle, 1.0, … )`**
   (`:2439`) and **`SlerpBones( m_pStudioHdr, q, pos, seqdesc, sequence, q2, pos2, flWeight, m_boneMask )`** (`:2440`).
5. `AddDependencies` (`:2444-2447`), `AddSequenceLayers( pos, q, … flWeight … )` (`:2449`), `SolveSequenceLocks` on
   `pos`/`q` (`:2451-2454`).

`AddLocalLayers` (`:2218-2286`) returns at once unless the sequence carries `STUDIO_LOCAL` (`:2229`), runs only the
`STUDIO_AL_LOCAL` autolayers with the window in cycle units, and calls **`AccumulatePose( pos, q, … )` on the `pos`/`q`
it was handed, which here is `pos2`/`q2`** (`:2284`). So a local layer is a full `AccumulatePose` INTO the sequence's own
pose: its lock bracket captures and solves on `pos2`, its own autolayers land in `pos2`, all before `pos2` is slerped
into the accumulator at the parent's `flWeight` and per-bone weights.

Every entry point is `AccumulatePose` (main `c_baseanimating.cpp:1986`, transitions `:1863`, overlays
`c_baseanimatingoverlay.cpp:369`, autoplay `bone_setup.cpp:4485`, autolayers `:2209` and `:2284`), so this applies to all.

## 2. Ours, at `19efea71` [src]

- **B633**: `TransitionsFor` (`managed/Tf2DemoSalvage.Scene/EntityModels.Autoplay.cs:403-562`) walks our queue from
  `queue.Count - 1` down (`:517`), which is already newest first, then **`fading.Reverse()`** (`:559`) under a comment
  saying the engine "walks its queue from the front" (`:557-558`). The `<returns>` says "oldest first" (`:379`). Our
  queue holds only the outgoing sequences (`queue.Add`, `:493`; the current one is not stored), so our `Count - 1` is
  Valve's `Count() - 2`. The one consumer is `BuildBlend`'s `foreach (FadingRoot fading in pending.Transitions)`
  (`EntityModels.Simulation.cs:647`).
- **B634, scene**: the builder `AccumulatePose` (`EntityModels.Autoplay.cs:247-281`) appends the sequence's `PoseLayer`
  and runs `AutoLayersFor(…, local: false, …)` only (`:278`). Its remarks say the local pass is "not ported" (`:242-245`).
  So for every sequence reached through it — transitions, wire layers, gestures, autoplay, and every autolayer target,
  including the target of a main sequence's local layer — **a `STUDIO_LOCAL` sequence's local layers are dropped
  outright, at any weight**. RISKS B634 called this a weight-below-one subtlety; it is larger than that.
- **B634, main sequence**: `BuildBlend` runs `AutoLayersFor(…, local: true, …)` at weight 1 straight into the list
  (`EntityModels.Simulation.cs:636`). The base is copied into `result` whole (`SkeletonPose.cs:1036-1051`), so `result` is
  both Valve's `pos` and `pos2` for the main sequence and composing its local layers there is exact at weight 1. Kept.
- **B634, skeleton**: `SkeletonPose.Accumulate` (`managed/Tf2DemoSalvage.Animation/Animating/SkeletonPose.cs:987-1219`)
  composes every layer onto the one `result` array; a stack `_open` (`:949`) of `(Close, Locks)` brackets closes in
  `CloseBrackets` (`:1232`). There is no second buffer, so a layer cannot be composed into another layer's pose.

## 3. Who can see it [meas]

- **B634: all 48 `STUDIO_LOCAL` sequences are viewmodel arms**, `models/weapons/c_models/c_<class>_animations.mdl`:
  `melee_allclass_{draw,idle,swing*}` and `throw_{draw,idle,fire}`, every class. Each declares exactly one
  `STUDIO_AL_LOCAL` autolayer and no other autolayers; **none is `STUDIO_DELTA`**. No `STUDIO_AL_LOCAL` autolayer sits under
  a sequence without `STUDIO_LOCAL` (0, so none is dead). Instrument: `sequence-flags`, extended on this branch to list
  every one, over all 14,416 models in `tf2_misc_dir.vpk`. Control: the same run counts `STUDIO_LOOPING` 2,982 and
  `STUDIO_DELTA` 1,656, so the reader reads flags.
- **So no player model reaches B634** [meas, for `tf2_misc_dir.vpk`]. A viewmodel reaches it only when one of those 48
  is NOT its main sequence: a cross-fade OUT of a draw, idle, swing or throw [interp: not observed]. At weight 1 as a main
  sequence the existing path is exact (§2).
- **B633** needs two transitions in flight at once on one entity: two sequence changes inside one fade window
  (`MIN( prev.fadeouttime, next.fadeintime )`, TF2's player sequences mostly 0.2 s) [interp; counted in §7 if the probe
  can carry it].

## 4. The change (minimum, structural)

### B633

Delete `fading.Reverse()`, rewrite the comment and the `<returns>` to Valve's order. **What follows from it**: the IK
rules gathered by `BuildBlend`'s `IkFor` loop (`EntityModels.Simulation.cs:668-671`) iterate `composed` in order, so their
order flips to Valve's too (`AddDependencies` is called in accumulation order, `bone_setup.cpp:2444-2447`).

### B634

**`PoseLayer.Local`** (new, last, default 0): how many of the layers immediately after this one are its `AddLocalLayers`
subtree, composed into the layer's OWN pose before that pose is blended in. `Nested` keeps meaning the whole subtree
inside the bracket and now covers both passes: `[S, S's local subtree (Local layers), S's non-local subtree]`, with
`S.Nested = Local + non-local`. A default of 0 leaves every existing construction and `AccumulatePathReference` identical.

- **Scene**: the builder `AccumulatePose` appends `S`, then runs `AutoLayersFor(…, weight: 1f, local: true, …)` —
  Valve's literal `1.0` at `:2439` — and writes `Local`, then the non-local pass at `S`'s weight, then `Nested`. The
  `<remarks>` "not ported" paragraph goes. `BuildBlend`'s main-sequence calls are unchanged (§2).
- **Skeleton**: the existing close-at-index LIFO gains a second kind of entry, a local frame:
  - a layer with `Local > 0` (and weight above 0) is sampled into a scratch pose at the next depth, seeded with the
    bind pose (`InitPose`, `:2433`), instead of being composed onto the current target;
  - layers `i+1 … i+Local` compose onto that scratch, and their brackets capture and solve on it;
  - when the frame closes, the scratch is composed onto its parent target at `S`'s weight and per-bone weights — the
    `SlerpBones` at `:2440` — and then `S`'s own non-local subtree and bracket proceed on the parent target.
  - LIFO gives Valve's order at a shared close index: the local subtree's brackets, then the scratch compose, then `S`'s
    solve. `S`'s bracket captures on the parent target BEFORE the sample (`:2424-2429`), as today.
  - Scratch buffers are per depth, allocated on first use and kept, like `Bracket(depth)`, so steady state allocates
    nothing.
  - **At `S` weight 0 the local range is skipped**: `pos2` is discarded by a `SlerpBones` at 0, so skipping is Valve's
    result. (Not an equivalent mutant, as first written here: without the skip the subtree would compose onto the parent
    target, since no own pose was opened. `Build_AZeroWeightLayerWithALocalLayer_ComposesNeither` holds it.) The IK rules
    of those layers are still gathered scene-side, as Valve's `AddDependencies` runs regardless.
- **Seed** (corrected 2026-11-01 in the review batch; the first version of this line said the opposite and was wrong):
  `InitPose` seeds bind (`:2433`), but `CalcPoseSingle` → `CalcAnimation` then overwrites every bone whose
  `seqdesc.weight` is above 0 (`bone_setup.cpp:1095-1126`). A bone with a track gets the track's value. A bone without
  one gets the bind pose for an ordinary animation, and **identity / zero for a `STUDIO_DELTA` one** (`:1112-1116`). So a
  delta local sequence's unnamed weighted bones are identity, not bind. The code now seeds identity for a delta layer
  (`SkeletonPose.SeedIdentity`), keyed on the sequence's delta bit, where the engine keys on the animation's. No shipped
  local sequence is delta (census), so the case is synthetic only:
  `NestedLockBracketConformanceTests.Build_ADeltaLocalParent_SeedsItsUnnamedBonesWithIdentity`.
- **Timers**: the scratch sample lands in `layer sampling`, its compose and the scratch-onto-parent compose in
  `compose`, brackets in `capture`/`solve`. No new span. `AccumulatePartsTests` stay unchanged.

### Not changed, stated

- **Main sequence bone weights.** The base is copied whole, where Valve's main `SlerpBones` at 1.0 keeps the bind pose
  on a bone whose `seqdesc.weight` is 0. A neighbour, not this change; to be filed.
- **The rules' weight for a local layer** stays the 1.0-derived one: Valve's own comment at `:2438` ("this weight is
  wrong, the IK rules won't composite at the correct intensity") says the same.

## 5. Tests, in order (red first)

1. **B633, Scene** (`AccumulatePoseConformanceTests`, empty autolayer dictionary so no children interleave): play 1, then
   2, then 0 inside one fade window; while both fade, `LayersOf` lists 2 then 1. Red today (1 then 2).
2. **B634, Scene**: an overlay on `STUDIO_LOCAL` sequence 1 at weight 0.5, declaring an unwindowed `STUDIO_AL_LOCAL`
   autolayer over 2, gives `[(1, 0.5, Local 1, Nested 1), (2, 1.0, 0, 0)]` — 2 at 1, not 0.5, is the discriminator.
   Control in the same fixture: a main sequence that is `STUDIO_LOCAL` with the same local autolayer shows 2, proving the
   flags reach `IsLocal` from the synthetic bytes.
3. **B634, Skeleton** (`NestedLockBracketConformanceTests`' leg): `S` at weight 0.5 puts the root at 4, its local layer
   `L` at weight 1 puts it at 8. Composed into `S`'s own pose first: root = 0.5 · 8 = **4**. Flat (today): 0.5 · 4 then
   onto that 1 · 8 = **8**. Control, `Local = 0`: 8. And a lock inside the local subtree pins on the scratch, not on the
   accumulator.
4. Sabotage each by hand (no agents in this session): restore `Reverse`, drop the `Local` write, compose the scratch at
   weight 1. Whole `.slnx` at 0 warnings (checked for `error S` as well as `error C`); Animation, Scene, Content,
   Rendering and Presentation with counts; allocation tests.

## 6. What the owner would see change [interp]

- **B633**: a player who changes sequence twice inside one fade window (land then run, a crouch tapped mid-transition)
  blends the two outgoing poses in the other order for at most ~0.2 s. Small, and only on those frames.
- **B634**: a first-person viewmodel cross-fading out of a melee or throwable draw, idle or swing keeps that sequence's
  local arm layer during the fade instead of dropping it. Not on any third-person player.
- **Cold `--tick` shots cannot show either**: a cold build has no transition queue, and both need one.

## 7. Cost [pred]

- **B633: nothing.** One `List.Reverse` fewer per entity with transitions.
- **B634: zero on players** (no player sequence is `STUDIO_LOCAL`, §3), so the roots-split instrument's per-root rebuild
  is unchanged for them: one extra `layer.Local > 0` test per layer, under 0.0001 ms per root. On a viewmodel mid-fade out
  of a local sequence: one bind seed (~70 bones), one extra local sample (~8 µs) and one dense compose (~3 µs), about
  **0.01-0.015 ms per such rebuild** — rare, and only the first-person viewmodel.

## 8. Result

- **Red first [meas]**: `BuildBlend_TwoSequencesFadingAtOnce_AccumulatesTheNewerFirst` got `[1, 2]`;
  `LayersFor_ALocalWireLayerWithALocalAutolayer_*` got `[(1, 0.5, 0, 0)]` (no local layer at all); the skeleton's
  `Build_AHalfWeightLayerWithALocalLayer_*` got 8 (predicted flat value 8, Valve 4) and
  `Build_ALockingLocalLayer_*` got 6.0000005 (predicted 6, Valve 0). The control
  `BuildBlend_ALocalMainSequenceWithALocalAutolayer_AccumulatesIt` and `…WithNothingLocal…` passed before and after.
- **Green after**, none of those tests edited. Sabotage by hand, each restored by the inverse edit:
  `fading.Reverse()` back → the B633 test alone; `Local = 0` in the builder → the local wire-layer test alone; the
  own-pose blend at weight 1 → `Build_AHalfWeightLayerWithALocalLayer_*` alone; the capture on the accumulator instead of
  the target → `Build_ALockingLocalLayer_*` alone; `skipThrough = index` → `Build_AZeroWeightLayerWithALocalLayer_*` alone.
- **What changed in the structure**: the old per-layer path composed onto `result` only and `CloseBrackets` solved on
  `result` only. Now every scope carries the pose it acts on (`OpenScope.On`), and a local scope carries its parent
  (`Into`). The per-bone compose is one method (`ComposeBone`) used by both the layer pass and the own-pose blend; the
  bind seed (`SeedBind`) and the sample overlay (`Overlay`) are one method each, used by the base and the own pose.
- **Timers**: the own pose's seed and overlay land in `seed` (the span already named "the rest seed and the base's copy or
  overlay"); its sample in `layer sampling`; its blend at the close in `compose`. On a player nothing changes, because no
  player sequence is local.
- **How often B633 applies [meas, proxy]**: `pose-layers D:/measure-f12/f12.dem 29700 30000`, extended on this branch,
  found **35** main-sequence changes within 0.2 s of the same entity's previous change, in 2,709 player-ticks. It is a
  proxy read off `FrameOf`, not the queue. The layer lists it prints show the two fading roots directly, newest first on
  the fix: for example entity 8 at tick 29756, `crouch_walk_PRIMARY` w 1 then `run_PRIMARY` w 0.684.
- **Shots [meas], and they show nothing yet.** Shots were taken with `--shot` under `run-exclusive.ps1`, with
  `TF2VIEW_CAMERA="-3955 -954 640 12 157"` on entity 8 and `TF2VIEW_SEEK_THROUGH` set to every tick from 29700 to the shot
  tick minus one. The base is `19efea71`, built from `git archive`.
  - **Before against after: 0 of 542,184 pixels differ at ticks 29756, 29760 and 29764.**
  - The after shot against a repeat of itself: 0.
  - Control: the after shot against a cold `--tick 29756` with no seek-through: 53,508 pixels differ (box x 407-720,
    y 37-341). So seeking through changes the frame, but **it is not shown that it fills the transition queue the way
    the probe's per-tick walk does.**
  - **Kill-transitions control, which comes back positive**: `pending.Transitions = []` was set temporarily in
    `EntityModels.Simulation.cs`, then the viewer was rebuilt in the same command and shot at 29756 with the same camera
    and seek-through. Against the after shot, **83,729 pixels differ**: with no transitions the soldier stands square,
    with them he is mid-stride. The edit was then reversed exactly. **So transitions do reach the drawn pose through
    seek-through.** The three zeros therefore mean that at most one fading root was in the drawn pose at those ticks.
    The probe's per-tick walk saw two at 29756, so the viewer's drawn moment and the probe's tick are not aligned. That
    is [interp]: interpolation lag is the likely cause, and it is not checked.
  - **Still unsettled**: a drawn frame with two fading roots, i.e. the visible B633 effect. Finding one needs the queue
    length as the draw used it, which no public instrument carries. `TF2VIEW_PLAY_SHOTS` needs `--measure`, which this
    task excluded.
  - Shots, masks and the diff script are in `D:/measure-f12/b633-b634/`.
- **Cost on the roots-split instrument [pred]**: unchanged for players. B633 removes one `List.Reverse` per entity
  with transitions. B634 adds one `Local` test per layer, under 0.0001 ms per root rebuild. A viewmodel fading out of a
  local sequence pays one more sample and two dense passes, about 0.01-0.015 ms on that rebuild.

## 9. Review batch (`fix/b633-b634-review-batch`, from merge `a1e2d14c`)

Every item below is [src] for the engine side; each test is synthetic.

- **Nested local scopes, depth two**: `Build_TwoNestedLocalScopes_BlendInnermostFirstAndReturnToTheAccumulator`. It
  passed first time. Two sabotages each reddened it alone: one own pose shared by every depth (`Scratch(0)`), and the
  target left on the inner pose at a close.
- **A locking parent whose subtree is all local**: `Build_ALockingLayerWhoseSubtreeIsAllLocal_SolvesAfterItsOwnPoseIsBlended`.
  It passed first time. Pushing the bracket above the own pose (so it pops first) reddened it alone, giving 8 against
  the expected 6.
- **A sparse local sample**: `Build_ASparseLocalSampleWithAWeightedUnnamedBone_PullsItHalfwayToBind`.
  - Engine arithmetic: `CalcAnimation` writes bind into every weighted bone without a track
    (`bone_setup.cpp:1110-1121`), and `SlerpBones` blends it at weight × per-bone weight. So the knee goes from 6 to 3
    and the tip's X stays 20.
  - The local path already did this; the test passed first time. Inverting the delta test, so a non-delta pose is
    seeded with identity, reddened it (X 15).
  - **Found while settling it, and not fixed here: the NON-local path diverges for the same reason.** A layer sampled
    at a whole frame comes back sparse (`StudioAnimation.PoseBetweenInto`: "the frame alone, sparse, when fraction is
    not above zero"). `ComposeBone` then touches only the named bones. The engine's `pos2` holds bind on every weighted
    unnamed bone and blends it in. At a fractional frame the sample is dense and the two agree, so the same layer
    behaves differently at a whole frame and a fractional one. **Needs a B number**; who reaches it is unmeasured.
- **A delta local parent seeds identity**: `Build_ADeltaLocalParent_SeedsItsUnnamedBonesWithIdentity`. Red first,
  with X 40 against 20 under the bind seed. §4 is corrected above.
- **IK-rule weight of a local layer**: Valve's value is the 1.0-derived one, independent of the parent's weight.
  - `AddLocalLayers` runs inside `if (CalcPoseSingle(…))`, which has no weight test (`:2436`).
  - Its `AccumulatePose` calls `AddDependencies( …, flWeight )` with the layer's own weight, which becomes
    `ikrule.flRuleWeight` (`:3317`).
  - So ours, `IkFor` at `layer.Weight`, is equal. Held by `LayersFor_ALocalWireLayerWithALocalAutolayer_*` at parent
    weights 0.5 and 0.1, where the child stays at 1.
  - At parent weight 0, Valve still adds the rules, and so does `BuildBlend`, which walks every listed layer. The
    skeleton's skip of that subtree is about the pose only.
- **Timers**: `AccumulatePartsTests.Build_WithALocalScopeAndASkippedOne_KeepsThePartsAddingUp`. It counts 2 samples and
  2 enumerators, the parts sum to the total, and the residual is not negative.
- **Fading roots keep their subtrees**: `BuildBlend_TwoFadingSequencesWithAutolayers_KeepEachSubtreeBehindItsRoot`
  (2, 0, 1, 0).
- **Tidy**:
  - `OpenScope` now carries `Locks` or an `OwnPose? Own` (parent pose, layer index, weight) instead of a whole
    `PoseLayer` and an `Into!`.
  - `STUDIO_LOCAL` is one public constant, `StudioSequenceFlags.Local`, the value of `StudioFlags.SequenceLocal`.
  - `sequence-flags` reads each model's sequence table once.
  - `VmtMaterial.IsDecalModulate` is inlined.
