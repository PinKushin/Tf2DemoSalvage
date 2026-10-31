# Read 2026-10-31: the IK-lock bracket, ours against Valve's

Investigation only. Nothing in production was edited, no viewer was launched, nothing was `--measure`d. It follows
`2026-10-31-accumulate-parts.md` (Verdict item 1), which measured **capture 0.008 + solve 0.016 = 0.024 ms per root
rebuild**, 37% of the 0.065 ms accumulate remainder, and could not say how many brackets run or whether Valve does the
same work.

Evidence tags: **[src]** read from published source (SDK at `F:/src/source-sdk-2013/src`, ours at this commit);
**[meas]** measured, with the instrument named; **[arith]** arithmetic on [src]/[meas] inputs; **[interp]**
interpolated, not checked; **[pred]** a prediction.

## Headline: we do NOT solve per layer where Valve solves once

Valve and we bracket at the same granularity.

- **Valve: one lock bracket per accumulated sequence.** `CBoneSetup::AccumulatePose` (`public/bone_setup.cpp:2393`)
  declares a fresh `CIKContext seq_ik;` (`:2424`). It captures under `if (seqdesc.numiklocks)` (`:2425-2429`) and
  solves under the same test after `AddSequenceLayers` (`:2451-2454`). Every main sequence, autolayer, transition, overlay
  and autoplay sequence reaches the engine through `AccumulatePose`, so each locking sequence has its own bracket. [src]
- **Ours: one bracket per `PoseLayer` that carries locks, plus the main sequence's.** See
  `SkeletonPose.Accumulate`, `managed/Tf2DemoSalvage.Animation/Animating/SkeletonPose.cs`:
  - the base: `:943-952` captures and `:974-979` solves;
  - each layer: `:1003-1010` captures and `:1113-1118` solves.

  A `PoseLayer` is one accumulated sequence, so the granularity is the same as Valve's. [src]
- **The IK rules are a separate mechanism, and their split matches too.** Valve calls `AddDependencies` per
  `AccumulatePose` (`:2444-2447`) and `SolveDependencies` once per `SetupBones`
  (`game/client/c_baseanimating.cpp`, the `if ( m_pIk && !IsRagdoll() )` block after `StandardBlendingRules`). We gather
  the rules per layer in `BuildBlend`'s `IkFor` loop (`managed/Tf2DemoSalvage.Scene/EntityModels.Simulation.cs:654-658`)
  and solve them once in `ReachWithIk` (`SkeletonPose.cs:712`). [src]

**The two strongest divergences are behavioural, not matters of cost.**

- **D3 is certain [src]:** a gesture's or a fading transition's autolayers are never accumulated, and neither are their
  lock brackets. 55 of scout's 102 locking sequences have autolayers [meas].
- **D1:** Valve's `SolveLock` does not check whether `Studio_SolveIK` succeeded. It always slams the captured rotation
  onto the effector and rebuilds the three bones. Ours skips both when the solve fails.
  - How visible D1 is depends on the unmeasured failure rate (§4).
  - **[interp]** It is the bigger of the two only if a large share of locks fail, for example on standing legs.

## 1. Our bracket, step by step

### When it runs, per rebuild [src]

- **Base bracket**: `Locks.Count > 0 && IkChains.Count > 0` (`SkeletonPose.cs:915`). `Locks` is set from the main
  sequence, and set to empty for a scaled model (`Simulation.cs:383`).
  - **No TF2 main movement sequence carries a lock.** `stand_*`, `run_*`, `crouch_*`, `swim_*`, `airwalk_*`,
    `Jump_*` and `taunt0x` all have `numiklocks = 0`. [meas: a seqdesc census of `models/player/scout_animations.mdl`
    (377 sequences) through the `game-file` probe, parsed by hand; interp for the other classes]
  - So on a player the base bracket does not run in practice.
