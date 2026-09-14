# Handoff — IVP's collision path, ported function by function against vphysics.dll (B369, D172)

Written 2026-09-14, superseding the handoff at `e47dc3f1` (same direction, earlier state).

**Branch `fix/b369-ivp-narrow-phase`, pushed.** The last full Animation run: 4682 total, 4681 passed, 1 skipped (the medic
medigun bone test, skipped before this work too). Solution build: zero warnings.

**Read `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md` first** — every port cites it by address. This session's
reading starts at *The broad phase* and runs to the end of the file.

## The direction, unchanged

D172: port ALL of IVP's collision path to parity and put it on the running path, replacing the invented
`IvpContact`/`IvpEnvironment` structure; then step 7 — delete TerrainDepth/TerrainReach and the compensators, run the
corpse-drop measurement, gate (`TF2DEMOSALVAGE_GCOR_ONLY=1 bash build/gate.sh`), merge. Valve's way, always (D89/D129/D131).

## Done — each pinned to the shipped binary called in process

| port | engine | probe | cases agreeing | fixture |
|---|---|---|---|---|
| `IvpOvTree` | `FUN_18009ecb0` insert, `FUN_18009efc0` removal and helpers | `vphysics-ov-tree` | 50,000 | 300 + 11 searched |
| `IvpRangeManager` | `FUN_1800a0420` policy 1, slots 1–2 | `vphysics-range` | 100,000 | 400 + 1 searched |
| `IvpLedgeTree` + `PhysicsHull.Tree` | polygon manager slot 4, `FUN_18007ada0`/`FUN_18007afb0` | `vphysics-ledge-tree` | 20,000 | 300 + 7 searched |
| `IvpObjectCache` | `FUN_180080a60` | `vphysics-object-cache` | 100,000, NaNs included | 400 + 20 searched |
| `IvpBroadPhase` | `FUN_180098880`, `FUN_180096eb0`, node filing and destructor | `vphysics-broad-phase` | 5,000 × 16 steps | 200 + 1 searched |
| `IvpPairMindists` | `FUN_180096680`, `FUN_1800975d0`, `FUN_180095fb0` | `vphysics-pair-mindists` | 5,000 × 6 steps | 200 + 1 searched |
| `IvpPairWatcher`, `IvpPairCreator` | `FUN_1800b5dd0`/`b6080`/`b5e80`/`b5fd0`, `FUN_1800a06f0`/`a07a0`/`a0690` | `vphysics-pair-watcher` | 5,000 × 6 steps | 200 |

Sabotage rounds (sonnet `sabotage-verifier`) have killed every non-equivalent mutant of every row, each survivor by a searched
case or a new lane; the equivalences are argued in findings 51. **One ordering is untested**: the watcher's destructor deleting its
mindists last first, which no lane can see while the mindist constructor's tails are detoured.

The two pair probes share `tools/Tf2DemoSalvage.Probe/Probes/VphysicsPairObjects.cs` (the fabricated objects, the detoured
constructor tails, the random draws). **A watcher probe must file each OV node before making a watcher**: a record keyed at
`1e20` in an empty min-list walks from slot `0xffff` and faults, a state the broad phase never reaches.

## Next, in order

