# Plan 2026-10-31: every accumulated sequence accumulates its own autolayers (B624, with B625 and B626)

Branch `fix/b624-sequence-autolayers`, from `9a43ea9e`. Written before any production edit. Evidence tags as in
`2026-10-31-iklock-bracket-read.md`: **[src]** read from published source, **[meas]** measured with the instrument named,
**[arith]**, **[interp]**, **[pred]**.

## 1. Valve's structure [src]

All in `src/public/bone_setup.cpp` unless named. Read for this plan.

- **`CBoneSetup::AccumulatePose` (`:2393-2455`) is the unit, and it is recursive.** In order:
  1. `flWeight = clamp( flWeight, 0.0f, 1.0f )` (`:2408`); `if ( sequence < 0 ) return;` (`:2410`). **No weight test.**
  2. `CIKContext seq_ik; if (seqdesc.numiklocks) { seq_ik.Init(…); seq_ik.AddSequenceLocks( seqdesc, pos, q ); }`
     (`:2424-2429`) — the bracket opens on the pose as it stands.
  3. `CalcPoseSingle` into `pos2`/`q2` (seeded by `InitPose` only for `STUDIO_LOCAL`), then `AddLocalLayers( pos2, q2, …,
     1.0, … )` and `SlerpBones( q, pos, …, q2, pos2, flWeight )` (`:2431-2441`).
  4. `pIKContext->AddDependencies( …, flWeight )` (`:2444-2447`).
  5. **`AddSequenceLayers( pos, q, seqdesc, sequence, cycle, flWeight, … )` (`:2449`)**.
  6. `if (seqdesc.numiklocks) seq_ik.SolveSequenceLocks( seqdesc, pos, q );` (`:2451-2454`) — the bracket closes AFTER
     step 5, so every nested accumulation sits inside it.
- **`AddSequenceLayers` (`:2125-2211`)**, per autolayer: skip `STUDIO_AL_LOCAL` (`:2140`); if `start != end`, the window
  (`index < start` and `index >= end` `continue`, `:2170-2173`), the ramp, spline, `XFADE`/`NOBLEND`/plain weight
  (`:2175-2200`) and the cycle remap (`:2202-2205`); then **`AccumulatePose( pos, q, iSequence, layerCycle, layerWeight,
  … )` with no weight guard (`:2209`)**. A ramp at 0, or below its start before the clamp, is still accumulated.
- **`AddLocalLayers` (`:2218-2286`)** is the same arithmetic over the `STUDIO_AL_LOCAL` entries, cycle-indexed only,
  returning at once unless the sequence carries `STUDIO_LOCAL`, and accumulating into the sequence's own `pos2` (`:2284`).
- **Every entry point is `AccumulatePose`**, so steps 2-6 apply to each:
  - the main sequence, at weight 1.0 onto `InitPose` (`c_baseanimating.cpp:1985-1986`);
  - each fading transition, `for (i = Count() - 2; i >= 0; i--) … AccumulatePose( pos, q, blend->m_nSequence, flCycle,
    blend->m_flWeight, … )` (`c_baseanimating.cpp:1848-1864`);
  - each overlay (TF2 gestures), gated `if (fWeight > 0)` and clamped to 1 (`c_baseanimatingoverlay.cpp:356-369`);
  - each autoplay sequence at 1.0 (`bone_setup.cpp:4474-4486`), inside the separate `auto_ik` (B631, not this change).