- **Layer bracket**: every `PoseLayer` with `Weight > 0` (`:987`) and `Locks` non-empty (`:999`). Where the layers come
  from (`BuildBlend`, `Simulation.cs:619-647`):
  - **Autolayers of the main sequence.** `AutoLayersFor` (`EntityModels.Autoplay.cs:83`) carries their locks (`:227`).
    This is where nearly every bracket comes from:
    - `stand_PRIMARY` → `PRIMARY_aimmatrix_idle` and `layer_idleNoise`, both locking;
    - `run_PRIMARY` → `PRIMARY_aimmatrix_run` and `PRIMARY_aimmatrix_runS`, both locking and pose-windowed (`STUDIO_AL_POSE|SPLINE`);
    - `stand_MELEE` → `MELEE_aimmatrix_idle` only.

    [meas: same census]
  - **Wire layers and gestures** (`EntityModels.Layers.cs:174-182`, `:433-445`). For example `jumpland_*`, `AttackStand_*`
    and `PRIMARY_reload_*` all lock with 2 locks each. [meas: same census]
  - **Transitions** (`EntityModels.Autoplay.cs:519-527`): the fading sequence's own locks. Main sequences have none.
  - **Autoplay** (`EntityModels.Autoplay.cs:328-338`): none on players.
- **The number of brackets per rebuild was not measured.**
  - **[interp]** f12 tick 30000 has about 5 eligible players, each with 1-2 locking aim/idle layers and sometimes a
    locking gesture, so **about 5-12 brackets per rebuild**.
  - **[meas]** For a different scope: `bone-flags D:/measure-f12/f12.dem 30000`, one build pass over **every** player
    at that tick (not only the drawn ones), printed `IK locks APPLIED on the pose path: 60` and `IK locks that MOVED an
    effector: 60, furthest 4 units`.
  - **That probe cannot give brackets per rebuild:**
    - it covers every player;
    - `Applied` counts only successful solves (see D1);
    - the counter accumulates over each entity's lifetime.

### What each step does, per lock [src], counts on the measured skeleton

Every lock found pins a foot. Every locking TF2 sequence declares 2 locks (B310). Chains 2 and 3 are the feet:

| class | right-foot chain | left-foot chain | shared ancestor |
|---|---|---|---|
| scout | 0→14→16→18 | 0→13→15→17 | bone 0 only |
| heavy | 0→14→16→30 | 0→13→15→29 | bone 0 only |
| medic | 0→14→16→35 | 0→13→15→34 | bone 0 only |

[meas: `mstudioikchain_t`/`mstudioiklink_t` and `mstudiobone_t.parent` read by hand through the `game-file` probe for
`scout.mdl`, `heavy.mdl` and `medic.mdl`]

- **`IkLocks.Capture`** (`managed/Tf2DemoSalvage.Animation/Animating/IkLocks.cs:118-153`):
  - one `BoneChain.Reset` (`:134`, `Array.Clear` of a `bool[bones]`);
  - per lock, `EffectorOf`, then `BoneChain.Build(foot, pose)` (`:147`);
  - then a `Decompose`, which is `ToQuaternion` (`:149`).
  - Per bracket that is **7 bone concatenations** (4 for the first foot, 3 for the second, because bone 0 is memoised)
    and 2 decompositions. [arith]
- **`BoneChain.Build`** (`BoneChain.cs:112-168`), per call that is not memoised:
  - a `stackalloc int[128]`, which is zeroed because the Animation assembly has no `SkipLocalsInit`;
  - per bone: `Stated` (`:190-206`), which reads `pose[bone]` twice through `IReadOnlyList<StudioBonePose>` (an
    interface call and a 32-byte copy each), then `FromQuaternion`, then `Concatenate` (or a copy at the root).
- **`IkLocks.Solve`** (`IkLocks.cs:185-265`):
  - one `Reset` (`:197`);
  - per lock:
    - `EffectorOf` again;
    - `Build(foot)` (`:210`; 4 bones for the first lock, 3 for the second);
    - `Build(link0)` and `Build(link1)` (`:213-214`), both memoised no-ops;
    - `IkContext.SolveChain` (`:221`, `IkContext.cs:300-385`): 3 lengths, a straightness test, the two-bone solver and
      two `Align`s;
    - **`continue` if it fails** (`:221-224`).
  - On success:
    - `Compose` (`:228`);
    - 3 `Rebuild`s (`:232`, `:242`, `:243`), each an `Invert`, a `Concatenate` and a `ToQuaternion`;
    - a `StudioBones.Slerp` at `LocalRotationWeight` (`:234-240`, which is 0 on every TF2 lock), through
      `System.Numerics.Quaternion.Slerp`;
    - `Applied++`.
  - Then a **diagnostic** (`:253-263`): `Reset` and `Build(foot)` again (4 bones) to measure `travelled` for
    `Moved`/`FurthestMove`.
