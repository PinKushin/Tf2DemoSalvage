# Handoff — the ragdoll solver is transcribed and running; nothing draws with it yet

Written 2026-09-07, superseding the handoff of 2026-09-01 (launch options, the chase camera, the
frame floor — all merged and done).

`feat/ragdoll-constraint-group` AND `feat/ragdoll-bind-pose-frames` are both merged to `main` and
pushed. Gate green both times, both phases: twelve assemblies at or above floor (animation
111 → 210 → 215), UI 31/31 under the machine-wide lock each time.

**In progress, on `refactor/corpus-tests-that-measure-tf2`, uncommitted:** a probe,
`tools/Tf2DemoSalvage.Probe/Probes/TimelineCostProbe.cs`, written but not yet run or built. It prints
`DemoTimeline.Build`'s own `TimelinePhases` (carried, not recomputed — B243) per demo, because the
fast gate's cost is lopsided and nobody had the breakdown: `Tf2DemoSalvage.Corpus.Tests` takes 142 s
under `TF2DEMOSALVAGE_GCOR_ONLY=1`, and one test alone that asks for `z1800`'s timeline (8.96 MB, 4×
any other gcor demo) takes 68 s by itself. Twelve of the slowest-reporting tests all key off
`Corpus.Demo("z1800")` and block on the SAME shared `TimelineCache` entry — they are not twelve
redundant decodes, they are twelve waiters on one, so cutting test COUNT there saves nothing.

**Next step, not yet done:** build the probe (`dotnet build tools/Tf2DemoSalvage.Probe`), run
`timeline-cost z1800` plus a couple of the small gcor demos as controls, and read which column —
commands, schema, messages, entities, sampling, viewmodels, or the unnamed "rest" — actually holds
the 68 s before touching anything. Do not assume it is decode size scaling linearly; z1800 is ~4×
the largest other gcor demo by bytes but was taking ~34× as long per-test before this was measured,
which is disproportionate enough to be a real finding rather than noise.

**Separately, real D38 violations were found and are NOT yet converted:** at least
`PlayersAt_OnARealMatch_ProducesAReloadGesture`, `..._LeavesSomePlayersWithNoGesture`,
`..._ReportsGesturesFromTheTempEntityStream`, `AttachmentPoint_AcrossTheCorpus_IsUsedByRealItems`,
`PropsAt_OnARealMatch_CarriesWireLayersOnBuildingsAndNoneOnPlayers`, and
`OffHandViewmodelAt_AcrossARealMatch_OffersOnlyModelsThatAreOnScreen` assert what a REAL demo
contains — a claim about TF2, not about this parser (the same mistake `CorpusObserverModeTests` and
`CorpusRenderModeTests` were converted for). These should become synthetic tests in `Core.Tests` with
a `[Explicit]` census diagnostic left for the real-demo half, same pattern as those two conversions.
Not started — the timeline-cost measurement above was judged more likely to explain the wall-clock
number and was done first.

**The original next item, still not started:** handoff item 1 below, wiring `RagdollSimulation` into
`RagdollProps` so a corpse actually simulates instead of playing a death sequence. Owner is stepping
away for token reasons and will resume from Claude Desktop once the session's limit resets.

**Read `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md` before touching any of this.** It is
the reverse-engineering account, it is long, and it carries three wrong turns kept on purpose. The
code cites it by function address throughout.

---

## What is done

A TF2 corpse's physics is transcribed end to end. `src/vphysics` ships no source, so all of it was
read out of `vphysics.dll` with Ghidra; the published half (`ragdoll_shared.cpp`) supplied
construction and the bone read-back.

| piece | type | reads |
|---|---|---|
| per-body integration | `IvpIntegrator` | `FUN_180099a00`, `FUN_180099fc0` |
| quaternion delta / product / normalise | `IvpQuaternion` | `FUN_180071680`, `FUN_180070d60`, `FUN_180070c60` |
| gravity | `IvpGravity` | `FUN_180074c80`, the controller at `env+0x0` |
| environment and step order | `IvpEnvironment` | `FUN_18008a020` → `FUN_180082560` → `FUN_1800909d0` |
| one angular limit | `IvpAngularLimit`, `IvpJacobian` | `FUN_180036f80`, `FUN_1800372c0`, `FUN_180037bd0` |
| the three deflections | `IvpRagdollConstraint` | `FUN_180038620`, `FUN_180036b80` |
| relaxation driver | `IvpConstraintGroup` | `FUN_18003c780`, `FUN_18003c330`/`FUN_18003d240` |
| `.phy` → running bodies | `RagdollSimulation` | the seam; both halves |