1. **The larger mindist** — `FUN_1800b21f0` and everything it reaches is read (findings 51, *The larger mindist in full*):
   slot 7 `FUN_1800b2700` (a frozen minimize opens the ledge), slot 8 `FUN_1800b2460` (a collision on a hull's virtual face
   opens it), `FUN_1800b29b0` (the children refreshed beneath the opened side's ledge), `FUN_1800b28a0` (told its hull passed:
   collapse to plain exact past `DAT_18012d64c`, else refresh), `FUN_1800b23a0`, `FUN_1800b2250`, the delegator
   `FUN_1800b2320`/`b2300`/`b2860`, and `FUN_180097ce0`, `FUN_180097d60`, `FUN_180097e20`, `FUN_180098ef0`. **The port needs
   slots 7 and 8 as dispatch on the mindist**: today callers hand `invalidate` and `collide` in as delegates
   (`IvpMindistManager.MinimizeExact`/`RecheckEveryPsi`, `IvpMindistHull.BecomeExact`, `IvpMindistFire`), and
   `IvpMindistHull.HullPassed` throws for the recursive state. An oracle sketch: trees whose root is a hull ledge
   (`IvpLedgeTreeReplay.InnerWithLedge`) so `FUN_180096680` makes one; the exact tail `FUN_1800977f0` detoured to a recorder that
   records the event and calls the binary's own `FUN_180097ae0` — link exact without the minimize — so the mindist sits on the
   manager's `+0x10` list and its objects' `+0x40` lists that `FUN_180098dd0` unlinks (its queue slot `+0x8` stays `0xffff`, and
   with the cores' `+0x58` null nothing joins the rechecked vector); `FUN_180095ad0` and `FUN_18008ecb0` (twelve bytes of register
   saves each) detoured to recorders driven by lanes; then slot 7, slot 8 and a record's slot 1 (`FUN_180097f00`) called on it. The
   manager block needs `+0x10`, the vector at `+0x18` (capacity, `+0x1a` count, `+0x20` elements) and `+0x28`; the probe's own
   delegator needs slot 2 (nothing) and slot 3 (`−1`), as the watcher's has.

   **The design, chosen 2026-09-14 from a three-design, two-judge panel and the re-verified reading:**
   - **Stages, each committed green:** (1) **done:** `PhysicsLedge` gains optional TRAILING virtual-bit lists (header and edge bit 31),
     every tree node with a ledge carries its decoded `PhysicsLedge` (inner hulls included, through the one `ReadLedge`), and
     `LedgeNodeOffset` models a zero `+0x4` as no node; (2) **done:** `IvpMindist` unsealed with `virtual Freeze(manager, queue)` (slot 7,
     base = `Invalidate`, which is `FUN_180098dd0` + `FUN_180097ce0`) and `virtual Collide(Action<IvpMindist>)` (slot 8, base =
     the still-unported `FUN_18008ecb0` handed in); `IvpMindistManager.Recheck` and **`IvpMindistHull.BecomeExact` both dispatch
     through `Freeze`** — `FUN_1800977f0` calls the mindist's own `+0x38` at `180097914`, so a larger mindist frozen at birth runs
     its own slot 7 — and `IvpMindistFire.Handle` through `Collide`; `IIvpCollisionDelegator` gains slots 2 and 3 as default
     members (no-op, `−1`) so the watcher is untouched; (3) **done:** `IvpMindistHull.FileRecursive` for `FUN_180097d60`+`FUN_180097e20`:
     `SplitGap`'s split, `1e-10f` on a clear side, both records through `InstallAtNextPsi` — never `FileFar`; (4) **done:**
     `IvpRecursiveMindist : IvpMindist` with a nested child delegator mirroring the `+0xe0` object (child removal
     `IvpCollisionList.Remove`, slot 2 adds to its own `+0xfc` total and tails outward, slot 3 asks outward first) — **the limit
     reads the outermost total, never a child list's count**; `Delete` = children deleted last first then `-count` told, then
     the base; slots 7/8 and `FUN_1800b28a0` as read, `FUN_1800b29b0` as a call to the unchanged `IvpPairMindists.Refresh` with
     the open side as ROOT and the other as LEDGE; `IvpPairMindists.Construct` builds it for a ledge with children, and
     `IvpMindistHull.HullPassed` sends the recursive state to it (a flags test, as `FUN_180097f00` does, not a vtable call);
     (5) **done:** the oracle above (`vphysics-recursive-mindist`, 5,000 cases agree; fixture `Data/ivp-recursive-mindist.txt`),
     after the sabotage round.
   - **Not added:** a slot-6 `IsRecursive` member — `FUN_180028aa0` answers 1, but no ported caller reads slot 6.
   - **Slot 7's side choice reads each ledge's OWN node** (`ledge + ledge+0x4`, `tree.Node(LedgeNodeOffset)`), a null node's
     radius `1e15f`; not the found node, and not `Left`.
2. **Units — done (D173).** vphysics runs IVP in metres and converts once at `CPhysicsEnvironment`'s boundary, so every IVP
   port now does too: `IvpCollisionTolerance`, `IvpMindistHull`, `IvpPairScheduler`, the time-of-impact searches and
   `IvpRootFinder` were converted from inches with their tests. The running path (`IvpEnvironment`, `IvpContact`,
   `RagdollSimulation`) stays in Source units until it is replaced; the conversion belongs at the `CPhysicsEnvironment` and
   `CPhysicsObject` seam, nowhere inside the core.