- **Per successful two-lock bracket the solve does 7 essential bone builds and 8 diagnostic ones** [arith]:
  - the essential 7 are 4 and 3, the same as Valve;
  - the 8 diagnostic builds are 4 each. The mid-loop `Reset` throws away bone 0, so lock 2's main build rebuilds it,
    and its own diagnostic rebuilds it again.
- **Allocation: none in steady state** [src]:
  - `_held` grows only;
  - `IkLocks`, `BoneChain` and `_parents` are created once per pose (`SkeletonPose.cs:823-828`);
  - `EffectorOf` returns `int?`, a struct.
  - Every list is read through an interface: `locks`, `chains`, `chains[i].Links` and `pose`.

## 2. Valve's matching steps

All in `public/bone_setup.cpp` unless named. Each one is [src].

| step | Valve | per |
|---|---|---|
| bracket open | `AccumulatePose` `:2424-2429`: `CIKContext seq_ik; if (seqdesc.numiklocks) { seq_ik.Init(…vec3_angle, vec3_origin…); seq_ik.AddSequenceLocks(seqdesc, pos, q); }` | accumulated sequence |
| capture | `AddSequenceLocks` `:3398`. Returns if `numikchains()==0` or `numiklocks==0`. `g_MatrixPool.Alloc()` (pooled, `:50-79`) and a fresh `CBoneBitList`. `m_ikLock.AddMultipleToTail` + `memset`. Per lock: **`if (!(boneFlags(bone) & m_boneMask)) continue;`**, `BuildBoneChain` (`:3468`, recursive and memoised, root `ConcatTransforms(rootxform=identity, …)`), `MatrixAngles(boneToWorld[bone], q, pos)`, and **`kneeDir` rotated by link 0's matrix if the chain states one; `kneePos` is never set and stays memset 0** | lock |
| sequence applied | `:2431-2441` `CalcPoseSingle` (`:1922`) → `AddLocalLayers` → `SlerpBones`. `CalcPoseSingle` returning false skips only the slerp, not the bracket | — |
| rules | `:2444-2447` `pIKContext->AddDependencies(…)` into the entity's persistent `m_pIk`, not `seq_ik` | sequence |
| nested sequences | `:2449` `AddSequenceLayers` (`:2125`) → `AccumulatePose` for each non-local autolayer, **inside this sequence's bracket**, each with its own bracket | autolayer |
| bracket close | `:2451-2454` `SolveSequenceLocks` (`:4295`): a second `g_MatrixPool.Alloc()` and a fresh `CBoneBitList` shared across that sequence's locks, then `SolveLock` per lock | sequence |
| solve | `SolveLock` `:4402-4451`: mask test (`:4415`), `BuildBoneChain` (`:4419`), position blend (`:4428`), **`Studio_SolveIK(…)` with its return ignored** (`:4431-4438`; the captured-kneeDir overload when `m_ikLock[i].kneeDir` is non-zero), slam (`:4441-4442`), `SolveBone` ×3 (`:4446`, `:4449`, `:4450`) and `QuaternionSlerp(q[bone], q2, flLocalQWeight)` (`:4447`). No re-walk after the solve | lock |
| rules solved | `SolveDependencies` (`:4046`), once per `SetupBones` | frame |

**When each part is skipped** [src]:

- **The lock bracket depends only on `seqdesc.numiklocks`** (`:2425`, `:2451`) and, inside, on `numikchains() == 0`
  and the per-lock bone-mask test.
  - It does **not** consult `m_pIk`, `IsModelScaled`, `ENTCLIENTFLAG_DONTUSEIK` or `IsRagdoll`.
  - `seq_ik` is a local of `AccumulatePose` and never touches `m_pIk`.