- **TF2 feeds gestures as overlays.** `CMultiPlayerAnimState::AddToGestureSlot` sets the sequence, `m_flWeight = 1.0f`
  and `m_nOrder = slot` (`multiplayer_animstate.cpp:616-718`, per `EntityModels.Layers.cs`'s cited reading), and
  `C_BaseAnimatingOverlay::AccumulateLayers` walks them in `m_nOrder` (`c_baseanimatingoverlay.cpp:329-369`).

## 2. Ours, at `9a43ea9e` [src]

- `EntityModels.Simulation.cs:619-663` `BuildBlend`: `AutoLayersFor(local: true)` then `AutoLayersFor(local: false)` for
  the MAIN sequence only (`:636`, `:639`), then `pending.Transitions` (`:641`), `LayersFor` (`:642`), `AutoplayFor`
  (`:645`), all appended to one flat `List<PoseLayer>`.
- `EntityModels.Autoplay.cs:83-244` `AutoLayersFor`: the window arithmetic, then `Math.Clamp` and **`if (layerWeight <=
  0f) continue;` (`:198-203`)**, a `PoseLayer` for the target and a recursion over the target's non-local autolayers
  (`:233`).
- `EntityModels.Autoplay.cs:519-527` (transitions), `EntityModels.Layers.cs:174-182` (wire layers) and `:433-445`
  (gestures), `EntityModels.Autoplay.cs:328-338` (autoplay): a `PoseLayer` for the sequence alone. **No recursion.**
- `SkeletonPose.cs:907-1122` `Accumulate`: the base bracket captures on the bind pose and **solves before any layer**
  (`:943-979`); each layer with `Weight <= 0` is skipped whole (`:987`); each other layer captures, composes and solves
  in one step (`:1003-1118`). There is no way for a bracket to contain a later layer.

## 3. The divergences this closes

| B | Valve | ours |
|---|---|---|
| **B624** | gestures, transitions, wire layers and autoplay sequences all run `AddSequenceLayers` | only the main sequence does |
| **B625** | a sequence's solve runs after its own autolayers (`:2449` then `:2451`) | the base solves before every layer; a locking autolayer solves before its own sub-autolayers |
| **B626** | an autolayer inside its window at weight 0 is accumulated, bracket included | `continue` on weight ≤ 0 (`Autoplay.cs:200`), and `SkeletonPose` skips weight-0 layers (`:987`) |

A consequence of B626 that is not cosmetic: a `STUDIO_AL_NOBLEND` child takes `layerWeight = s` whatever its parent
weighs (`:2195`), so dropping a weight-0 parent also drops a child that Valve draws at full weight.

## 4. The replacement (minimum, structural)

**One recursive builder that is `AccumulatePose`'s shape, used by every entry point; and a layer list that can say which
later layers sit inside a layer's bracket.**

- **`PoseLayer.Nested`** (new, default 0): how many of the immediately following layers belong to this layer's
  `AccumulatePose` — its non-local autolayers, recursively. The list stays flat and in Valve's accumulation order;
  `Nested` is the tree's shape written into it.
- **`SkeletonPose.BaseNested`** (new): the same for the main sequence, whose bracket is the base's.
- **Scene: `AccumulatePose(…)` in `EntityModels.Autoplay.cs`** appends the sequence's `PoseLayer`, then runs
  `AutoLayersFor(local: false)` for it, then writes `Nested`. `AutoLayersFor` calls it for each autolayer instead of
  adding a layer and recursing itself, and loses its `layerWeight <= 0` `continue` (the clamp stays: it is `:2408`).
  Transitions, wire layers, gestures and autoplay call it instead of `new PoseLayer(…)`. Each passes its own sampled
  frame (unchanged) and the cycle Valve hands `AddSequenceLayers` (the clamped cycle, not the `STUDIO_REALTIME` one,
  which `CalcPoseSingle` substitutes privately).
- **Skeleton: brackets nest.** A small stack of open brackets: a layer with locks captures, composes, and pushes
  (close index = its index + `Nested`); after each layer every bracket closing there is solved, innermost first. The
  base bracket is pushed first with close index `BaseNested - 1`. A weight-0 layer still opens and closes its bracket
  and skips only the sample and compose, which is result-identical to Valve because `SlerpBones` skips every bone at
  `s2 <= 0` (`bone_setup.cpp:1373` onward, quoted at `SkeletonPose.cs:884`).
- **The old shape goes**: the per-layer capture-compose-solve in one step and the base's early solve are removed, not
  kept beside the new path.
- **Timers (`RootSetupParts`)**: capture, sampling and compose keep their stamps. A solve that runs after a later layer
  is stamped around itself and added to `solve`. New work lands in the existing spans: the added layers' samples in
  `layer sampling`, their per-bone pass in `compose`, their brackets in `capture`/`solve`. No new counter is needed for
  the lead's measurement; a run before and after compares `over N` (samples per rebuild) and the sub-parts.

### Not ported, stated

- **`AddLocalLayers` for any sequence other than the main one.** The local pass composes into the sequence's own `pos2`
  BEFORE `SlerpBones` at `flWeight`; a flat list composes onto the accumulator, which is exact only at weight 1 with
  unit bone weights. That is why it is right for the main sequence (weight 1.0, `c_baseanimating.cpp:1986`) and why
  B294 already names it. Reproducing it needs a second accumulation buffer per nesting level. Who could reach it:
  `STUDIO_LOCAL` is set on 48 of 26,706 sequences in `tf2_misc_dir.vpk`, examples all `c_*_animations` viewmodel arms
  **[meas: `sequence-flags`, this branch]**; a player never plays one. A viewmodel cross-fading OUT of a local sequence
  would. Filed as a sibling, not fixed here.
- **`auto_ik` for autoplay rules** (B631) and the IK-rule weight of a weight-0 sequence: `AddDependencies` still runs at
  weight 0 (`:3253-3267`, no weight test), and ours adds nothing at zero influence
  (`IkRuleDependencyTests.IkFor_BeforeTheEnvelopeOrAtZeroInfluence_AddsNothing`). Rules are not locks; unchanged here.
- **B622 (failed solve slam)** is another agent's, in `IkLocks.cs`. This change does not edit `IkLocks.cs`.

### Neighbour found while reading, filed with this change

`MaintainSequenceTransitions` walks `m_animationQueue` from `Count() - 2` DOWN to 0 (`c_baseanimating.cpp:1848`), and
`CheckForSequenceChange` appends with `AddToTail` (`sequence_Transitioner.cpp:64`), so the NEWEST fading sequence is
accumulated first and the oldest last. Ours reverses to oldest first (`EntityModels.Autoplay.cs:530-532`, whose comment
says the engine "walks its queue from the front"). It matters only with two or more transitions in flight. Fixed here
only if it needs no more than removing the reverse and one conformance test; otherwise filed.

## 5. Which f12 players reach it [meas]

Instrument: the new `pose-layers` probe (`tools/Tf2DemoSalvage.Probe/Probes/PoseLayerProbe.cs`), which reads
`EntityModelSet.LayersOf` — the list `SkeletonPose` accumulates — over one `EntityModelSet` walking the range, so
transitions exist. Run on this branch before any production change, `pose-layers D:/measure-f12/f12.dem 29700 30000`:

- 2,709 player-ticks, 6,601 layers, 5,261 with locks; **1,396 layers whose sequence declares non-local autolayers, with
  0 layers nested under them.**
- The parents, by count: `run_PRIMARY` 280 and `crouch_walk_PRIMARY` 238 (fading transitions: their aim matrices are
  missing during every cross-fade), `ReloadStand_PRIMARY_loop` 157, `jump_float_PRIMARY` 154, `jump_start_PRIMARY` 140,
  `ReloadStand_PRIMARY` 123, `PRIMARY_reload_start` 113, `gesture_primary_help` 57, `PRIMARY_reload_end` 50,
  `ReloadStand_PRIMARY_end` 40, `Jump_Start_primary` 28, `airwalk_PRIMARY` 14, `PRIMARY_airwalk_reload_start` 2.
- Example at tick 30000, entity 7 at (-3615 -2147 782): gesture `ReloadStand_PRIMARY_loop` declares
  `layer_reload_standing_arms_loop`, window all zeros (so full weight, always), which is not accumulated today.
- **Control:** the main sequences' own autolayers ARE present (`stand_PRIMARY` → `PRIMARY_aimmatrix_idle` and
  `layer_idleNoise`; `run_PRIMARY` → `PRIMARY_aimmatrix_run`), so the instrument sees autolayers where the old code
  builds them.