**The one thing worth knowing if you read nothing else:** a ragdoll joint's three limits measure
three *different kinds of quantity*. Only the twist is an angle.

```
m = normalise( A[primary]_world + B[primary]_world )      the bisector, cached at geom+0x110
p = normalise( m × A[wider]_world )
q = B[wider]_world

twist = −atan2( q·p , q·(p×m) )      an ANGLE
swing = B[primary] · A[wider]        a SINE   — zero at the bind pose
cone  = B[primary] · A[primary]      a COSINE — one at the bind pose
```

and each block takes its bounds from a *different* axis than the one it measures, which reads like a
bug and is what `FUN_1800393d0` does:

| block | bounds |
|---|---|
| twist | `−hi`, `−lo` of the primary — negated and swapped to match its negated angle |
| cone | `range × ∓0.5` of the **wider** swing |
| swing | `lo`, `hi` of the **narrower** swing, straight through |

**The bind pose is the control that makes all of it checkable.** `constraintToReference` and
`constraintToAttached` exist precisely so each constraint axis maps to the same world vector through
either body at rest, so `0, 0, 1` is an exact prediction — and it caught a sign error on its first
run.

---

## What is NOT done, in the order it should be picked up

### 1. Nothing draws with it (the reason corpses still T-pose or play a death animation)

`RagdollBody.Build`, `RagdollBody.Pose` and `RagdollSimulation` have **no production caller**.
Corpses are posed today by a death sequence (`RagdollDeath.SequenceFor`) through
`RagdollProps` → `SceneProp`.

Wiring it means per-corpse simulation state that lives across ticks and resets on a seek — the same
shape as the persistent sample in D131, and the same hazard: a stepped timeline must equal a freshly
built one. `PersistentSampleTests` is the pattern to copy.

**This changes what the owner sees, so it needs their eyes, not a green suite**
(`docs/memory/state-the-assumptions-the-owner-can-falsify.md`).

### 2. ~~The bind-pose rotation~~ — DONE 2026-09-07, and it found a defect beside it

`RagdollBody` keeps the rotation now, as `AxesParentSpace`: the **columns** of
`Studio_CalcBoneToBoneTransform`'s matrix, because a frame is used as `matrix · e_k` and
`matrix3x4_t` is row-major. Both frames reach the joint in the same permutation, so a joint measures
its deflection from the bind pose and the bind pose reads `0`, `0`, `1` exactly.

**The defect found on the way: the CHILD is the reference body**, not the parent —
`CreateRagdollConstraint( childElement.pObject, ragdoll.list[parentIndex].pObject, … )`
(`ragdoll_shared.cpp:253`) against *"a constraint in the space of pReferenceObject"*
(`vphysics_interface.h:572`). This had them reversed, which was invisible while both frames were the
identity because every test started both bodies at the same orientation — the condition where
correct and broken predict the same observation. A two-bone fixture with one bone **turned a quarter
turn** is what separates them, and it is in `RagdollSkeletons`.

Also closed: a constraint joining a body to itself now makes no joint at all, matching the engine
nulling BOTH indices on *"Bogus constraint on ragdoll %s"*.

### 3. Three smaller stated departures, each documented at its site

- **Isotropic inertia.** `CreatePolyObject` seeds it from the hull; hulls are decoded but not
  integrated. Isotropic makes Euler's free-rotation terms vanish, so a wrong scalar changes joint
  stiffness, not direction.
- **The axis permutation is by declared range**, where the engine picks the twist as the axis whose
  rotation moves the two anchors most, weighted by inverse mass (needs hull inertia).
- **The rate gain is zero.** It would let a limit clamp the *predicted* deflection rather than the
  current one; at zero a joint resists a limit it has already broken instead of stopping short.
  It arrives in the per-sweep vector the driver builds and its provenance is unread.

### 4. The cone limit's sense — a real open question, not a gap

The cone measures a cosine and is bounded by half the wider swing's range in radians. Worked on the
demoman's own joints, the clamp fires when the cone angle is **small** and releases when it is large
— so it reads as a *minimum* bend of roughly 64–67° on his tighter joints, and never fires at all on
his 136° one. At the exact bind pose the axis is degenerate and retired, so a settled corpse is not
fighting it.