- **`m_pIk`, the rules context, is gated by `C_BaseAnimating::SetupBones`.**
  - It is allocated only if all of these hold: `!IsModelScaled()`, `hdr->numikchains() > 0`, and
    `!(m_EntClientFlags & ENTCLIENTFLAG_DONTUSEIK)`. A scaled model has it deleted.
  - `UpdateTargets` and `SolveDependencies` run only `if ( m_pIk && !IsRagdoll() )`.
  - `IsRagdoll()` is `m_pRagdoll && (m_nRenderFX == kRenderFxRagdoll)` (`c_baseanimating.cpp:4499-4502`).
  - **Not resolved:** where `ENTCLIENTFLAG_DONTUSEIK` is set. clangd has no index for the SDK here, and a text search
    for the macro was refused by the C/C++ hook. It gates only `m_pIk`, so it cannot affect the lock bracket either way.
- **Autoplay sequences** run inside `CalcAutoplaySequences(pos, q, t, &auto_ik)` (`:4457`).
  - This is a **separate** `auto_ik` context made in `StandardBlendingRules`, with `AddAutoplayLocks` and
    `SolveAutoplayLocks` around the loop.
  - Each autoplay `AccumulatePose` adds its rules to `auto_ik`, not to `m_pIk`.
  - Players have 0 autoplay locks. [meas: `numlocalikautoplaylocks` header field of scout.mdl, heavy.mdl, medic.mdl]
- **`CalcPoseSingle` and `Studio_SeqAnims`** (`:5441`) have no lock or IK-context logic. They choose and weight
  animations only. `C_TFPlayer::BuildTransformations` (`game/client/tf/c_tf_player.cpp:8760-8821`) and
  `C_TFRagdoll::BuildTransformations` (`:8827-8847`) run after the pose is built: duck-jump offset and head/torso/hand
  scales. Neither touches locks.
- **Valve allocates per locking bracket and we do not.**
  - `m_target` is `CUtlVectorFixed<CIKTarget, 12>`, which is inline.
  - But `Init` does `m_ikChainRule.SetSize(numikchains)`, and `AddSequenceLocks` grows `m_ikLock`. Both are heap
    `CUtlVector`s, freed when `seq_ik` leaves scope.
  - The matrix buffers are pooled.

## 3. Where we do more, and where we diverge

### More work than Valve for the same result [src][arith]

- **M1: the diagnostic re-walk.** `IkLocks.cs:253-263`: after every successful lock, a `Reset` and a 4-bone `Build`
  to compute `travelled`. It costs **8 of the 15 bone builds** in a successful two-lock solve. Valve does nothing here.
- **M2: the interface pose read.** `Stated` reads `pose[bone]` twice through `IReadOnlyList` per concatenated bone,
  although every caller passes a `StudioBonePose[]` (`result`). Valve indexes `pos[]`/`q[]` directly.
- **M3: a zeroed 512-byte `stackalloc`** per non-memoised `Build` (`BoneChain.cs:130`), 6 per successful bracket.
  Valve's recursion has no buffer.
- **M4: `EffectorOf` and the `chains[...]`/`Links[...]` interface reads, done twice per lock** (capture and solve).
  Valve does the same lookups. This is constant overhead, not extra arithmetic.
- **Less than Valve:** no heap traffic per bracket (Valve: two `CUtlVector`s) and no root `ConcatTransforms` with the
  identity (we copy).
  - The copy is bit-identical: `x·1 + y·0 + z·0 + 0` equals `x` except for the sign of zero. [arith]

### Parity divergences: B candidates, unnumbered and not fixed

