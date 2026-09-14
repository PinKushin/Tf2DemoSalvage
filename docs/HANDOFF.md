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
   - **Not attempted this session, and it should not be rushed**: writing `collide` for real. Every other subsystem in
     this port — down to the larger mindist's own 5-stage, oracle-backed process — earned trust through a dedicated
     `vphysics-*` probe calling the shipped binary in process before being wired in. `collide` is the one piece left
     that would ship without one if written now. **Next session's concrete steps**: read
     `IvpFrictionSystem::LinkContactByCore` and the true `FUN_180090700` (this call site, not the island-assembly one)
     in full, add `IvpMindist.ContactPoint`, write `collide` as its own class with its own oracle probe
     (`vphysics-collide` or similar) before wiring it into any running loop.
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