- The scout census in the bracket read (55 of 102 locking sequences have autolayers; `MELEE_swing` →
  `layer_melee_swing` → autolayer 64) is not repeated; no melee swing fell in this window.

## 6. What the owner would see change [interp]

- **Reloads:** the arms layer of `ReloadStand_PRIMARY_loop` (and the other reload gestures above) is added on top of the
  gesture, so the reload motion changes in the arms. How much is unmeasured until the shots in §8.
- **Cross-fades:** for the 0.2-ish seconds a player leaves a run or crouch-walk, the outgoing sequence keeps its aim
  matrix instead of fading a body without one, so the upper body stops dipping toward the unaimed pose mid-transition.
- **Melee swings** (not in this window): `layer_melee_swing` and its own layer.
- **Feet:** each added locking layer brings its lock bracket, and brackets now enclose their children, so feet hold
  where a nested layer would have moved them.

## 7. Tests, in order

1. Conformance (synthetic, `Scene.Tests` beside the existing autolayer tests, red first): a gesture whose sequence
   declares an autolayer gets that autolayer accumulated right after it with Valve's weight; the same for a transition;
   ordering is Valve's (parent, its subtree, next root); a zero-weight autolayer is present at weight 0 and its NOBLEND
   child at `s`.
2. Skeleton (synthetic, `Animation.Tests`): a parent's lock bracket closes after its nested layer (pose differs from the
   flat order and equals the hand-computed nested order); the base bracket closes after `BaseNested` layers; a weight-0
   locking layer still runs its bracket.
3. Sabotage one of each; whole `.slnx` build at 0 warnings; Animation, Scene, Content, Rendering and Presentation tests
   with counts.

## 8. Before/after, and cost

- Shots: base `9a43ea9e` built from `git archive` in `D:/measure-f12/b624-base`, against this branch, same
  `TF2VIEW_CAMERA`, `--tick 30000`, on entity 7; pixel diff count. Saved under `D:/measure-f12/b624/`, not in the repo.
- **Cost [pred]:** about 0.52 extra parents per player-tick in the window above, mostly one autolayer each, so about
  2-3 more layers a rebuild over today's 12.7 samples. At the measured ~8 µs a sample and ~3 µs a compose
  (`2026-10-31-accumulate-parts.md`), plus ~2-5 µs per added lock bracket, **about 0.02-0.05 ms per root rebuild**,
  landing in `layer sampling` (most), `compose`, `capture` and `solve`. Not `--measure`d here.