| # | divergence | ours | Valve | consequence |
|---|---|---|---|---|
| D1 | **A failed solve is not slammed.** Valve ignores `Studio_SolveIK`'s return in `SolveLock`, so a lock whose leg is too straight (`l3 > (l1+l2)·0.9998`), or whose `CIKSolver` fails, still gets the captured ROTATION slammed onto the foot and all three `SolveBone`s. Control: `SolveDependencies` DOES gate on the return (`bone_setup.cpp:4211`, `if (Studio_SolveIK(pchain, pChainResult->pos, boneToWorld ))`, with an `else` that decays the latch), so the omission in `SolveLock` is not a house style. The one unchecked `Studio_SolveIK` in `SolveDependencies` (`:4252`, "force angle (bad?)") sits inside `#if 0` … `#endif` (`:4236-4263`) and is compiled out [src] | `IkLocks.cs:221-224` `continue` | `bone_setup.cpp:4431-4450` | [interp] Visible: a foot on a near-straight leg keeps the sequence's orientation instead of the locked one. [src] Every TF2 lock has rotation weight 0, so Valve keeps the slammed rotation. [src] `Applied`/`Moved` count only successes, so the failure rate is invisible today |
| D2 | **A scaled model drops its lock brackets.** The comment at `Simulation.cs:376-383` cites B301 (link lengths on a resized model), but `AccumulatePose` never consults `IsModelScaled` or `m_pIk`; scale only gates `m_pIk` in `SetupBones` (`c_baseanimating.cpp:2946-2963`). `seq_ik` is initialised with `vec3_angle, vec3_origin` and no scale, on the local pose, before `BuildTransformations` [src] | `Simulation.cs:374`, `:383`, and the `IkChains.Count > 0` guards at `SkeletonPose.cs:915`, `:999` | `bone_setup.cpp:2424-2429`, `:2451-2454` | [interp] MvM giants and scaled bosses: feet slide where Valve pins them |
| D3 | **A gesture's or a transition's autolayers are never accumulated.** `LayersFor` and `TransitionsFor` add the sequence alone; only the main sequence goes through `AutoLayersFor` (`Simulation.cs:636-639`). Valve's `AccumulatePose` recurses `AddSequenceLayers` for EVERY sequence. Examples: `PRIMARY_reload_start` → autolayer 9; `MELEE_swing` → `layer_melee_swing` (2 locks) → autolayer 64; a fading `run_PRIMARY` → its two locking aim matrices. 55 of scout's 102 locking sequences have autolayers [meas]. Not found filed [meas: searched `docs/` for gesture/overlay/transition/fading within 80 characters of autolayer/AddSequenceLayers, either order; only unrelated hits] | `EntityModels.Layers.cs:174-182`, `:433-445`; `EntityModels.Autoplay.cs:519-527` | `bone_setup.cpp:2449` → `:2209` | [interp] Visible: missing layered motion on swings and reloads, missing aim matrix during a cross-fade, and the brackets that go with them. [arith] This is MORE work than we do today |
| D4 | **Bracket nesting.** Valve's solve runs after the sequence's local and non-local autolayers (`:2439`, `:2449`, `:2451`). Ours solves the base before any layer (`SkeletonPose.cs:974-979` runs before the loop at `:984`), and a locking autolayer's sub-autolayers are appended after its solve (`AutoLayersFor` recursion, `Autoplay.cs:233`) | as named | as named | Latent on scout. No main sequence locks, and no locking autolayer of a main sequence has autolayers of its own [meas: scout]. It becomes live when D3 is fixed: `layer_melee_swing` has both |
| D5 | **A zero-weight autolayer gets no bracket.** Inside its window, Valve calls `AccumulatePose` even when `layerWeight` computes to 0, for example `index == start` with `start != peak`. So the capture and solve still run and re-solve the legs | `Autoplay.cs:198-203`, `SkeletonPose.cs:987` | `AddSequenceLayers` → `AccumulatePose` at `:2209`, unguarded by weight; overlays are gated `fWeight > 0` (`C_BaseAnimatingOverlay::AccumulateLayers`, reader quote), and our overlay gate matches | [interp] Small. It needs an exact boundary value, and a weight-0 solve is near-identity |
| D6 | **The captured knee direction.** If a chain states `kneeDir`, `SolveLock` uses the direction captured BEFORE the sequence and `kneePos = (0,0,0)` (memset; `AddSequenceLocks` never sets it, unlike `AddAutoplayLocks`). `SolveChain` uses the model's `kneeDir` rotated by the CURRENT thigh and the current knee | `IkContext.cs:316-336` via `IkLocks.cs:221` | `:4431-4434` | Latent: every link of every chain has `kneeDir = 0` on scout, heavy and medic [meas], so both sides take the no-preference overload |
| D7 | **The bone-mask test per lock** (`AddSequenceLocks`, `SolveLock` `:4415`) | none in `IkLocks` | `if (!(boneFlags(bone) & m_boneMask))` | Heavy's foot effectors carry `0x1f00` and medic's `0xff00`: no bone-merge bit and only the low LOD bits [meas]. A low-LOD mask in Valve drops the lock. Whether our build ever uses a mask that excludes them was not checked |
| D8 | **Weight clamping.** `Math.Clamp` on both lock weights | `IkLocks.cs:217`, `:239` | unclamped | Latent: TF2 locks are 1 and 0 (B310) |
| D9 | **Slerp at t = 0.** Valve's `QuaternionSlerp` (`mathlib/mathlib_base.cpp:1605-1658`) gives `sclp = sin(ω)/sin(ω) = 1` and `sclq = 0`, so it returns `q[bone]` exactly, up to the sign of zero [arith]. Ours goes through `System.Numerics.Quaternion.Slerp`; whether that is bit-exact at `t = 0` was not checked [interp: possibly ≤ 1 ulp]. Valve aligns by `Σ(p−q)² > Σ(p+q)²` (`QuaternionAlign`, `:1509`), which reduces to `p·q < 0`, the same test as a dot-sign check except for float rounding at the boundary [arith] | `IkLocks.cs:236`, `StudioBones.cs:601-612` | as named | At most last-bit differences; affects every slerp we do, not only locks |
| D10 | **Autoplay rules go to `auto_ik` in Valve.** Ours fold every layer, autoplay included, into the one `IkErrors` | `Simulation.cs:656-658` | `StandardBlendingRules`, `CalcAutoplaySequences` | Rules, not locks. Players have no autoplay sequences, so latent |

