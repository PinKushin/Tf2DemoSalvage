# What a bone-setup build does, ours against Valve's `SetupBones` (2026-10-31)

The question is about the `setup` column in the viewer's `moment cost` line. In
`docs/measurements/2026-10-30-flex-for-cost/01-A.txt:110` it reads:

    setup 0.7 (roots 0.666 over 10.4 built, eligible 0.58 over 5, slowest 0.204 peak 2.902;
               children 0.058 over 23.4 built; prepose 0 over 0, blend 0), ... anim 0.2 ...
    built 33.9 (simulate 0, corpses 0, draw 33.9, place 0); posed 33.6 of 1860.8 selected

The task: list the work our code does for each root it builds and each child it builds. For each
step, say whether Valve's `SetupBones` path does it too, does less, or does it differently, and
whether we repeat it.

This is a read only. Nothing was built, run or measured for this document. Valve's code comes from
`D:/measure-f12/engine-read-setupbones.md`, cited as `ER §n`, which quotes `F:/src/source-sdk-2013/src`
word for word. It also comes from files I read myself in that tree. `CA` is
`game/client/c_baseanimating.cpp`.

Every claim carries one of these evidence tags:

| tag | meaning |
|---|---|
| **[src]** | read from published source (ours or Valve's) |
| **[meas]** | measured: a number from the 01-A log |
| **[arith]** | arithmetic on measured numbers |
| **[interp]** | interpolated: inferred and not observed |
| **[pred]** | a prediction |

## 0. What the measurement already settles

- **No whole build is repeated within a frame. [meas]** The log has `built 33.9` (all of them `draw`,
  none in `simulate`) against `posed 33.6`. The particle line has `0 rebuilt after this frame's
  first`. So there is no mask thrash. The simulate-phase `Attach` call
  (`EntityModels.Simulation.cs:552`, `wearer.SetupBones(UsedByAnything)`) and the attachment queries
  (`EntityModels.Queries.cs:253`) do not cause a second build of a skeleton.
  - "Repeated" in this document therefore means work repeated *inside* one build, never a second
    build.
- **Per-build cost. [arith]**
  - A root build costs 0.666 / 10.4 = **64 µs**.
  - An eligible root (≥16 bones, no move parent, no ragdoll; in practice players) costs
    0.58 / 5 = **116 µs**.
  - A non-eligible root costs (0.666 − 0.58) / 5.4 = **16 µs**.
  - A child costs 0.058 / 23.4 = **2.5 µs**.
  - Roots are 92% of `setup`. Children are not where the time is.
- **These means probably include first-use costs. [interp]** `peak 2.902` against a mean slowest of
  0.204 means at least one rebuild in the window paid for a first use. Candidates:
  - `BonesOf` → `StudioBones.Read` for a new group (`PropModels.cs:2265`)
  - the first `IkRules` parse (`:2246`)
  - the `_parents`, `IkContext` and `IkLocks` lazy fields (`SkeletonPose.cs:634-638`, `:751-756`)
  - `_quatInterp` misses (`:1321`)
  - JIT

  So 64 µs and 116 µs are upper bounds on the steady-state cost (see
  `docs/memory/a-split-carries-its-first-use-builds.md`).
- **The roots' time has almost no internal split. [arith]**
  - `anim 0.2` is `SkeletonPose.AnimationTicks`, which times only the base-sequence `_animation` call
    (`SkeletonPose.cs:381-387`). It covers every build: roots and children, 33.9 of them.
  - **Untimed:**
    - the per-layer `_animation` call (`:897-898`)
    - `BuildBlend`, which runs inside the root's span through `BeforeBuild`
      (`AnimatingEntity.cs:280` → `EntityModels.Simulation.cs:619`)
    - the per-bone concatenate loop
    - `ReachWithIk`
  - At least 0.666 − 0.2 ≈ **0.47 ms of the roots' time has no split. [arith]** Everything below
    about how that 0.47 ms divides is **[interp]** until follow-up 1 is done.

## 1 and 2. Call trees, step by step, with the matching Valve step

### Per call, before any build (roots and children)

| # | ours [src] | what it computes or allocates | Valve [src] |
|---|---|---|---|
| A1 | `AnimatingEntity.SetupBones` (`AnimatingEntity.cs:260-270`): 2 × `Stopwatch.GetTimestamp`, then `BoneSetupMeter.Record` (2-3 `Interlocked.Add`) | instrument; no allocation | none (`VPROF_BUDGET` only, `ER §2`, CA:2798) |
| A2 | `BeforeBuild?.Invoke()` **ahead of every guard** (`:280`) → `BuildBlend`, which returns at once if `!pending.Ready` (`Simulation.cs:621`) | a delegate call and a flag test on a cache hit; the whole blend input on the first call of the frame | `StandardBlendingRules` runs only inside the miss branch (`ER §2`, CA:2911 → :2987) |
| A3 | first-touch-this-frame guard plus `LastBoneChangedTime` (`:302-322`) | integers | CA:2874-2886, same |
| A4 | readable-subset early-out (`:331`) | integers | CA:2911, same |
| A5 | `wanted = mask \| readable \| prev` (`:342`) | | CA:2924-2929, same |

### Per child build (merged item; 2.5 µs each [arith])

| # | ours [src] | allocates / recomputes | Valve [src] |
|---|---|---|---|
| C1 | `_merge.UpdateCache(parent._pose)`: `ReferenceEquals` early-out (`BoneMergeCache.cs:86`) | nothing on a hit | `CBoneMergeCache::UpdateCache` compares four pointers (`ER §9`, bone_merge_cache.cpp:2414 in ER) |
| C2 | `parent.SetupBones(FollowBoneSetupMask)`, a cache hit after the parent's first build (`AnimatingEntity.cs:404`) | A1-A4 on the parent | `MergeMatchingBones` → `m_pFollow->SetupBones(NULL,-1,mask,curtime)` (`ER §9`, :130) |
| C3 | `MergeMatchingBones` first calls **`SpreadOf`** (`BoneMergeCache.cs:242`) **unconditionally**. It loops over every pair and calls `_worn.FlagsOf(mine)` per pair. Only then does it test `IsEnabled(Debug)` (`:248`). | a whole loop over the pairs, purely for a diagnostic; `FlagsOf` is `_bones[bone].Flags` through `IReadOnlyList<StudioBone>`: an interface dispatch plus a struct copy | not present. Valve's copy loop is the only loop (`ER §9`) |
| C4 | the copy loop: `FlagsOf(mine)` again per pair, then `CopyTo` (`:250-282`) | a second `FlagsOf` per pair | same loop and the same mask test (`ER §9`) |
| C5 | `_pose.Build(...)`, the full root pipeline R2-R12 below on the item's own skeleton, skipping the merged bones in the loop | as below | `StandardBlendingRules` then `BuildTransformations` on the item; merged bones skipped (`ER §4`, CA:1523) |

A child does the same work as Valve. The only extra is C3 (a diagnostic loop) and the doubled
`FlagsOf` copies.

### Per root build (64 µs, players 116 µs [arith])

| # | ours [src] | allocates / recomputes | Valve [src] |
|---|---|---|---|
| R1 | **`BuildBlend`** (`Simulation.cs:619-663`), once a frame per entity, inside the root's timer: `AutoLayersFor` ×2, `+Transitions`, `LayersFor`, `AutoplayFor`, then `IkFor` for the main sequence and each layer | see R1a-R1d | the inputs of `StandardBlendingRules`, assembled inside `SetupBones` (`ER §3`, CA:1950-2018). Autolayers are reached by recursion in `AccumulatePose` (`ER §10`, bone_setup.cpp:2389-2455) |
| R1a | `LayersFor` builds a **new `List<PoseLayer>` every build** (`Layers.cs:141`) | 1 List allocation | Valve walks the existing `m_AnimOverlay` (`ER §6`, c_baseanimatingoverlay.cpp:294) |
| R1b | for each gesture, every build: `TranslateGesture` → `PlayerActivityTable.Translate(..., name => skinned.ForActivity(name) >= 0)` (`Layers.cs:351-358`), then `GestureSequence` → `SelectWeightedSequence` with a seed (`:346-348`). For a non-chest flinch, a second `SelectWeightedSequence` (`:314`) | **recomputed every build**; a closure allocation per call (`[interp]`: it captures `skinned`) | resolved **once**, when the gesture starts: `AddToGestureSlot` stores `m_nSequence` (`Layers.cs:340` cites `multiplayer_animstate.cpp:633`). The result is identical by construction: the roll is seeded on the gesture's start (D233) |
| R1c | `IkFor` → `skinned.BlendedAnimations(...)` returns a newly built collection for a plain sequence (`PropModels.cs:2058`, `[(chosen.Animation, 1f)]`) | 1 allocation per `IkFor` call; per player, 1 + layers | `Studio_SeqAnims` fills stack arrays |
| R1d | `posed.Layers` is reused (B574); `ReusedIkErrors` is reused | none | |
| R2 | `_animation(Sequence, ...)` = `SkinnedModel.LocalsInto` (`PropModels.cs:1901`): sequence lookup, grid `Locate` ×2, `ThreeWayInto`, then **for each of up to 3 corners, `PoseBetween`** (`:2173`) | see R2a-R2c | `AccumulatePose` → `CalcPoseSingle` (bone_setup.cpp:1919-2116, three-way blend under `anim_3wayblend`) → `CalcAnimation` per corner → `BlendBones` |
| R2a | `PoseBetween` runs `PoseOf(frame)` **and `PoseOf(frame+1)` as two complete passes** (`:2176`, `:2183`). Each pass is `StudioAnimation.PoseInto` (`StudioAnimation.cs:347`): it re-reads the descriptor and **re-walks the animation's bone chain** (`Chain`, `:383`), then runs `ReadBone` per animated bone. `ReadBone` takes `bones[bone]` **by value through `IReadOnlyList`** (`:389`) | 2 chain walks, 2 RLE walks per channel, and 2 Euler→quaternion conversions per animated bone; a struct copy per bone per pass | **one** walk per channel yields both keys: `ExtractAnimValue(frame, ..., v1, v2)` (bone_setup.cpp:413-415, 530). **One** `AngleQuaternion` when `angle1 == angle2` (:428-446). **No second read at all** for raw rotation or position (`RAWROT`/`RAWROT2`/`RAWPOS`, :379-391, :502-507), nor for un-animated channels (:393-404, :509-520) |
| R2b | the frame blend: `StudioPoseBlend.BlendInto` (`StudioBlendGrid.cs:572`). It **expands both sparse poses to the full skeleton** (`ExpandInto` ×2), then aligns and nlerps every bone, unanimated bones included | 3 passes over the full skeleton per corner; buffers reused (finding 88) | the blend is per animated channel, inside `CalcBoneQuaternion` (`QuaternionBlend`) and `CalcBonePosition` (lerp). Unanimated bones are copied from the base, not blended |
| R2c | the grid: up to 2 more `BlendInto` calls (`PropModels.cs:1977-1994`). Each **re-expands inputs that are already dense** (`ExpandInto` on a full list) before blending | 2 extra full-skeleton passes per grid blend | `BlendBones` takes dense `pos`/`q` arrays, with no expand step |
| R3 | `Accumulate` (`SkeletonPose.cs:835`): if there are layers or locks, **fill `_layered` from the rest pose for every bone** (`:844-848`), overlay the base (`:860-868`), then for each layer with weight > 0, **`_animation(...)` again** (`:897`, untimed) = R2 for the layer's sequence, then a per-bone slerp or delta add | per layer, a full R2. `foreach` over `IReadOnlyList<PoseLayer>` (`:875`) and over `IReadOnlyList<StudioBonePose>` (`:900`) **box an enumerator per build per layer** | `InitPose` (bone_setup.cpp:1779) seeds every bone once. Per layer, `AccumulatePose` → `CalcPoseSingle` + `SlerpBones` (`ER §10`). The count of samples is the same; the seeding is the same |
| R4 | `Adjust` (`:1109`), only when the model has controllers and the demo sent values: **a second fill from rest plus overlay** (`:1118-1130`). `foreach` over two interface lists (`:1124`, `:1134`) | a second densify pass; boxed enumerators | `CalcBoneAdj` works in place on the dense `pos`/`q` (bone_setup.cpp:2462, `ER §10`) |
| R5 | `SpinBarrel` (`:1073`), minigun and grenade launcher only: **a new `List<StudioBonePose>`** plus a copy of the pose every build (`:1082`); `foreach` over the interface (`:1085`) | 1 List allocation per build | `CTFMinigun::StandardBlendingRules` writes `q[m_iBarrelBone]` in place (`tf_weapon_minigun.cpp:1068`, as cited at `SkeletonPose.cs:401`) |
| R6 | `Array.Fill(_overrideOf, -1)` plus an index pass over the sparse pose (`:414-424`) | 1 full-skeleton pass | none (Valve's arrays are dense) |
| R7 | the per-bone loop (`:435-581`): `IsMarked`, mask test, `FromQuaternion`, concatenate onto the parent or the placement | none; the bone is read by `ref` from `_boneArray` | `BuildTransformations` loop (`ER §4`, loop opens CA:1515): `QuaternionMatrix`, then `ConcatTransforms` |
| R7a | for each bone, **`QuatInterp` and `Jiggle` each read `_bones[bone].Flags` through `IReadOnlyList<StudioBone>`** (`:1299`, `:1254`; `Jiggle` reads it only after its `JiggleSource` test passes) | **up to 2 struct copies per bone per build**. The loop already holds `ref readonly rest` (`:452`), so these copies are avoidable. The ~136-byte size is from the comment at `:437-443`, not re-measured | Valve reads `hdr->boneFlags(i)` (a pointer read) |
| R7b | a procedural bone is concatenated first, then **overwritten** by `QuatInterp` (`:570`) | 1 wasted concatenate per QUATINTERP bone | `CalcProceduralBone` runs **before** the concatenate and `continue`s (`ER §4`, CA:1527) |
| R7c | `Jiggle` calls **`StudioJiggleBones.Read(model, bone)` every build** (`:1261`), re-parsing the bone's procedure block from the bytes. It returns a struct, so nothing is allocated | per jiggle bone per build | `pbones[i].pProcedure()` is a pointer add (CA:1578) |
| R8 | `ReachWithIk` (`:627`): `IkContext.Solve`, then a re-concatenate pass from the lowest chain bone (`:662-680`). That pass uses `foreach` over `IkChains` and `Links` (`:651`, `:653`, boxed). `IsChainBone` is indexed | | `UpdateIKLocks`, `UpdateTargets`, `CalculateIKLocks`, `SolveDependencies` **before** `BuildTransformations` (`ER §2`, CA:2989-3000). Valve has no rebuild pass. **Done differently**, the documented choice at `SkeletonPose.cs:621-625` |
| R9 | duck-jump: `DuckJumpOffset != 0` shifts **every** bone (`:592-600`) | | `C_TFPlayer::BuildTransformations` shifts only bones in `boneMask` (c_tf_player.cpp:8797-8806) |
| R10 | `PlayerBoneScales.Head/Torso/Hands` (`:606-608`) | `scale == 1` returns at once (`PlayerBoneScales.cs:218`); otherwise a linear name search, and `Descendants` allocates a `List` and a `HashSet` | same early-out (c_tf_player.cpp:8484, 8650, 8717); `LookupBone` per call; `CUtlVector` per call. Equal |
| R11 | (attachments are not in this timer) | | `SetupBones_AttachmentHelper` is inside `SetupBones` when attachment bits are first wanted (`ER §5`, CA:2055) |
| R12 | (ragdoll) `Ragdoll` hook before the merge (`AnimatingEntity.cs:371-383`) | | CA:1473-1491, same order |

**Overrides read for this question** [src]:

- `C_BaseFlex::StandardBlendingRules` (c_baseflex.cpp:227-273). Its extra work is `#ifdef
  HL2_CLIENT_DLL`, so for TF it is the base class plus nothing.
- `C_BaseFlex::BuildTransformations` (c_baseflex.cpp:1163-1183). It sets flex weights from **bone flex
  drivers** before calling the base class. See P3.
- `C_TFPlayer::BuildTransformations` (c_tf_player.cpp:8760-8821): duck-jump, then
  head/torso/hand scales, then the meathook.
- `C_TFPlayer::ApplyBoneMatrixTransform` (c_tf_player.cpp:8473-8476) only forwards to the base class.
- `C_BasePlayer` declares no bone-setup override (header read; c_baseplayer.h:172 is
  `GetRagdollInitBoneArrays` only).
- Not read: `C_TFWearable` and the weapon classes' bone overrides. The minigun is cited from our own
  code comment, not re-read.

## 3. Where we differ from Valve

### More work than Valve (the result is the same)

| id | where | what | evidence |
|---|---|---|---|
| M1 | R2a | every frame-pair sample walks the chain and the RLE twice, and converts Euler angles twice even when both keys are equal. Raw and un-animated channels are read twice and blended with themselves | [src] both sides |
| M2 | R2b, R2c | expand-then-blend over the full skeleton, plus re-expansion of inputs that are already dense in every grid blend | [src] |
| M3 | R4, R6 | a second densify in `Adjust`; a fill and index pass for `_overrideOf` | [src] |
| M4 | R1b | gesture activity translation and the weighted roll are recomputed on every build. Valve resolves them once per gesture | [src]; same result by D233's seeding |
| M5 | R1a, R1c, R5, R1b | a `List` allocation per build in each of these places: `LayersFor`, `BlendedAnimations` per `IkFor`, `SpinBarrel`, and the `Translate` closure | [src] |
| M6 | R3, R4, R5, R8 | `foreach` over `IReadOnlyList` boxes an enumerator per build per loop. The file's own `IsChainBone` comment measured this pattern at 14 MB/s on f12 | [src]; the cost is [interp] |
| M7 | R7a, R2a, C3/C4 | struct copies through `IReadOnlyList<StudioBone>` on per-bone paths | [src] |
| M8 | R7b, R7c | a wasted concatenate before QUATINTERP; the jiggle parameters are re-parsed every build | [src] |
| M9 | C3 | the diagnostic `SpreadOf` loop runs whether or not Debug logging is on | [src] |
| M10 | A1 | the meter's timestamps and interlocked adds (the instrument, kept on purpose) | [src] |

### Less than Valve, or different: proposed B candidates

These are proposed, to be numbered after B613; the lead assigns the numbers. **None is filed in
`docs/RISKS.md` by this document.** Already-kept divergences are not re-filed: D131 (transitions
built in simulate) and B584 (a gesture's variant re-rolled on restart).

- **P1. Frame-blend threshold. [src] both sides.**
  - Valve blends two keyframes only when `s > 0.001f`, for rotation (bone_setup.cpp:408) and for
    position (:525). At or below that it reads the single frame.
  - Ours blends whenever `fraction > 0` (`PropModels.cs:2178`).
  - For 0 < s ≤ 0.001 we draw a blend Valve does not. The visible difference is at most a thousandth
    of one keyframe step.
  - Fixing it is a parity change, not a performance item, though it also saves a second read in that
    band.
- **P2. Jiggle bones on the wrong clock. [src] both sides.**
  - Valve passes `gpGlobals->realtime` (CA:1586). Ours passes demo time (`SkeletonPose.cs:1270`,
    `(float)currentTime`).
  - `JiggleBones.Build` integrates `currentTime - LastUpdate` (`JiggleBones.cs:243`) and re-seeds after
    a 0.5 s gap (`:233`).
  - **Observable difference [interp]:**
    - While paused, Valve's springs keep integrating real time and settle. Ours stop.
    - At `host_timescale`/demo speed ≠ 1, Valve's springs run at wall-clock speed. Ours run at demo
      speed.
    - A backward seek: Valve's realtime never runs backward, so the next step is just a short forward
      step. Ours takes a negative step, which is clamped to 0.001 s (`JiggleBones.cs:121-122`), and
      can re-seed.
  - Check `docs/memory/a-paused-frame-is-two-moments.md` before fixing, in case the paused case is
    already ruled on.
- **P3. IK on a ragdoll: unverified.**
  - Valve runs IK only when `m_pIk && !IsRagdoll()` (CA:2990-2991, read directly: `// don't calculate IK on ragdolls`).
  - Ours: `ReachWithIk` has no ragdoll test (`SkeletonPose.cs:627-681`). `IkContext.Solve` is not
    passed `alreadyWritten` (`:640`), so it would overwrite bones the ragdoll drove.
  - The divergence exists **only if** a corpse's `SkeletonPose` ever has `IkChains.Count > 0` **and**
    `IkErrors.Count > 0` while `AnimatingEntity.Ragdoll` is set. That depends on whether a corpse prop
    passes through the simulate path at `EntityModels.Simulation.cs:374` and gets a `BuildBlend`.
  - The check: a probe that reports, for each corpse entity, `Pose.IkChains.Count`,
    `Pose.IkErrors.Count` and `SolvedChains` after a draw.
- **P4. Bone flex drivers are not implemented. [src]**
  - `C_BaseFlex::BuildTransformations` (c_baseflex.cpp:1163-1183) drives flex controllers from local
    bone positions.
  - Nothing in `managed/` reads `mstudioboneflexdriver_t`. It already appears in the generated
    not-handled list (`docs/SDK-COVERAGE.md:49`), so this is tracked as coverage.
  - The open question is content: does any TF2 model declare a bone flex driver (studiohdr2)? Not
    measured. File it only if one does.
- **P5. Duck-jump ignores the mask. [src]** Ours shifts every bone (R9); Valve shifts only bones in
  `boneMask`. With our `UsedByAnything` mask this differs only for bones with no use flags. Those
  slots are not written by the build, so the shift lands on stale data nobody draws.
  - **[interp]:** inert, but it is a different shape.
- **P6 (note, not a candidate): the draw-time mask.**
  - Ours always asks for `UsedByAnything` (`EntityModels.Instances.cs:427-434`).
  - The engine's draw call into `SetupBones` (engine `modelrender`) is not in the SDK. Whether it asks
    for `BONE_USED_BY_VERTEX_AT_LOD(lod)` cannot be settled from published source; it needs the
    decompiler.
  - It matters for cost only if LOD0's vertex mask is much narrower than "anything".

## 4. Parity-safe reductions, ranked: all **[pred]**

Each item below leaves Valve's result unchanged. The ranking is bounded by what is measured. **Only
`anim 0.2` (the base-sequence half of R2, all 33.9 builds) is measured directly.** The rest of the
roots' time, 0.47 ms, is unsplit, so items that touch it carry an assumption. The assumption is
stated in each item, and follow-up 1 is what would replace these predictions with observations.

1. **Single-pass frame-pair decode (fixes M1).** Walk the chain once per corner. Read v1 and v2 per
   channel in one RLE walk. Do one `AngleQuaternion` when the keys are equal. Do no second read for
   raw or un-animated channels. Write directly into the dense corner buffer, which also removes R2b's
   two expands.
   - **Prediction:** 30-50% of sampling cost.
   - On the measured base half alone that is **0.06-0.10 ms per rebuild**.
   - If layer sampling (R3, untimed) costs about the same as base sampling, **0.12-0.20 ms**. That
     assumes 2-3 layers per player at a similar cost per sample, which is **[interp]**.
2. **Keep the pose dense end to end (fixes M2, M3).** Corners land in dense buffers, so the grid
   `BlendInto` does not re-expand. `Adjust` composes in place on `_layered`. `_overrideOf` goes away,
   because the loop reads the dense pose by index.
   - **Prediction:** 0.03-0.08 ms per rebuild. These are full-skeleton passes: about 6-10 of them per
     player build at about 78 bones, which are struct writes of a 32-byte `StudioBonePose`.
     **[interp]**
3. **Resolve a gesture once, at its start (fixes M4).** Cache the translated activity and the rolled
   sequence on the gesture (or in a per-entity cache keyed by `(activity, StartedSeconds)`), as
   `AddToGestureSlot` stores `m_nSequence`. The result is identical because the roll is already
   seeded on the start (D233).
   - **Prediction:** 0.01-0.04 ms, only on frames where players hold gestures. **[interp]**
4. **Remove per-build allocations and boxing (fixes M5, M6).** Reuse the `LayersFor` list. Write
   `SpinBarrel` into a reused array. Return a span or reused buffer from `BlendedAnimations` in
   `IkFor`. Replace the `Translate` lambda with a cached delegate. Index the `foreach` loops in
   `Accumulate`, `Adjust`, `SpinBarrel` and `ReachWithIk`.
   - **Prediction:** under 0.01-0.02 ms of direct time, plus GC pressure. The 01-A window allocated
     39.9 GB since start [meas], so allocation is not free here.
5. **Read bones by `ref` (fixes M7).**
   - `QuatInterp` and `Jiggle` take `in StudioBone` or the flags from the loop's `rest`.
   - `ReadBone` takes `in StudioBone` from a `StudioBone[]`.
   - `BoneMergeCache` caches the worn model's flags as an `int[]` at pairing time.
   - **Prediction:** 0.005-0.02 ms.
6. **Cache the jiggle parameters per bone (fixes M8).** Do it as `_quatInterp` already does. Run
   `QuatInterp` before the concatenate and skip the concatenate when it applies, as
   `CalcProceduralBone` + `continue` does.
   - **Prediction:** under 0.005 ms (22 procedural bones on a map, per the `JiggleBones.cs:113` comment).
7. **Guard `SpreadOf` on `IsEnabled(Debug)` (fixes M9).**
   - **Prediction:** under 0.005 ms. It is bounded by the whole children column, 0.058 ms.

Not listed, because they would depart from Valve:

- skipping the blend for a ragdoll-driven body (Valve runs `StandardBlendingRules` for ragdolls too)
- narrowing the mask below what the engine asks for (P6 is open)
- caching a pose across frames for anything whose `LastBoneChangedTime` says it changed

## Follow-ups

1. **Split the roots' 0.47 ms before acting on the ranking.** Add carried timers, in the same shape
   as `AnimationTicks`, for each of these:
   - layer sampling (`SkeletonPose.cs:897`)
   - `BuildBlend` (`Simulation.cs:619`)
   - the per-bone loop (`:435-581`)
   - `ReachWithIk` (`:583`)

   Then take one `--measure` run. Exclude first uses (time the miss paths separately) so the peak
   does not inflate the means.
2. P1 and P2 go to the lead for numbering as RISKS entries. P3 needs the corpse probe first. P4 needs
   a content check first.