Two things were checked and came back negative: `useClockwiseRotations` is false everywhere
(`Defaults()` sets it, nothing in the SDK sets it, no `.phy` declares the key), and the range is
invariant under that flip anyway. **The transcription is pinned by test either way**; what is open
is whether a limit in that sense is physically what Valve intended. This is the first reading that
predicts what the owner describes — *"the ragdolls do funny things thats why they are fun"*.

### 5. Units — filed against `IvpEnvironment`, harmless today

`CPhysicsEnvironment::SetGravity` converts on the way in (inches → metres, Z-up → Y-up). Our
environment holds `(0, 0, −800)` unconverted and its tests bake that in, so the whole simulation
runs in Source units and axes. Nothing transcribed so far notices — the integrator carries no length
constant and the constraint solve is entirely angular. **It stops being harmless the moment hull
collision arrives**, since a hull's extents are metres.

---

## FPS, which is a separate open thread

Measured this session with a `PoseBuilds` counter added for it: `built ≈ posed × 1.1`. So the
readable-bone cache works, `_previousMask` does its job, and there is **no mask thrash** — the
remaining cost is genuine bone math. About 104 entities at ~62 µs each against TF2's implied ~5 µs:
a twelve-fold gap, ~620 ns/bone for a quaternion-to-matrix plus a 3×4 concatenate.

The candidate is contiguous `BoneAccessor` storage — **not** for cache locality (that reasoning was
wrong; jagged arrays allocated in a loop are adjacent) but for per-access overhead: `Bone()` and
`BoneForWrite()` each make two `ArgumentOutOfRangeException.ThrowIf*` guard calls plus a double
indirection, three times per bone. 123 call sites across 26 files.

---

## Tooling and process changes made this session

**`D:\ghidra-proj\scripts\DisasmWithData.java`** — disassembly with every memory operand resolved to
its four lanes, printed on the instruction that reads it. Built because two wrong conclusions in one
session were both made in decompiled C rather than in the disassembly:

- `DAT_1800eea1c` taken for π because its neighbour `DAT_1800eea18` genuinely is 2π. It is `1e-16`,
  and a whole conclusion was committed off it before being retracted.
- `uVar6` read as a dumped mask in one expression and a comparison result three lines later, because
  Ghidra reuses local names for unrelated SSA values.

**The rule: shape from the decompiler, identity from the instructions.** The trigger is a sentence
naming a `DAT_`/`_UNK_` symbol, or reaching for a value because it is *adjacent* to a known one —
adjacency is where dumping feels most redundant and is most likely wrong. See
`docs/memory/settle-a-constant-in-the-disassembly.md`.

**A sabotage that reddened nothing found a real defect**, which is the second time that memory has
paid (`docs/memory/a-sabotage-that-reddens-nothing-names-the-missing-input.md`). Reversing the cone
axis left every test green; closing the hole exposed that `IvpAngularLimit` had collapsed the
engine's **two** axis routines into one, on a note claiming their opposite sign conventions cancel.
They do not — with the rate gain at zero the two `θ` are identical and only the impulse sign
differs, so two of every joint's three axes were being driven backwards. The routine is now named at
every call site, never defaulted.

---

## Standing constraints that bit this session, so they are worth repeating

- **The UI phase of the gate exits 0 even when it fails.** `run-exclusive.ps1` does not propagate
  the inner exit code — measured: `Failed: 1, Passed: 30` with exit 0. Read the `Passed!`/`Failed!`
  line, never the status. And do not `| tail -6` it; that cut the only line naming the failing test.
- **A person at the keyboard is an input to a UI suite.** One failure this session was the owner
  pressing space. Re-run before investigating, and say which of the two you are reporting.
- **Back-to-back pushes to `main` cancel each other's CI Test run** (`concurrency:
  cancel-in-progress`). Several Test runs were cancelled tonight and never completed; branch pushes
  trigger nothing, so batch main pushes and watch by SHA.
- **Never run Ghidra while the UI suite has the desktop** — it is timing-sensitive and headless
  analysis is CPU-heavy.

---

## Where to look

| question | file |
|---|---|
| how any of the physics was worked out, and what was got wrong | `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md` |
| what a `.phy` holds, measured over all 4,755 the game ships | the `ragdoll-constraints` probe |
| what a corpse's joints and bodies are | `RagdollBody`, `docs/RISKS.md` B58 |
| why the project is built this way | `docs/DECISIONS.md` — D142, D143, D146, D147 |