## 4. Parity-safe reductions, ranked

**Every saving below is a prediction.** The two inputs that size them are unmeasured:

- **brackets per rebuild:** [interp] 5-12;
- **the lock-solve failure rate.**

**Arithmetic says a successful solve should cost about 4-5× a capture**: 15 bone builds, 2 solver calls, 6 rebuilds
and 2 slerps, against 7 builds and 2 decompositions. **The measured ratio is 2×** (0.016/0.008). Two
readings fit that one number, and nothing measured so far tells them apart [interp, both]:

- **(a) Many locks fail early.** The straightness test fails, for example on standing legs, so most solves are cheap.
  Under (a), D1 is widely visible and M1 is worth less, because the re-walk runs only on successes.
- **(b) Fixed per-bracket overhead dominates both halves.** Interface dispatch, struct copies and cache misses pull the
  ratio toward 1-2× even when every lock succeeds. The capture's own excess (last paragraph of this section) points
  this way. Under (b), D1 is rarely visible and M1 sits at the low end of its range.

**Measure these first.** One instrument settles both inputs: a bracket count beside `capture`/`solve` (the audit gap
`2026-10-31-accumulate-parts.md` already names), plus a failed-solve count beside `Applied`.

**D1 interacts with every saving below.** Fixing D1 ADDS a slam, three rebuilds and a slerp to each failed lock: about
0.002-0.006 ms a rebuild if most locks fail [pred]. The savings are given with and without it.