3. **The running path — the real driver is found: `IvpEnvironment::IntegrateAwakeCores` (`180090bac`/`1800909d0`), read in
   full 2026-09-14.** `IvpEnvironment.Simulate()` (897 lines, `managed/.../IvpEnvironment.cs`) is a fully independent,
   invented solver — damping → push flush → gravity → `Constraints.Solve()` → an event-walk subdivided per collision
   (`Advance`) → once-per-PSI friction (`Rub`), on its own `IvpContact`/manifold types (1765 lines) — **wired to none of
   the ported core.** The engine's actual shape, read this session, call by call:
   - `IntegrateAwakeCores` first clears each core's `+0x260` slot (calling `FUN_180079120` when it named a still-current
     record — unread), then computes `dt = env+0x190 − env+0x188` (`NextPsi − Now`, item 1's own fields).
   - **For each active controller** (its flags' `0x2` bit clear): a bound/inverse-bound pair is built from `dt` (capped,
     never divided by a too-small step) and handed with a local stack min-list (`0x100` bytes, `IvpMinList`-shaped) to
     `FUN_180099a00` — **unread past its outline**: phantom/listener bound check (a vtable call at the controller's
     `+0x40`, the still-unported phantom controller HANDOFF already flags), `IvpCoreSpeedBound::From` (already ported)
     for the angular bound, position-history shift (`+0x180..0x1a0`), two unread helpers `0x180070d60`/`0x180070c60`
     (quaternion normalize/rebuild, guessed from the shape, not confirmed), then a per-pair loop over the controller's
     own touching-pair array (`+0x70`, count `+0x6a`) computing a candidate time and appending each pair pointer into the
     local min-list (`FUN_180072ba0` grows it — unread, plain array grow).
   - `IvpHullManager::NotifyAll` (`18009a690`, already named/read in full this session) then drains that min-list: a
     bottom-up heap sift using two unread comparators `FUN_180094460`/`FUN_180094540`, firing a vtable slot at each
     pair's `+0x28`-indexed table on a swap (this is where a queued time-of-impact becomes a real event — the
     `IvpMindistFire`/`Examine` path item 1 already ported per-pair almost certainly lands here), then for the smallest
     1–3 entries calls `FUN_18009a4f0` (unread) and stamps an integer tick from a double time via `FUN_180094490`
     (unread) when the entry is due.
   - After every controller is processed, `IntegrateAwakeCores` loops the controllers again calling
     `IvpMindistHull::RevalidateOne` (`1800746c0`, renamed and read in full this session — per-pair: compares each
     side's hull-cache generation at `+0x250`, revalidates via `FUN_180095cb0` [unread] on a mismatch, then re-`Examine`s
     with `recheck = AfterMiss` when not parked) through `IvpMindistHull::RevalidateTouching` (`1800792b0`, renamed and
     read in full this session — stamps the object's own `+0x250` generation from the environment's `+0x1a4` counter,
     then walks the object's hull array calling `RevalidateOne` on each).
   - **`FUN_180099a00` read in full 2026-09-14** — the actual per-controller PSI step, and it is large:
     - **Phantom listener dispatch** (the unported mechanism HANDOFF already flagged, `FUN_18008ae50`/`FUN_18008b0a0`):
       when `core+0x58` (a listener pointer) is set and `core+0x8` is nonzero, checks squared linear movement
       (`core+0x130` accumulated offset vs. the hull bound's `+0x110`/`+0x14`) and calls listener vtable `+0x8` on
       overflow, then squared angular movement (`core+0x140..0x148` vs. the bound's `+0xc`) and calls vtable `+0x0`.
     - **Rotation integration is `FUN_180099fc0`, read in full — IVP's real quaternion integrator with adaptive
       substepping**: when a flag bit (`core & 0x8`, or the hull's `+0x1ac == 5`) is clear, computes the angular speed
       magnitude (`FUN_18006e120` — trivial, `sqrt(x²+y²+z²)`), and if `speed² · dt² ·` a constant exceeds a threshold,
       subdivides the step into `ceil(sqrt(...))+1` sub-steps, each applying a quaternion multiply (`FUN_180071680`
       plus inline quaternion math) — otherwise takes the flag-bit path through `FUN_180070f50` (axis-locked, unread
       in detail) instead. **This is a substantial, self-contained IVP mechanism** (`ivp_mindist`'s rotation update,
       almost certainly `change_orientation`/`rot_change` in the real IVP source naming) — expect several more
       functions (`0x180071680`, `0x180070f50`, `0x1800c8020`) if it needs porting exactly, or a justified equivalence
       if `System.Numerics.Quaternion`'s own integration is provably the same to the ULP this project already holds
       itself to.
     - **Position/orientation history**: after rotation, extrapolates position linearly using last-step velocity
       (`core+0x150..0x160` position, `core+0x170..0x178` velocity), then double-buffers the transform
       (`core+0x1a0/0x1b0` current → `core+0x180/0x190` previous) before calling `FUN_180070d60`/`FUN_180070c60`
       (quaternion normalize + world-matrix rebuild, guessed from the shape, not read in detail).
     - **The per-pair aging/enqueue loop** (own pairs at `core+0x70`, count `core+0x6a`): ages each pair's
       extra-radius/margin fields over elapsed time using the pair's cached bound values, and appends the pair into
       the caller's local candidate list (`FUN_180072ba0` — read in full, trivial capacity-doubling array grow, no
       port gap) when the aged margin goes negative.
   - **The rotation integrator is now fully resolved, read in full 2026-09-14 — no port gap, standard math throughout.**
     `FUN_180071680` (the normal, non-`0x1ac==5` path): a shared small-angle quaternion delta from angular velocity ×
     half-dt, cubic sine approximation (`sin(x)≈x−x³/6`), `w=√(1−x²−y²−z²)` — portable directly.
     `FUN_180070f50` (the exact/axis-locked path): the same construction but with a REAL `sin` call per axis
     (`FUN_1800c8020`) instead of the cubic approximation, then renormalizes if the xyz magnitude exceeds 1.
     `FUN_1800c8020` **is plain CRT `sin(double)`** — a range-reduced minimax polynomial with an AVX/FMA-detected
     variant and NaN/inf special-casing, not IVP code at all. **`Math.Sin`/`Math.Sqrt` are the correct C# equivalents
     for both paths**; no oracle or dedicated port pass needed for this piece — it was a false alarm raised by its
     size, not by any actual engine-specific behaviour.
   - **`FUN_180070d60`/`FUN_180070c60` also closed, read in full 2026-09-14 — standard math, no port gap.**
     `FUN_180070d60` is a plain quaternion multiply (Hamilton product, `new = delta * old`).
     `FUN_180070c60` is quaternion normalize: magnitude² checked against an epsilon, an iterative Newton refinement of
     `1/√magnitude²` when off by more than it, then all four components scaled. Both are directly portable
     (`System.Numerics.Quaternion`-shaped, though the Newton iteration's exact step count may matter for bit parity —
     check when porting, not a design question).
   - **`FUN_180094460`/`FUN_180094540` read in full 2026-09-14, in `NotifyAll`'s per-pair heap loop** (`NotifyAll`'s
     params corrected on this read: `RCX`=manager, `RDX`=`&localMinList`, not the reverse guessed earlier).
     `FUN_180094460(pair, manager)` is a trivial getter (`manager+0x48 → +0x18`, an id/generation counter, shape).
     `FUN_180094540(pair, manager, index)` dispatches through **the manager's own vtable, slot at `+0x20`** — this is
     polymorphic, resolved by whatever concrete manager type is running. **Hypothesis, not yet confirmed**: given the
     compare-a-stamped-id-then-notify shape matches exactly what item 1's `Examine`/`RevalidateOne` already do, this
     slot most likely resolves to the same `IvpPairScheduler.Examine` path, reached virtually here instead of
     directly. **Needs the concrete vtable read to confirm before relying on it** — not yet located which type
     implements this manager interface at runtime for a live environment.
   - **`FUN_18009a4f0` read in full 2026-09-14 — a repeat, not new.** It is the same drain-loop body already seen
     inline in `NotifyAll` (fire ready events — `pair.time(+0x30) < pair.gate(+0x18)` — via a vtable `+0x8` call on
     the pair's indexed array entry, re-sift via `FUN_180094540`, repeat). No new mechanism; confirms the loop shape
     rather than adding to it.
   - **`FUN_180094490` read in full 2026-09-14 — a genuinely different mechanism, and a real port-model gap, not
     solved math like the rest of this item.** Walks a per-pair offset-accumulation list (an index chain at the
     pair's `+0x38`/`+0x28`, entries carrying a next-index at `+4`, an accumulator at `+8`, an object at `+0x10`),
     redistributing the pair's own accumulated position delta (`-pair+0x10/+0x14`) into every linked entry's
     accumulator and firing that entry's vtable `+0x18` on each, then flattens the pair's `+0x10` delta into its own
     `+0x18`/`+0x30` (gate/time) fields and clears it. **The current C# port has no equivalent field or mechanism for
     this offset accumulation/flattening** — `IvpMindist`/`IvpMindistState` track length/normal/flags but nothing
     shaped like a per-pair accumulated delta redistributed across a linked side-list. This needs its own dedicated
     investigation before the running-path rewrite can model it.
   - **Resolved 2026-09-14 — confirmed a lazy-deferred optimization, not a new physical quantity.** Traced the raw
     bytes at `IvpRigidBody::PutCoreToSleep`'s call site (`180078d12..180078d26`, which the decompiler had elided into
     an unrelated-looking `IvpHullManager::Rebase` neighbourhood — **read the disassembly, not just the decompile,
     for this one**): `pair+0x10`/`+0x14` are incremented by a velocity × elapsed-time delta immediately before the
     call, the exact same math `FUN_180099a00`'s per-pair loop already ages every PSI. **`+0x10`/`+0x14` is the same
     margin/extra-radius-growth-over-time quantity already scoped for that loop — not a separate new field.**
     `FUN_180094490` is its redistribution/flatten step, run whenever a pair actually fires (`NotifyAll`'s three
     gated calls) or once more right before its core sleeps, so a deferred update never goes stale past those two
     triggers. **Conclusion for the port: no new state is needed beyond what `IvpMindistState`/the pair-aging loop
     already models** — a port that recomputes the aged margin at the point of use (rather than deferring it into an
     accumulator that's periodically flushed) is equivalent, since the accumulator's only purpose is to avoid
     touching every pair's cache every step. This closes the biggest open question from `FUN_180099a00`.
   - **The `collide` callback (`FUN_18008ecb0`→`FUN_18008ef60`) is far more built than assumed — traced 2026-09-14.**
     `IvpMindistFire.Handle` already exists and takes `collide` as a plain `Action<IvpMindist>` hand-in — deliberately
     unported, per its own doc comment. Tracing what it must do: `FUN_18008ecb0` rebuilds each core's matrix at event
     time (`IvpRigidBody::RebuildMatrixAtEventTime` — check whether ported) when not resting/immovable, bumps the
     environment's generation counter (`+0x1a4`, the same one `RevalidateTouching` reads), then calls `FUN_18008ef60`.
     That function: finds/creates the pair's friction system (`IvpFrictionSystem::LinkContactByCore` — **named in
     Ghidra, not yet ported**), looks up the existing `IvpFrictionPair` for the two cores via `FUN_1800850b0` — **read
     in full, and it is trivial**: a plain linear search of the system's own `Pairs` list (already a real class,
     `IvpFrictionPair`) for a `(core1, core2)` match in either order, no dispatch logic at all — stamps the contact's
     last-measured time to `Now`, calls `IvpContactPoint.PushOut` (already ported) and `IvpImpactSolver.Enter`
     (already ported), flips the pushed-vector sign for one orientation convention, then a still-unidentified
     `FUN_180090700` call (name collision with the unrelated island-assembly function of the same address in an
     unrelated file — **needs its own read**, almost certainly `IvpImpactSolver.Solve`'s real call site or an
     override-list construction I have not resolved).
   - **The one confirmed real gap: `IvpMindist` has no persistent contact cache.** `IvpEnvironment.cs`'s own existing
     comments already flag this as a departure (*"Not persistent, and the engine's ARE — `FUN_18008d0c0` caches its
     record at `mindist+0x70`"*) — `IvpMindist` needs a `ContactPoint` property (nullable `IvpContactPoint`) before
     `collide` can be written at all, since a contact point's whole design is to outlive one collision and warm-start
     from the last one.
   - **Correction, same session: this is NOT mostly orchestration.** `FUN_180090700` (`collide`'s tail call) is the
     PSI's impact-retry loop, read in full: it rebuilds each collided pair's contact records, then loops
     `FUN_180090bd0` (also read in full) — which scans every touched friction pair for the WORST approaching contact
     (`IvpContactPoint.Estimate`, already ported), calls `IvpImpactSolver.Enter`/`.Solve` on it (already ported), and
     repeats up to 5,000 times — before finally re-entering `IvpEnvironment::IntegrateAwakeCores` itself. **This
     confirms the PSI is not a clean linear phase sequence**: a single collision can recursively re-run the whole
     per-core integration loop. This part IS close to pure orchestration (`Estimate`/`Enter`/`Solve` already exist) and
     is safely portable once read once more carefully for the exact loop-exit condition (this session's read is close
     but the `FUN_18008da40`/`FUN_180090240`-style helpers inside it are still unread).
     **`IvpFrictionSystem::LinkContactByCore` (`180090e50`) is a different story — read in full, and it is a genuine,
     substantial subsystem**: per-object friction-info hash allocation, placement-new contact allocation
     (`IvpContactPoint::Allocate`), and MERGING two previously-separate friction systems when a new contact bridges
     objects that were each already in their own system. It pulls in roughly a dozen more unread functions
     (`FUN_180078460`, `FUN_180081e50`, `FUN_1800879e0`, `FUN_180076690`, `FUN_180088090`, `FUN_180087bf0`,
     `FUN_180086240`, `FUN_180083a60`, `FUN_180074e40`, `FUN_1800747a0`, `FUN_180088310`) — the same scale as the
     OV-tree or larger-mindist ports, each of which got its own dedicated multi-stage, oracle-backed session. Writing
     it now from inference, without reading those functions and without a `vphysics-*` probe verifying it against the
     shipped binary, would risk exactly the kind of subtly-wrong physics this project's own history (documented at
     length in `IvpEnvironment.cs`) has repeatedly paid for.
   - **Contact identity, read further, 2026-09-14 — `IvpMindist.ContactPoint` (`+0x70`) is a memo, not the canonical
     store.** `IvpContactPoint::Allocate` (`18008c4b0`) is the real lookup: only for an exact mindist (flags
     `& 0x3c0000 == 0xc0000`), it walks the SECOND object's own `ContactPoints` list (already a real field,
     `IvpCollisionObject.ContactPoints`) for an existing point whose `FirstObject`/`SecondObject` matches the other
     side AND passes a feature-match test, `FUN_1800869a0` (unread) — reusing it if found (persistence, warm-starting
     `Slide`/`PushStreak`), else allocating fresh via the already-ported `IvpContactPoint` constructor. The `+0x70`
     field added this session is a per-mindist fast-path memo the engine also keeps, checked before this search — its
     exact relationship to the canonical object-list search (which takes priority, when the memo is trusted) is not
     yet confirmed and needs its own read before `LinkContactByCore` can use it correctly.
   - **`IvpFrictionPair::Build` (`180086670`) — read, and confirmed genuinely skippable for now.** It computes an
     inter-body relative-velocity direction and per-core response coefficients (rotated into each core's local frame
     via `IvpMatrix::RotateInverseNarrowed`, unread) that `IvpFrictionSystem.cs`'s own existing doc comment already
     says are deliberately not carried (*"only the fields the heap solve reads are carried"*) — and confirmed here:
     `LinkContactByCore`'s own pair-allocation path (`FUN_180088090`→`FUN_1800830d0`) never calls `Build` at all; it
     only zero-inits a bare shell with two sentinel constants, matching the existing plain `IvpFrictionPair`
     constructor exactly. No new work needed here.
   - **The system-merge branch (`FUN_180086240`, read in full) is its own substantial function** — migrates every
     contact and every core from a losing system into a surviving one when a new contact bridges two objects that
     were each already in separate systems, merging per-core friction-info shares along the way. Confirmed separable
     from the common "new pair, no pre-existing systems" path — the next port should implement the common path first
     and throw a named `NotSupportedException` for this branch, matching this codebase's own established convention
     (e.g. `IvpMindistManager.RecheckEveryPsi`'s ball-vs-triangle exception) rather than guessing at a merge this
     deep without its own oracle.
   - **Ordering note for whoever ports this**: `LinkContactByCore` swaps which object is treated as the FIRST
     friction core based on ONE bit of the object's own `core+0x0` byte (bit `0x2` alone) — but `IvpRigidBody`'s
     existing `Immovable` bool already collapsed TWO engine bits (`0x2` and `0x10`) into one, per that property's own
     doc comment, because no prior caller needed to tell them apart. **This is a real divergence risk, not
     approximated away lightly**: for every real ragdoll-vs-static-world case the two bits coincide, so `Immovable`
     is a safe stand-in in practice, but the port should say so explicitly in a comment rather than silently reuse
     `Immovable`, and should not touch `Immovable`'s representation to fix this without re-testing every existing
     caller.
   - **Done, same session: contact identity and friction-system linking for a body-vs-world collision.**
     `IvpFrictionLinking.FindOrAllocate` (`IvpContactPoint::Allocate`) and `.LinkContactByCore`
     (`IvpFrictionSystem::LinkContactByCore`) are ported and tested (`IvpFrictionLinkingConformanceTests.cs`, 9 cases,
     TDD caught two real bugs — `AddCore` not wiring a core's own `FrictionInfo` back, and the ordering-swap
     approximation — before either touched running code). Explicitly `NotSupportedException`s the system-merge branch
     (`FUN_180086240`), which only body-against-body contact would reach and this project does not implement.
     **Still not pinned by an oracle probe against the shipped binary** — these are synthetic conformance tests, not a
     replay, so treat them as "internally consistent with the read behaviour," not "proven bit-identical to
     vphysics.dll" until a `vphysics-friction-link` probe exists.
   - **Checked `IvpMindistMinimize.Solver.Route` (`180094c70`) — it is NOT the side builder.** It's the
     feature-kind dispatcher (`PointPoint`/`PointEdge`/`PointFace`/`EdgeEdge`/`FaceFace`), matching the C# port's own
     `Solver.Dispatch` already. **`IvpLedgeSide` construction from a live `IvpCollisionObject` genuinely does not
     exist anywhere in this port** — every native path either receives sides as arguments or (per `Minimize`'s own
     C# signature) takes them as parameters. `collide` should follow the same convention: take pre-built sides as
     parameters, deferring "who builds them from live objects each PSI" to the same still-unbuilt orchestration layer
     that will need to feed `Minimize` its sides anyway.
   - **`IvpRigidBody::RebuildMatrixAtEventTime` (`180078d60`) read in full — real work, one more dependency needed.**
     Saves the core's current angular velocity/orientation into a NEW snapshot (`core+0x260`, arena-allocated — the
     save half of `FUN_180079120`'s restore, already ported this session), overwrites the core's live orientation
     with the value interpolated to the exact event time (`IvpQuaternion.Interpolate`, already ported), rebuilds
     `CoreMatrix` at that instant (`IvpMatrix.FromRotation`, already ported) along with a new "position at event
     time" field (`core+0xf0/0xf8/0x100`, not yet on `IvpRigidBody`), then — unless `FlagBit3` — recovers the exact
     instantaneous angular velocity implied by the interpolated quaternion via `FUN_1800d392c` (signature matches
     `asin(double)`, not yet confirmed) applied to `FUN_1800712b0`'s output (**unread** — extracts axis components
     from the quaternion delta). **Needs**: `FUN_1800712b0` read, a snapshot type added to `IvpRigidBody`, the new
     event-time-position field. Small, well-scoped — not rushed into this pass.
   - **Done, same session: `RebuildMatrixAtEventTime` ported** (`IvpRigidBody.RebuildMatrixAtEventTime`,
     `IvpCoreSnapshot`, `EventPosition`, `RestoreFromSnapshot`) with 5 tests, and **`collide` itself is wired**
     (`IvpMindistCollide.Collide`) through `IvpFrictionLinking` → `IvpContactRecord.Build` →
     `IvpContactPoint.SetMaterials`/`PushOut` → `IvpImpactSolver.Enter` (which runs its own solve internally, no
     separate `.Solve()` call needed — simpler than first assumed). **A real hang was found and fixed by actually
     running the tests, not by inspection**: `LinkContactByCore` was relinking an already-filed contact into
     `IvpFrictionSystem`'s list unconditionally, setting the point's own `Next` to itself — a one-node cycle. Fixed by
     skipping the link when the contact already belongs to the system.
   - **Correction, same session: the "retry loop" is not separate scope from the top-level PSI driver — it's the same
     mechanism, reused recursively.** Re-read `FUN_180090700` mapping its real parameters (out-scratch, mindist,
     system, pair, contact — corrected from an earlier wrong guess at this same call): it builds a temporary
     mini-island of just the pair's two touched objects, runs the worst-approaching-contact retry
     (`FUN_180090bd0`, up to 5,000 times) against it, then calls `IvpEnvironment::IntegrateAwakeCores` **on that same
     scratch structure** to re-integrate just those two cores. This confirms `IvpIntegrator.cs`'s original doc
     comment — *"the pipeline `FUN_180082560` assembles islands in `FUN_180090700`"* — was right; an earlier read in
     this session had doubted it. **`collide` (`IvpMindistCollide.Collide`) is correctly complete as written**: it
     ends at `IvpImpactSolver.Enter`, which is the real, whole physics response for one collision. What comes after —
     building a real island/environment substructure and driving `IntegrateAwakeCores` on it — was always going to be
     the top-level PSI driver's job; this reading just confirms there is one remaining big task here, not two.
   - **Done, same session: `IvpLedgeSide.FromLedge`** closes the "build fresh sides from a live object" gap — not
     read from the disassembly, this project's own assembly of already-decoded pieces (`PhysicsLedge`'s fields map
     directly onto `IvpLedgeTopology`'s constructor). 1 test, no port gap left here.
   - **Found, same session, reading toward the per-PSI friction solve: the TANGENTIAL solve is a whole separate
     unported subsystem, roughly the size of the already-oracle-backed normal-push heap solve.**
     `IvpFrictionSystem::SolveOncePerPsi` (`1800836b0`, read in full) walks every `Pairs` entry, and each pair
     apparently keeps its OWN array of contact records touching it (a field `IvpFrictionPair` does not yet carry —
     `IvpFrictionPair::Build`'s deferred extra fields, revisited: they may not be as skippable as the normal-push path
     made them look), computes a per-pair friction cone budget from summed `PushOut × Friction × <a `+0x60` field not
     yet named>`, clamps each contact's `Slide` against it, then dispatches per contact to `FUN_180085100` (unread, a
     fast "sticking" path) or `IvpFrictionSystem::SolveTangentialPair` (`1800857c0`, named in Ghidra, not yet read in
     detail — likely similarly sized to `SolveHeap`). **This needs its own dedicated read-design-port-oracle pass**,
     matching how `SolveHeap`/`SolveOne` earned `IvpHeapSolveConformanceTests` and the `vphysics-heap-solve` probe —
     not something to guess into the PSI driver.
   - **`FUN_180085100` (the "sticking" fast path) read in full, 2026-09-14 — as substantial as `IvpImpactSolver.Enter`
     itself, not a quick add.** Built from already-ported primitives (`IvpRigidBody.UnitPush`, `.PointVelocity`,
     `IvpMatrix.RotateInverseNarrowed`), but a full push-along-a-slide-direction solve in its own right: computes a
     combined slide direction from the pair's two spans, projects each core's relative velocity onto it, solves for
     the push that cancels it (clamped by the per-pair friction budget from `SolveOncePerPsi`), and stages it into
     each core's pending push (`+0x98../0xa0..`, matching `IvpRigidBody.PendingVelocity`/`PendingAngularVelocity`
     shape). `IvpFrictionSystem::SolveTangentialPair` (`1800857c0`) is the other dispatch branch, not yet read, likely
     similarly sized. **Confirms the tangential solve is genuinely its own dedicated port+oracle stage**, not
     something to fold into the PSI driver pass — same conclusion as the previous entry, now with more precision.
   - **`IvpFrictionSystem::SolveTangentialPair` read in full, 2026-09-14 — confirms a full separate subsystem, at the
     scale of the whole impact solver.** A proper 2×2 symmetric-matrix friction-cone solve: computes the tangential
     slip velocity, inverts a 2×2 symmetric system to find the friction impulse that would cancel it, clips it to the
     cone's radius if it exceeds the budget, applies it, and tracks a running average magnitude (`param+0x84`) for
     the caller's `+0x30` accumulator. Pulls in **four more unread functions**: `FUN_180085a80` (an early-out branch,
     gated on some per-pair condition involving `+0x58`), `IvpContact::TangentialSlipVelocity`, `IvpContact::TryInvertSymmetric`
     (the 2×2 inverse itself), `FUN_18009c620` (applies the found impulse). **This is not a small remaining piece —
     it is the same scale of work as `IvpImpactSolver` itself**, which earned its own dedicated
     `vphysics-impact`/`vphysics-heap-solve` oracle-backed ports. Treat the tangential solve as its own
     multi-session port, following the exact same precedent (OV tree, larger mindist, recursive mindist, impact
     solver), not a step folded into finishing the running path.
   - **Found trying to wire the PSI driver's live `Minimize` call, 2026-09-14 — a real architectural prerequisite, not
     a detail.** `IvpMindistMinimize.Minimize`/`IvpMindist`/`PhysicsLedgeTreeNode` all need a real `PhysicsLedge`
     (points, triangles, edge topology) for each side. **`IvpRigidBody.Hull` — the running path's current
     representation, read by the old GJK-based `IvpContact.Find` — is a flat point cloud plus a separate `Faces` list,
     not a `PhysicsLedge`/ledge tree.** Only `IvpWorldCollision` carries real ledge-tree data today. Building the
     live PSI driver means giving ragdoll bodies a real ledge-tree hull too (from `PhysicsHull.Tree`, the same
     decoder `IvpWorldCollision` already uses for the world) — not just reusing the existing flat point list. This is
     a real, necessary piece of replacing `IvpContact`, not an incidental wiring detail.
   - **Done, same session: `RagdollElement.Ledges`/`IvpRigidBody.Ledges`** carry the un-flattened `PhysicsLedge` list
     through — `HullInBoneSpace` already had it and was discarding it into `Hull`/`Faces`. Not yet consumed; this is
     the raw material the next step below needs. 1 test, both `RagdollBody.Build`/`BuildProp` call sites updated.
   - **Next, in order**: (1) as its own dedicated port: read `FUN_180085a80`, `IvpContact::TangentialSlipVelocity`,
     `IvpContact::TryInvertSymmetric`, `FUN_18009c620` in full, design `IvpFrictionSystem`'s per-pair contact list and
     the tangential solve's own state, port both `SolveOncePerPsi` dispatch branches, build a `vphysics-friction-solve`
     probe and oracle fixture, sabotage-verify; (2) turn `IvpRigidBody.Ledges` into a real ledge-tree hull (an actual
     tree structure, from `PhysicsHull.Tree`-shaped logic, or a flat single-ledge shortcut for a body with only one)
     so `IvpLedgeSide.FromLedge` can build sides for a moving body, not only the world — `Ledges` alone is not yet
     enough, since a mindist needs a *node* (matching `PhysicsLedgeTreeNode`) to name which ledge a synapse feature
     came from; (3) the top-level `IntegrateAwakeCores`-shaped PSI/island driver — `FUN_180090700`'s mini-island
     construction, the retry loop, `collide`'s generation bump (`env+0x1a4`); (4) a
     `vphysics-friction-link`/`vphysics-collide` oracle probe for everything built this session — still synthetic
     conformance testing, not a replay against the shipped binary; (5) only then replace `IvpEnvironment`/`IvpContact`.
   - **`IntegrateAwakeCores`'s full call graph is now closed, 2026-09-14.** `FUN_180079120` (read in full): trivial —
     when `core+0x260` names a queued snapshot, restores it into the core's bound extents (`+0x130..0x138`) and
     transform (`+0x1a0/+0x1b0`), then clears the pointer. `IvpMindistMinimize::Minimize` (`180095cb0`) was already
     read and named earlier this session (the generation-cache-hit check ahead of the actual minimize dispatch) — it
     was never actually unread, just mislabeled in an earlier pass of this list. **The only remaining open item is
     confirming the concrete vtable behind `FUN_180094540`'s slot `+0x20`** (the `Examine`-dispatch hypothesis) — a
     nice-to-have verification, not a blocker: nothing about the rewrite's design depends on which concrete type
     implements it, since either way the port already has `Examine` ported and callable. **The running-path rewrite
     can now be designed** from this scoping without further disassembly, unless implementation turns up a new
     question.
   - Replacing `IvpContact`/`IvpEnvironment` means reproducing this exact two-pass shape — build each controller's local
     candidate list, drain it as a heap firing real events, then a second full pass revalidating every pair's cache
     generation — not a single merged loop, and not `Advance`'s ad hoc per-collision subdivision. **A full rewrite, not a
     patch** (owner's direction, 2026-09-14): this replaces the invented solver outright rather than feeding it.
   - This is a multi-session rewrite; the next session should start by reading the five still-unread functions above in
     full before writing any C#.
4. **Displacements — deferred 2026-09-14, pick up after item 3 (or later).** `FUN_180025bc0` calls the engine's
   virtual-mesh query, which is outside vphysics.dll, so this subsystem can never be corpse-drop tested standalone even
   fully ported — and most TF2 maps don't use physics displacements. `FUN_18007bea0` (closest-point-on-triangle distance,
   vertex/edge/face region test) is read: built on `FUN_18007cdf0` (triangle edge+normal setup, axis-permutation table at
   `0x180124fe0`) and two more dense SIMD helpers, `FUN_18007d070` (edge-projection, read — computes an edge parameter
   pair) and `FUN_18007d300` (unread). Low information density per token spent reading it by hand; stopped here on the
   owner's call to conserve budget, not because it's blocked.

**Phantoms stay unported** (`FUN_180097940`'s far path is read, `FUN_18008ae50`/`FUN_18008b0a0` are the phantom controller's
listeners); whether a TF2 client corpse ever meets one is not established — the port throws where one would be told.

## How the ports are built, so the next one matches

A port in `managed/Tf2DemoSalvage.Animation/Animating/`; its lanes in `tools/Tf2DemoSalvage.Probe/Oracle/Ivp*Replay.cs`
(linked into `Tf2DemoSalvage.Animation.Tests.csproj` with its `Data/*.txt`); a probe in `tools/.../Probes/Vphysics*Probe.cs`
with `sweep n` and `fixture path` modes; a `*ConformanceTests` class with a fixture control. **Isolate a routine by detouring its
callees** (`VphysicsDetour`, twelve bytes, restored on dispose) rather than fabricating their state. Constants by their bits or as
widened floats — a decimal literal cost an ulp twice. Every product and sum through `IvpMath` with the disassembly's destination.
Use the MCP servers: `mcp__ghidra__*` (`disassemble_function`, `disassemble_bytes` for code Ghidra has no function for,
`read_memory`, `get_xrefs_to`) and `mcp__agent-lsp__*` for the C# side.