| rank | change | bit-identical? | predicted saving per rebuild [pred] |
|---|---|---|---|
| 1 | **M1: drop the diagnostic `Reset` + `Build`** (`IkLocks.cs:253-263`). Keep `Moved`/`FurthestMove` as a **carried** value: `travelled` from the solved end matrix (`_chain.Matrix(bone)` after the slam, Valve's `boneToWorld[bone]`), not a re-walk | **The pose: yes on TF2's topology** [arith]. The two locked chains share only bone 0, and no lock rewrites it. Without the mid-loop `Reset`, lock 2 reads bone 0 from the memo, built from the same `pose[0]` by the same arithmetic. This is also closer to Valve's shared `boneComputed` in general. **The counters: no.** `travelled` would measure to the solved matrix instead of the re-concatenated one, which differs by round-trip rounding. That is a B243 instrument change and needs a sentence in the record. Wants a differential over a corpus demo for the pose | **0.003-0.006**. D1 does not change this unless the re-walk is extended to failed locks, in which case 0.004-0.008 |
| 2 | **M2: `BoneChain.Build` takes `ReadOnlySpan<StudioBonePose>`** (or the array), and `Stated` reads once by `ref` | Yes by construction: the same values, read once | 0.0005-0.002 |
| 3 | **M3: no zeroing of the `stackalloc int[128]`**: `[SkipLocalsInit]` on `Build`, or a depth sized to the skeleton | Yes by construction: `upwards[d]` and `local` are written before they are read. Note that `SkipLocalsInit` needs `AllowUnsafeBlocks`, which the Animation project does not set today | 0.0003-0.001 |
| 4 | **M4: resolve each lock's chain once per bracket** (`EffectorOf` and `chains[locks[at].Chain]` at capture; reuse at solve), and index `Links` without the interface | Yes by construction | ≤ 0.0005 |
| — | **Not a reduction: skip the `Slerp` at `LocalRotationWeight == 0`.** It returns Valve's exact value (D9) but may change our current bits | Needs a differential; it belongs with D9 | ~0.0002 |
| last | **Reuse layer N's post-solve re-walk as layer N+1's capture** when nothing composes between them | Result-identical (same pose, same arithmetic) but **not Valve's structure**: Valve builds a fresh `CBoneBitList` per bracket. **Owner call**, and moot once M1 removes the re-walk | — |

**Total for ranks 1-4: about 0.004-0.010 ms of the 0.024** [pred]. That is roughly a sixth to two fifths of the bracket.
**The capture's own cost is not explained by the arithmetic.**

- **[arith]** At 5-12 brackets, 0.008 ms is 0.7-1.6 µs per capture, against about 0.3 µs for 7 concatenations.
- **[interp]** The remainder is plausibly interface dispatch, struct copies and cache misses on the chain arrays.
- A per-bracket count is what would turn this into a measurement.

## Method and instruments

- **The Valve side.**
  - Statements were read in the SDK at the outline lines that clangd's `list_symbols` reports (`bone_setup.cpp`,
    `c_baseanimating.cpp`, `c_tf_player.cpp`, `mathlib_base.cpp`).
  - Longer bodies came from the verbatim quotes in `D:/measure-f12/engine-read-iklock.md`. Its ranges include comment
    headers, so its `2389` for `AccumulatePose` is the function at `:2393`.
- **Our side**: read at this branch's base, `0979f203`.
- **Model data, the `game-file` probe.**
  - Probe: `tools/Tf2DemoSalvage.Probe`, `game-file <path> hex <n>`, run under `run-exclusive.ps1 -Shared`.
  - Files dumped: `scout.mdl`, `scout_animations.mdl`, `heavy.mdl`, `heavy_animations.mdl`, `medic.mdl`, `medic_animations.mdl`.
  - Parsed in PowerShell against the `studiohdr_t`, `mstudioseqdesc_t` (212 bytes), `mstudioautolayer_t` (24),
    `mstudioikchain_t` (16) and `mstudioiklink_t` (28) offsets.
  - **Control:** the scout sequence labels decoded as `ref`, `ragdoll`, `user_ref`, `ragdollspawn`. The locking set
    matched B310's examples: `PRIMARY_aimmatrix_*` and `AttackStand_PRIMARY`, all with 2 locks.
  - Counts: scout_animations 102 locking of 377 (55 with autolayers); heavy_animations 82 of 317 (23); medic_animations
    59 of 265 (27). Medic has a fifth chain, on bones 25-27.
- **Lock count**: `bone-flags D:/measure-f12/f12.dem 30000`, quoted in §1 with its limits.
