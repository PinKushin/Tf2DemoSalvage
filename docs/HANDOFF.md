# Handoff — IVP's collision path, ported function by function against vphysics.dll (B369, D172)

Written 2026-09-14, superseding the handoff at `e47dc3f1` (same direction, earlier state).

**2026-09-18: the virtual-terrain drop and the drive-together test both pass** — see *Resolved 2026-09-18* below. Branch
`wip/b369-contact-drops`; Animation.Tests 5335 total, 5334 passed, 1 skipped.

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
3. **The running path. THE DRIVER IS NOW PORTED AND RUNNABLE, 2026-09-15 — `IvpSimulation`** (`managed/.../IvpSimulation.cs`)
   holds the environment, the time manager, the unit lists and the mindist manager, and runs the engine's own step: the
   self-rescheduling PSI event (`IvpPsiEvent`, `FUN_18008a020`), its six-phase pipeline (`IvpPhysicsPipeline`,
   `FUN_180082560`), each awake unit's PSI (`IvpSimulationUnit`, `FUN_180075c80`), the whole per-core step
   (`IvpIntegrator.StepCore`, `FUN_180099a00`), the hull pass, the per-core recheck, and all five controllers by their read
   priorities — friction `2000`, gravity `1000`, friction `600`, the constraints `405`, friction `0`. The impact loop
   (`IvpImpactIsland`: build, drain, grow, tail) runs inside the collision, as `FUN_18008ef60` does.
   **2026-09-16: two bodies collide in it end to end** — broad phase, near/far cycle, one-event-at-a-time queues, time-coded
   object caches, contact filing for two movers with the system merge, unit merge, wake with the core revive, sleep with the
   core freeze (findings 51, *Two bodies driven together, end to end*). Since ported, each in findings 51: the map as static
   objects and virtual terrain, the friction split, the revive's resting-contact rebuild, the tangential work bank,
   `CPhysicsEnvironment::Simulate`'s frame dispatch with `GetPosition` read at the clock, and air drag. **What it still lacks:**
   the mindist slot-0 call at the 5,000-pass cap, and phase 1's guarded calls and `env+0x158` list. **A gap found 2026-09-17
   on `wip/b369-contact-drops` (parked, not merged):** with the normal pass's contact drop in (`FUN_180084490`'s tail,
   `FUN_1800a9bf0`'s filing pass), `Advance_ABodyDroppedOnVirtualTerrain_ComesToRestOnIt` falls through instead of resting.
   **One real wiring bug found and fixed the same day, but it does not close the gap**: `IvpPhysicsPipeline.Psi`'s phase-2
   `RecheckInvalid` closure was calling the pipeline's shared budgeted `minimize` (`FUN_180095cb0`, `IvpSimulation.Minimize`)
   on every invalid pair, where `IvpCollisionObject::RecheckInvalid` (`FUN_180074240`, read fresh from the disassembly) calls
   specifically `IvpMindistMinimize::MinimizeWithoutBudget` (`FUN_180095ad0`) — the same routine but for one stack constant (a
   step budget of 20 versus none). Fixed by threading a second `recheckInvalidMinimize` delegate through
   `IvpPhysicsPipeline.Psi`/`IvpPsiEvent.Start`/`RunPsi`, wired to `IvpSimulation.MinimizeWithoutBudget` in `IvpSimulation.Start`;
   pinned by `IvpPhysicsPipelineTests.Psi_AnInvalidMindist_IsRecheckedWithTheNoBudgetMinimizeNotThePhaseThreeOne` (sabotage-verified:
   reverting the wiring reddens exactly that test). **The broad-phase test's own numbers are unchanged after the fix**
   (`body.Position.Y` still lands at `7.220574437630701`, bit-identical) — so the fall-through is not a delegate mix-up.
   Re-traced with a temporary `IvpMindistManager.Recheck`/`RecheckInvalid`/`IvpRecursiveMindist.HullPassed` instrumentation
   (removed before committing, per this file's own convention): the mesh's 8 child mindists (one per triangle under the opened
   ledge) DO get created, DO cycle exact ↔ invalid correctly for a while (a real oscillating near-zero-length resting pattern),
   and `RecheckInvalid` DOES revive several of them mid-fall. Eventually every child in a PSI reports a single huge jump in
   `Length` (observed `-7.84` to `-7.95` in different runs) and its flags settle at exactly `0x4000` — IVP's "genuinely parked,
   `RecheckInvalid` refuses to revalidate" state (`(flags & 0xc000) != 0x4000` is the only reviving condition, confirmed against
   `FUN_180074240`'s own instructions) — and **never comes back**, because `IvpPairMindists.Refresh`'s own "Keep" step (confirmed
   against `FUN_180096680`'s disassembly: a hash match purely on the two ledge pointers, no state check at all) reuses the SAME
   dead mindist object for that ledge pair forever rather than ever discarding and rebuilding it. The outer
   `IvpRecursiveMindist`'s own coarse pair gets stuck in the identical way (its `HullPassed`'s `recheck(this)` re-minimizes the
   SAME fixed synapse features every hull pass — confirmed byte-for-byte against `FUN_1800b28a0`, so this is not a port bug
   either), which is why it never satisfies the "frozen bits clear AND length past tolerance" test that would let it close back
   to a plain exact pair and `DeleteChildren()`.
   **2026-09-17, resolved by disassembly** (`FUN_180094e30` `BacksideWalk`, `FUN_180097f00` `IvpMindistHull::HullPassed`,
   `FUN_180003f70` `PhysicsVirtualMesh` construction): the permanent park at `0x4000` on a single-triangle child IS IVP's real,
   by-design terminal state — `PhysicsVirtualMesh.TriangleLedge` deliberately builds one isolated 2-face ledge per triangle with
   no neighbor topology at all (`BacksideWalk`'s three-edge crossing test always fails against it), and the shipped engine
   hands exactly these isolated ledges to the per-triangle children. **Not a missing-topology bug.** Recovery is meant to come
   from the OUTER `IvpRecursiveMindist`'s own `HullPassed` → `RefreshChildren` → `IvpPairMindists.Refresh` cycle re-querying the
   surface tree fresh from the body's current position each hull pass — and that cycle **is firing**, confirmed by re-tracing
   `simulation.LastHullPass` through the fall: `Refiled`/`HandedOff` alternate repeatedly from t≈0.6 to t≈2.0.
   **Corrected 2026-09-17 (this doc's own "launches upward" framing, written earlier the same day, was itself wrong — it
   compared raw Y values without the test's own +Y-is-down convention; see the test's remarks, "a cube dropped along IVP +Y"):**
   traced `body.Velocity.Y` per step. Gravity accelerates it smoothly at a constant +0.1515/step from t≈0.55 (already past the
   fixture's earlier fixed contact) with **zero impacts recorded** the whole way — i.e. **`IvpImpactSolver` is not seeing the
   ground at all until one single event at t≈1.09**, where `simulation.Environment.Impacts` jumps 0→3 in one step and velocity
   flips from +10.9 to −3.6 (a correct-looking bounce: three impacts, one per cube corner touching at once). That one bounce
   is accurate — the body's parabolic apex lands at y≈−4.64, t≈1.44, within half a unit of the true rest y=−4. **Then it falls
   straight back through with no second impact ever firing**: `Impacts` stays fixed at 3 all the way from t≈1.09 to the end of
   the traced window (t=1.99, y=−3.18, climbing straight back toward and past the ground) while `LastHullPass` keeps cycling
   `Refiled`/`HandedOff`/`Recursive` the whole time — the hull-pass machinery is still alive and still re-querying, it just never
   produces a second `Fired` impact. **This is exactly "falls through", precisely located**: the first contact resolves once,
   correctly, then the SAME pair's mindist (or its child under the recursive parent) never re-arms to catch the second approach.
   **Traced the exact chain, read from source (`FUN_1800977f0` `BecomeExact`, `FUN_180097440`/`180097914` `Freeze`)**: a plain
   exact mindist with `FrozenBits` set after its minimize (`(mindist.Flags & FrozenBits) != 0` in `IvpMindistHull.BecomeExact`)
   calls `IvpMindist.Freeze`, which is confirmed to do exactly one thing — `manager.Invalidate(this, first, second, queue)` —
   moving it into `IvpMindistManager.Invalid`. From there the ONLY path back to being examined again is
   `IvpCollisionObject.RecheckInvalid` → `RecheckInvalid` → `IvpMindistMinimize.MinimizeWithoutBudget`, and that revival
   condition (`(flags & 0xc000) != 0x4000`, `FUN_180074240`, already confirmed byte-for-byte against the disassembly) is
   written to explicitly REFUSE a mindist sitting at exactly `0x4000` — which is exactly the flag value `BacksideWalk`'s
   exhaustion leaves it at. **So the two already-confirmed-correct pieces combine into a real dead end**: `Freeze` always sends
   a resolved exact pair to `Invalid`, and `RecheckInvalid` is written to never revive it from there once it lands at exactly
   `0x4000`. Since real TF2 does not fall through displacement terrain after one bounce, something else in the shipped engine
   must OR a second bit into those flags when the object starts moving again — meaning it stops being exactly `0x4000` — and
   that write site has not been located. It is not `IvpMindistHull.HullPassed` (the coarse far-pair re-file loop, read above):
   this pair never re-enters that path, because `IvpPairMindists.Refresh`'s ledge-pointer-identity `Keep` reuses the SAME dead
   child object on the body's second descent (it lands on the same triangle) rather than routing through the coarse hull queue
   at all. **`IvpMindistManager.RecheckEveryPsi`/`Recheck` (`FUN_180098610` walk, `FUN_1800983e0`/`FUN_180098710` bodies) ruled
   out too**: `Recheck` calls `minimize(mindist)` UNCONDITIONALLY (no `0x4000` guard, unlike `RecheckInvalid`) so it looked like
   the missing revival path — but it only runs on mindists in `IvpMindistManager`'s `_rechecked` array, added only when either
   core's `HasOffset58` is set, and `IvpRigidBody.HasOffset58`'s own doc comment (read this session, `IvpIntegrator.cs:669`)
   says outright **"no ragdoll element sets it"** and names it a constrained/rotated-core flag with an unread writer. This
   test's cube and ground are plain unconstrained rigid bodies — neither ever gets `HasOffset58`, so this mindist never enters
   `_rechecked` and `RecheckEveryPsi` never touches it.
   **Read `RecheckInvalid` (`FUN_180074240`) itself in full, finally, and it changes the question.** It does NOT skip a
   `0x4000` mindist: it calls `minimize(mindist)` unconditionally on every entry of `InvalidSynapses`, every PSI, and only
   checks the flags AFTER to decide whether to revalidate. So the solver genuinely gets a fresh attempt every PSI — the
   question is not "is it ever retried", it is **"why does the retry keep producing the same answer".** Read
   `IvpMindistMinimize.Minimize` (`FUN_180095cb0`/`FUN_180095ad0`, both bodies, in full): on `Backside`, it calls
   `BacksideWalk` and **stores the walked result back onto the mindist's own synapse** (`mindist.SetSynapse(behind, new
   IvpSynapse(walked, IvpFeatureKind.Triangle))`) before retrying, up to twice, then sets `flags = 0x4000` and stops. Combined
   with the already-confirmed fact that `BacksideWalk` can never cross out of an isolated single-triangle ledge
   (no neighbor topology, by design, matching the shipped engine) — **every subsequent PSI's retry starts from the same
   stuck synapse, walks the same three uncrossable edges, and lands on the same `0x4000` again, forever, regardless of how
   far the body has moved and come back.** This is now a complete, disassembly-verified mechanism, not a further-unknown.
   **The real open question is no longer "what call site is missing" — it's whether this is genuine shipped-IVP behavior for
   a drop that lands EXACTLY on a shared triangle seam** (this fixture drops the cube at x=25.4, z=25.4, the flat cell's exact
   centre, deliberately straddling the diagonal between its two triangles) **or an actual port divergence.** `IvpPairMindists`
   only replaces a child when its ledge no longer appears in a fresh spatial query — a vertical drop returns to the same x/z
   and re-queries the same seam every time, so `Keep`'s ledge-identity match (already confirmed correct against
   `FUN_180096680`) would reuse the same exhausted child even in the shipped engine. **The single decisive experiment that
   needs no more disassembly**: rerun this same fixture with the drop point moved off the exact seam (e.g. x=20, z=25.4, well
   inside one triangle rather than straddling two) and see whether it rests correctly. If it does, the fixture itself — not
   the port — is what needs fixing (test a realistic drop point, not a pathological one); if it still falls through, the
   divergence is real and specifically in how the recursive parent should be prompted to re-open (`DeleteChildren`+refile from
   scratch) rather than keep refreshing the same doomed child, which is squarely `IvpRecursiveMindist.HullPassed`'s own
   close-or-refresh branching (already read and confirmed byte-for-byte — so if the experiment says this path is wrong, the
   divergence would be in a THIRD function not yet found, not in anything read so far).
   **Experiment run 2026-09-17: moved the drop point off the seam (x=16, z=22, well inside one triangle, no grid line and no
   local diagonal near either coordinate) — same failure, worse** (`y=20.09`, `impacts=4`, `hull=Refiled`, vs the on-seam
   drop's `y=7.22`/`impacts=3`). **This rules out the seam-straddle theory outright: it is not a pathological-fixture
   artifact, it is a genuine divergence**, reproducible from a plain, unremarkable drop point. Reverted the fixture change
   (test file is clean again). The next read this needs is **whether the mindist that lands at `0x4000` and dies is a CHILD
   of the recursive parent (one triangle's own exact pair, going through `BecomeExact`/`Freeze` independently of the parent)
   or the recursive parent's OWN top-level pair** — `simulation.LastHullPass` still reports `Recursive` outcomes after the
   bounce, meaning the parent never closes back to a plain exact pair (`IvpRecursiveMindist.HullPassed`'s `DeleteChildren`
   branch, which needs `(Flags & FrozenBits) == 0 && Length < ContactGap`, is apparently never taken), so the parent stays
   recursive and keeps calling `RefreshChildren` on a set that provably includes at least one permanently-`0x4000` child that
   is never dropped. **Traced live (temporary `Console.WriteLine` in `IvpRecursiveMindist.HullPassed`, removed before this
   commit): the PARENT's own `Flags` are ALSO stuck at exactly `0x4000`** (`0xFD04120 & 0xC000 == 0x4000`, all 4 hull passes
   this pair got, `now=1.773` through `2.712`, `Length` ranging `5.36` down to `0.87` — the parent's own `Length` DOES track
   real separation and shrinks correctly as the body falls back, but `frozenClear` is `False` every single time, so
   `HullPassed`'s close-to-plain-exact branch can never be taken regardless of `Length`). **This is the same solver-dispatch
   exhaustion as the child** — the parent's `recheck(this)` runs the identical `MinimizeWithoutBudget`/`Solver.Dispatch` on
   its own (placeholder Point/Point-at-construction) synapses, hits `Backside`/`GaveUp`, and lands on `0x4000` exactly like a
   real feature pair does. **And this exact fact was already flagged, correctly, before this session started** (see the
   original *"the outer `IvpRecursiveMindist`'s own coarse pair gets stuck in the identical way... confirmed byte-for-byte...
   not a port bug either"* above) — so BOTH the child's and the parent's `0x4000` states are now independently confirmed
   correct against the disassembly, in isolation. **That means the divergence is not inside any function read so far** —
   `HullPassed`, `Recheck`/`RecheckEveryPsi`, `RecheckInvalid`, `BecomeExact`/`Freeze`, and now the parent's own `HullPassed`
   condition are all individually faithful ports.
   **The `IvpFrictionSystem` candidate named just above was a wrong turn — corrected the same day.** Read
   `IvpVirtualMeshSurfaceManager.LedgesWithin` (the spatial query `IvpPairMindists.Side` calls): it is a genuine fresh
   `Tree.TrianglesInSphere` query against the CURRENT `center`/`radius` every call, not cached, so a body returning to the
   same x/z correctly gets the same candidate triangle indices back — `Keep`'s ledge-identity reuse of the old dead child is
   the RIGHT answer given a correct, un-cached query, not a symptom of a caching bug. And `IvpFrictionSystem` itself never
   does spatial detection at all — it only solves and files contacts a mindist already handed it via `BecomeExact`/`Examine`;
   it has no mechanism to invent a new contact on its own, so it cannot be where a missed second impact gets recovered.
   **The genuinely untouched remaining area is `IvpPairWatcher`/`IvpPairCreator`** (in the HANDOFF "Done" table above, ported
   and probe-verified for the ORIGINAL pair-creation path, but never checked for what — if anything — periodically tears
   down and rebuilds a pair wholesale, independent of the recursive mindist's own internal state). If the broad-phase watcher
   ever destroys and recreates the entire pair (not just its children) on some schedule, that would explain how real IVP
   avoids this exact trap without any of the functions read so far needing to differ from ours.
   **Read `IvpPairWatcher.Refresh` (`FUN_1800b6080`) in full — also ruled out, same day.** It calls
   `IvpPairMindists.Refresh(_first, _second, gap, _pair, null, null, null, null, this)` with every ledge/root argument `null`,
   meaning it re-queries EACH object's FULL surface fresh (not a fixed ledge) — but the surface query for a fixed object
   always returns the SAME hull-root ledge references (`Mesh.Hulls[0]`/`[1]` for the ground, the body's one ledge for the
   body — stable object identities, never rebuilt), so `Keep`'s identity match reuses the SAME top-level recursive mindist
   here too, every time this fires (from the watcher's own periodic hull pass, confirmed by its one other caller,
   `IvpPairWatcherRecord.HullPassed`). **Nothing anywhere in this port ever tears a mindist down purely because time passed
   — every level relies on ledge-identity matching over stable object references, and that is consistent with real IVP's
   actual design** (a pair's mindists persist and get updated in place for the whole life of the pair, not recreated). So
   the "watcher periodically rebuilds" theory is wrong too, for the same underlying reason the seam-straddle theory was
   wrong: it assumed a mechanism this session has now shown does not exist in EITHER the port or, most likely, the shipped
   binary either. **Every plausible source-level candidate is now checked and cleared.** The one experiment left that
   is not more source reading: run this exact fixture's geometry through the real `vphysics.dll` via the paired-drop harness
   below and see whether the SHIPPED ENGINE also produces a permanent fall-through here. If it does, this is not a
   divergence at all — it is a genuine IVP limitation this project has faithfully reproduced, and the fixture's expected
   value (`-4d`) is simply wrong for this exact drop. If the real engine rests correctly, the divergence is real but is in
   a mechanism this project has not identified even by name yet, and static reading has been exhausted for this specific
   question — the next investigation needs to start from a live disassembly TRACE of the real binary on this exact case,
   not another read of already-covered functions. *Not a regression the drop introduced*: the earlier resting contact
   this project's own drop had been (wrongly) keeping was propping the body up over
   this gap the whole time.
   **Run 2026-09-17 (`vphysics-virtual-terrain-drop`, `tools/Tf2DemoSalvage.Probe/Probes/VphysicsVirtualTerrainDropProbe.cs`):
   the same `DisplacementCollisionTree`/field geometry fed to the shipped `vphysics.dll`'s own `IPhysicsCollision::CreateVirtualMesh`
   (its real API, `public/vphysics/virtualmesh.h`), same half-cube, same undamped free fall — the REAL ENGINE RESTS, bouncing once
   around t≈1.1s (v flips from −397.6 to +60.0 in/s), a second smaller correction near t≈1.4–1.6s, settled by t≈1.8s at
   Z≈157.95 in against an expected 157.48 (the port's `-4d` in its own metric scale), and stays parked there through t=3.0s.
   **The divergence is real, not a shipped-IVP limitation this fixture happens to trigger** — the next step is the disassembly
   trace this file already names, not another source read.**
   **2026-09-17, re-run and taken further (still not resolved)**: re-ran `vphysics-virtual-terrain-drop`, same numbers
   bit-for-bit (bounce at t≈1.1–1.2s, −397.6→+60.0, apex, a second free-fall arc t≈1.4–1.6s at exactly gravity's
   −393.7 in/s² with zero impacts, then arrested to ≈0 in/s by t=1.8s at Z=157.95). Brought up a live GhidraMCP headless
   server on the vphysics.dll project (`D:\ghidra-proj\tf2vphysics-collide.gpr`, `D:\ghidra-proj\ghidra-mcp-headless.bat`,
   port 8089 — was not running; start with `pmux new-session -d -s ghidra-mcp`, `pmux send-keys` the batch file, then
   `connect_instance` to project `tf2vphysics-collide`; the tool's own dynamically-registered 214 tools are not reachable
   through this session's tool-call surface, but its plain REST endpoints work directly, e.g.
   `curl "http://127.0.0.1:8089/decompile_function?address=0x180082560"`, POST with a JSON body for endpoints that need
   one such as `read_memory`) and read `IvpEnvironment::RunPipeline` (`180082560`) phase 1 in full for the first time —
   the four call sites this file has flagged as *"not carried... none of which has been read"* since the driver first
   went in. **One is now ruled out with confidence**: `env+0x58`'s guarded call (`FUN_180087e50`) is gated on the SAME
   field `IvpCollisionEnvironment.RangeCallback` already documents as null in vphysics (confirmed the same struct by
   matching `IvpCollisionEnvironment::AdvanceClock`'s `+0x1a0`/`+0x188` writes to the already-ported `Psi`/`Now` fields)
   — this branch never fires, full stop. **The other three are read but not resolved**: `env+0x162`/`env+0x168` is a
   small pointer vector, gated on a nonzero count, each entry's own flags word dispatched by a `kind==8` test through
   `FUN_1800758e0` (relinks the entry between two lists) or `FUN_180075610`→`FUN_180078820` (unread), then clears bit
   `0x0004` on the entry — read `FUN_180089210` and `FUN_180077c80` in full, but not what writes an entry INTO this list.
   `env+0xE0` holds a pointer to a default-constructed object (`FUN_18009f490`, vtable `1800fe460`, read by dumping its
   raw bytes via `read_memory` rather than guessing — slot 10 is `FUN_1800a0070`, called unconditionally every PSI, which
   itself unconditionally calls its own slot 8 `FUN_1800a0280`, draining two more small add/remove queues at its own
   `+0x28`/`+0x38` by calling each entry's slot 1 — reads as a deferred controller add/remove reconciliation, empty at
   construction, no writer found). `env+0x158` (count `env+0x152`) is walked LAST FIRST every PSI, unconditionally,
   calling each entry's own slot 0 with `&env` — population site not found either. **Ruled out as writers for the two
   unresolved lists**, by reading each in full and finding no touch of `+0x158`/`+0x152`/`+0x162`/`+0x168`:
   `IvpCollisionEnvironment::SimulateUnitPsi` (already the ported `IvpSimulationUnit.Psi`), `IvpIntegrator::IntegrateCore`
   (already the ported `IvpIntegrator.StepCore`), `IvpCollisionEnvironment::AdvanceClock`, and the gravity-vector setters
   `FUN_1800824e0`/`FUN_180075320` chained from `CPhysicsEnvironment::CPhysicsEnvironment`'s gravity install (also checked
   `IvpGravity::AddToGravityList`, `1800748b0` — a real, already-named function, but it appends to a PER-CORE list at a
   completely different offset, `core+0x1e0`, not `env+0x158`; a false lead from sharing the array-grow helper
   `FUN_180072ba0`, not from any real connection). **Tried and abandoned the live half of the plan the same day**: set
   out to attach the debugger to the actual probe process mid-run to watch these four fire, or not, in real time. Could
   not get a viable window — measured directly, not assumed: backgrounding a build-then-run of the probe and polling
   `tasklist` every 0.2s never once caught the process alive, meaning the ENTIRE 300-tick run (native calls into a
   32-triangle mesh, trivial per-tick cost) completes in a small fraction of a second of wall clock after the dotnet
   host starts, far faster than this session's own tool-call round-trip latency can win a manual attach race. The
   established fix — a deliberate pause in the probe to buy attach time — is blocked: `Thread.Sleep`/`Task.Delay` are
   refused in any `.cs` file with no carve-out for throwaway probe instrumentation
   (`~/.claude/hooks/block-banned-csharp.ps1`), and this tool environment has no way to feed a blocking `Console.ReadLine`
   real interactive stdin from a backgrounded process. **Net for this pass**: one of four candidates closed (`env+0x58`,
   dead), three read but still open, and the live-fire question still unanswered — so still no concrete, evidence-backed
   root cause, and per this file's own rule no fix was attempted. **Next session, in order**: (1) find the `env+0x158`/
   `env+0x162` write sites with a whole-binary xref sweep on those two field offsets — GhidraMCP exposes
   `get_field_access_context`/`analyze_struct_field_usage` for exactly this and neither got its parameter names right
   this session (a REST/param-naming problem, not a missing capability — `search_tools` lists both as `callable`); (2)
   once found, check statically whether a plain two-object drop (one static virtual-mesh ground, one dynamic poly
   object, no constraints, no phantoms) would ever populate either list — if provably never, both are closed the same
   way `env+0x58` was, with no live run needed; (3) only if (2) says yes, solve the attach-window problem (a
   `Stopwatch`-spin busy-wait behind an opt-in env var is one option that avoids the literal banned APIs) and get the
   trace this file has wanted since the divergence was first confirmed real.
   **2026-09-17, a concrete candidate found via the xref sweep this file asked for**: `search_instructions` for
   `MOV ... [reg+0x162]` across the whole binary (not the field-access tools, which needed parameter names this session
   never got right — plain instruction search worked immediately) turned up exactly four writers. Two are the environment
   constructor and destructor (zero-init and teardown only, not a populate site — already expected). The third,
   `FUN_180089100`, is a REMOVAL: given a core pointer, it searches `env+0x168` (the array `env+0x162` counts) for that
   pointer by identity, removes it, and clears bit `0x4` on the core's own flags word. **The fourth is the real find**:
   `FUN_1800737d0`, called from NOWHERE statically (`get_function_callers` returns none — it is only ever reached through
   a function pointer, exactly matching this file's own unresolved note that `env+0x158`'s list "calls each entry's own
   slot 0"). It takes a COLLISION OBJECT pointer (confirmed: it reads a core at `param+0xe8`, this project's own established
   offset for `IvpCollisionObject.Core`) and does, in order: **if the core's flags have bit `0x4` set, calls the already-read
   `FUN_180089100` to remove it from the `env+0x162` list**; then, if a state byte at `core+1` is under 8, walks the core's
   OWN contact list (`core+0x38`, count `core+0x35` — this project's `IvpCollisionObject.ContactPoints`) and for each
   contact calls **`IvpFrictionSystem::DropContact`, already ported as this branch's `ad4c08ba`** — but then, **if the
   OTHER object in that contact now has zero contacts left (`*(short*)(other+0x7a) == 0`), calls that OTHER OBJECT's own
   vtable slot `+0x38` with argument `1` — a call this port has never made, anywhere.** The rest of the function stamps
   several float snapshots and a "now − 20.0" timestamp, reading as sleep/quiet-timer bookkeeping unrelated to the call
   in question. **This is now the leading candidate**: the contact-drop mechanism this branch already ports correctly
   removes a stale contact, but real IVP's companion step — telling the OTHER object something when it is left with zero
   contacts — has no equivalent anywhere in this port. If that vtable slot is what forces a fresh mindist search (an
   `IvpCollisionObject`-level "your synapses may be stale, recheck" signal, distinct from anything already read this
   session), its absence would explain exactly this symptom: the drop removes the old contact correctly, and nothing
   ever tells the body's object it needs to look again. **Not yet confirmed**: what vtable slot `+0x38` actually does
   (needs a decompile of `IvpCollisionObject`'s vtable at that slot — not yet read this session), and what registers a
   collision object into the `env+0x158` list in the first place (still the one open population-site question, now
   narrowed to "whatever calls this exact function's caller," not the whole list mechanism blind). Read slot `+0x38`
   before writing any code — a wrong guess about what it does would be exactly the invented-mechanism mistake this
   investigation has repeatedly avoided.
   **2026-09-17, the slot `+0x38` question turned out to be the wrong branch to chase — read `IvpFrictionSystem::DropContact`
   (`180083e40`) itself in full instead, and it points somewhere already flagged in this codebase.** `DropContact` does two
   independent things when a contact leaves a core's per-system tally at zero: it calls `FUN_180088c80` (already the ported
   `IvpFrictionSystem.Leave`, confirmed correct) — and, separately, for EACH of the contact's two cores whose own
   friction-info contact count (`+2` on the info) reaches zero, it reads `core+0x1f8` — **this project's own established
   `IvpRigidBody.Unit` field** — dereferences to the unit's flags word (`unit+0x0`) and does
   `flags = (flags & ~0x200) | 0x100`. **`IvpSimulationUnit.cs:57`'s own doc comment, written in an earlier session, already
   names this exact gap**: *"the flags word, unit+0x0, whose 0x400/0x3000 bits carry a fast spin into the next PSI... what
   sets the 0x300 pair is not read yet — the PSI only clears it once it has rebuilt."* `0x100`/`0x200` are precisely the
   `0x300` pair. **This port never sets these bits anywhere** — `RemoveContact`/`RemoveFromPair`/`RemoveCoreContact` in
   `IvpFrictionSystem.cs` touch contacts and cores but never a core's `Unit.Flags`. And per `RebuildEntries`'s own doc
   comment (line 127, also pre-existing): *"the third routine the unit's 0x300 bits also call, `FUN_180074e80`, is
   unread"* — meaning the PSI's dispatch on a unit with `0x300` bits set calls BOTH `RebuildEntries` (ported) AND a second,
   never-read function, before clearing the bits. **This is now the strongest candidate found this session**: dropping a
   contact is supposed to flag the loser's simulation unit for a rebuild-plus-something-else next PSI, and this port drops
   the contact (correctly, `ad4c08ba`) without ever raising that flag — meaning whatever `FUN_180074e80` does for a
   just-emptied core's unit never happens here. **Not yet confirmed**: what `FUN_180074e80` actually does, and where in
   the PSI loop the `0x300` check itself lives (search for where `IvpSimulationUnit.Flags`'s callers already check other
   bits, or find `FUN_180074e80`'s callers directly — it may only be called from that one dispatch site, same as
   `FUN_1800737d0` had none). Read both before writing any code.
   **2026-09-17, `FUN_180074e80` read in full — it is a UNIT-level split, distinct from `IvpFrictionSystem.Split`.** It
   caches each core's friction-system root (via what reads as `IvpFrictionSystem.RootOf`, `FUN_1800878d0`) at a field this
   session has not otherwise named (`core+600`/`0x258`), zeroed for every core in the unit first. Then it checks whether
   EVERY core in the unit still shares the SAME root as the first — **if any core's root differs, the unit's cores no
   longer form one connected friction-system component, and it calls a three-step sequence**: `FUN_180074ba0` (already
   this project's own `RebuildEntries`'s first bookend, per that method's own doc comment), then `FUN_1800761c0`
   (**still unread — almost certainly the actual unit-split, given the one differing-root core it is handed**), then
   `FUN_180075470` (`RebuildEntries`'s second bookend). **This means `RebuildEntries` alone is not the full picture**:
   the doc comment on it citing "`FUN_180074ba0` then `FUN_180075470`" was describing this function's OUTER bookends,
   with the actual split (`FUN_1800761c0`) sandwiched between them — so `RebuildEntries()` as currently written may be
   doing the rebuild-bookend work but is missing the split step entirely, even where it IS already called.
   **This is a real, load-bearing missing mechanism, not a guess**: a body that just lost its last contact (dropped by
   the already-ported `DropContact`) needs its unit checked for a split so it stops being simulated as part of a group
   it no longer belongs to. Whether this specific gap is what blocks the second impact detection in
   `Advance_ABodyDroppedOnVirtualTerrain_ComesToRestOnIt` is NOT yet confirmed — that requires reading
   `FUN_1800761c0` (the actual split), finding where in the PSI loop a unit's `0x300` flags are checked to trigger this
   whole dispatch (still unread), and testing whether porting all three (the `DropContact` flag-set, the `0x300` PSI
   check, and the real split in `FUN_1800761c0`) makes the failing test pass. Per this project's TDD/sabotage rules,
   port and test before declaring this the fix.
   **2026-09-17, this candidate closed — read `FUN_1800761c0` in full and proved, not guessed, that it cannot explain
   this test.** `FUN_1800761c0` is the unit-level split: given a unit and a differing core-root `r` (from
   `FUN_180074e80`'s own scan), it allocates a brand-new `IvpSimulationUnit` (`FUN_180005480(0x48)`, initialized
   asleep/state 8, exactly as this port's own `Add()` does), files it via `FUN_1800749f0(root's own +0x10 list,
   newUnit)`, walks the ORIGINAL unit's cores moving every one whose root equals `r` into the new unit's own array
   (retargeting each moved core's `+0x1f8`/`Unit`), rebuilds the new unit's entries (`FUN_180075470`), and loops again
   (a `do...while`) if a THIRD distinct root turns up among what is left, so one PSI can split a unit more than once.
   **`FUN_180074e80` re-read to confirm the dispatch it guards**: it re-derives each core's union-find root every call
   (zeroing `core+0x258`/`UnionParent`, then for every controller entry merging that controller's OWN touching-core
   set — the same union-find shape `IvpFrictionSystem.DetachedRoot` already uses), and ONLY IF a core's root differs
   from the unit's first core's does it call `FUN_180074ba0`→`FUN_1800761c0`→`FUN_180075470`; **if every core still
   shares one root it returns having done nothing at all, not even a rebuild.** **`180083e40` (`DropContact`) read
   again in raw disassembly, not decompiled paraphrase, to pin the mask**:
   `**(uint**)(core+0x1f8) = **(uint**)(core+0x1f8) & 0xfffffdff | 0x100` for each of the contact's two cores whose own
   friction-info count hits zero — `0xfffffdff` is exactly `~0x200`, confirming the earlier summary's
   `(flags & ~0x200) | 0x100` bit for bit. **But this port's `IvpFrictionSystem.Leave` already carries an equivalent
   write** (committed at `52e83459`, before this investigation began) **and it turns out not to be the operative one
   for this test at all**: `IvpNormalFrictionController.Advance`
   (`managed/Tf2DemoSalvage.Animation/Animating/IvpFrictionController.cs:212`, already ported, priority 0, runs last
   every PSI) sets the identical bits UNCONDITIONALLY every PSI a friction system's normal face runs — contact or no
   contact, split or no split — matching that file's own pre-existing doc comment on `FUN_180084320`, *"either way the
   unit's dword loses bit 9 and gains bit 8."* **So `Flags & 0x300` is already true on essentially every PSI once a
   body has any live friction system**, not only transiently after a drop, and `IvpSimulationUnit.cs:309`'s
   `if ((Flags & 0x300) != 0) { RebuildEntries(); ... }` already runs (as a no-op rebuild) on that same cadence.
   **Why the chain still cannot be this test's gap, proven rather than guessed**:
   `Advance_ABodyDroppedOnVirtualTerrain_ComesToRestOnIt` has exactly one dynamic body — `ground` is `Immovable` and is
   never passed to `IvpSimulation.Add`, so it never gets a `Unit` at all (confirmed by reading `Add`, the only place a
   `Unit` is constructed). `FUN_1800761c0`'s split needs ≥2 cores in one unit with DIFFERING roots; with exactly one
   core, `FUN_180074e80`'s root scan compares the core's root against itself and can never diverge — the split is
   mathematically unreachable, and separately, a full rebuild versus the engine's real no-op-when-no-divergence branch
   produce an IDENTICAL `Entries` list for a one-core unit, so neither change can move this test's outcome even ported
   byte-for-byte. **Checked whether any OTHER test could exercise it, so this isn't scoped too narrowly**: `Absorb`
   (the only path that ever puts two cores in one unit) has exactly one caller in the whole codebase
   (`IvpSimulation.cs:355`, the constraint-registration path), and no test under `tests/Tf2DemoSalvage.Animation.Tests`
   calls the constraint API at all (grepped) — this mechanism is unreachable by the ENTIRE test suite, not just this
   one test. **Per this file's own hard-stop rule, not implemented speculatively**: porting `FUN_180074e80`/
   `FUN_1800761c0` for real needs a new `IIvpUnitController` member (the vtable `+0x10` "this controller's own
   touching cores" call `FUN_180074e80` makes) implemented on every controller — drag, gravity, the three friction
   faces, constraints — with no disassembly read yet for what gravity/drag/constraints return there, and no test,
   existing or constructible from this session's findings, that would give it ground truth; building it now would be
   exactly the untested, unverifiable churn this project's TDD/D38 rules are against, for a mechanism proven inert for
   the actual gap. **This candidate is closed, not "still open"** — nothing here contradicts earlier findings; it
   confirms `RebuildEntries`/`Split`/`DropContact` are each independently correct where already ported, and simply
   cannot be the cause. **Still open, unchanged from before**: why the second impact never re-arms after the first
   bounce. Every source-level candidate this file has named to date (`HullPassed`, `Recheck`/`RecheckEveryPsi`,
   `RecheckInvalid`, `BecomeExact`/`Freeze`, the parent's own `HullPassed` condition, `IvpPairWatcher.Refresh`, and now
   the unit-split/`DropContact` flag chain) is individually confirmed byte-for-byte faithful, and none of them is the
   divergence. The next investigation needs the live disassembly trace of the real binary on this exact case that an
   earlier entry in this file already named and could not get a viable attach window for — that blocker (measured, not
   assumed: the whole 300-tick run completes faster than a manual attach can win the race, and
   `Thread.Sleep`/`Task.Delay` are refused in any `.cs` file) is still standing.
   **2026-09-17: the attach-window blocker itself is fixed, but a second, different blocker replaced it — the
   live trace still did not happen.** Added `TF2VPHYSICS_PROBE_PAUSE_MS` to `VphysicsVirtualTerrainDropProbe.cs`
   (opt-in `Stopwatch` spin-wait, no `Thread.Sleep`/`Task.Delay`, printing `pid=<n>` before the wait and a
   confirmation line after it), verified working stand-alone (`pid=10612`, a 2s pause, then the identical
   trajectory this file already has on record — bounce at t≈1.1–1.2s, second correction t≈1.4–1.6s, settled
   `Z≈157.95` by t≈1.8s — bit-for-bit the same run, so the pause changes nothing about the physics). Launched it
   in the background (`pmux`, a 45s pause) and got a real PID with time to spare — the race this file previously
   lost is solved. **`debugger_attach` then failed for an unrelated reason**: `"Debugger server not running at
   http://127.0.0.1:8099. Start it with: uv run python -m debugger"`. This is a SEPARATE process from the
   already-running GhidraMCP headless REST server (port 8089, static analysis only) — `debugger_attach` proxies
   through `bridge_mcp_ghidra.debugger` (the installed uv tool, `C:\Users\pinku\AppData\Roaming\uv\tools\ghidra-mcp-bridge`)
   to a second, standalone "debugger server" (its own docstring: `debugger/server.py`, wraps dbgeng/WinDbg via
   `pybag`, Windows-only) that this machine does not have installed anywhere. **Searched and confirmed absent,
   not just unrunning**: the installed `ghidra-mcp-bridge` venv's `site-packages` (only `bridge_mcp_ghidra`, which
   proxies to it but does not embed it — no `pybag`, no `debugger` package), `D:\ghidra-proj` and its
   `mcp-install` bundle (`INSTALLATION.md` documents only the Ghidra extension zip and the bridge wheel, nothing
   about a debugger server), the published upstream repo `bethington/ghidra-mcp`'s own `python/bridge_mcp_ghidra`
   directory (same set of files as the installed venv — no `debugger/` package there either, confirming it is
   not part of this open-source project at all), and `Documents`/`Desktop`/`source` for any locally-authored
   `pybag` script. **This is a hard environment gap, not a further disassembly question**: standing up a
   dbgeng/WinDbg bridge from scratch is its own substantial piece of software (equivalent in scope to writing a
   new debugger front end), squarely out of scope for a single investigation pass and not something to improvise
   under this project's own no-invented-mechanism rule. **Net for this pass**: the previously-blocking attach
   race is now solved and the fix is kept in the probe permanently (harmless, opt-in, `TF2VPHYSICS_PROBE_PAUSE_MS`
   unset in every normal run).
   **2026-09-17, unblocked and answered the same day**: the missing debugger backend was a real gap (correctly
   identified above), not something to build from scratch — a working, actively-maintained one already existed
   publicly (`github.com/miscusi-peek/cheatengine-mcp-bridge`, source read in full before use: clean, loopback-only
   by default, no exfiltration, its dangerous surface — code execution, DLL injection, kernel/CR3 access, input
   injection — hard-denied via this repo's own `.claude/settings.local.json` permission rules rather than left to
   convention). Attached live to `vphysics-virtual-terrain-drop` (the probe's own `TF2VPHYSICS_PROBE_GO_FILE`
   opt-in, added alongside the pause fix, lets a real debugger signal "armed" instead of guessing a wait duration)
   and set non-breaking hardware breakpoints, with stack capture, on `IvpImpactSolver`'s entry (`18008e290`),
   `IvpFrictionSystem::DropContact` (`180083e40`) and `IvpPairMindists::Refresh` (`180096680`). **Result, and it
   changes the shape of the whole investigation**: the 3 `IvpImpactSolver` hits
   from the earlier Lua-based pass are now proven — not inferred — to be ONE bounce, not three: all three share
   bit-identical `RSP`/`RBP`/`R14` (`0xBCB27DDDE8`/`0xBCB27DDEF0`/`0x205AACA00D0`), meaning the same call site, same
   stack frame, three loop iterations — the cube's three corners resolved together. **`IvpFrictionSystem::DropContact`
   fired exactly twice in that same clustered moment, also from an identical stack frame** — meaning the real
   engine's single bounce drops 2 of the cube's 3 corner contacts and KEEPS ONE. There is no evidence anywhere in
   this trace of a second, separately-triggered impact or a "re-arm" signal — the entire event (solve three
   corners, drop two, keep one) happens as one continuous pass. **This reframes the bug**: the missing mechanism
   this file has spent all day hunting (unit-split flags, `FUN_180074e80`, vtable slot `+0x38`, `IvpPairWatcher`)
   may not exist because there is nothing to re-arm — the "second correction" seen in the original probe's
   trajectory (t≈1.4–1.6s) is plausibly just the ordinary push-out/friction convergence of the ONE contact real
   IVP keeps, not a special re-trigger at all. **The new, sharply testable hypothesis**: check whether this port's
   own contact-drop logic (`ad4c08ba`) drops all 3 corner contacts to zero instead of keeping 1 — if so, that
   plain over-aggressive drop, not a missing re-arm signal, is the entire bug.
   **2026-09-17, confirmed with a targeted port-side trace (temporary `Console.WriteLine`, removed before this
   commit): the port's `bodyObject.ContactPoints.Count` and `body.FrictionInfo` are `0`/`null` at BOTH t=1.2 (right
   after the bounce) and t=3.0 (end) — every corner contact is gone, matching the hypothesis exactly.** Read
   `IvpFrictionSystem.File()` (the heap's filing pass, `FUN_1800a9bf0`) in full — **it is not the bug**: its drop
   condition (`point.Gap >= IvpCollisionTolerance.RestingContactGap || record.Outside`) matches the disassembly
   exactly, evaluated independently per contact, no different from what the real engine's own filing pass does.
   **The bug is therefore not in the drop condition's LOGIC — it is that this port's 3 corner contacts all end up
   with a `Gap` that trips the same threshold, while the real engine's 3 corners come out with 2 past it and 1
   under it.** The real engine's DropContact calls fired from an identical stack frame just like ImpactSolver's
   did, meaning even there the mechanism is uniform across corners — so whatever makes ONE corner's gap smaller
   in the real engine has to be a genuine PHYSICAL difference (most likely a small angular/rotational discrepancy
   putting one corner measurably closer to the ground at the exact instant this filing pass runs), not a branch
   this port takes differently. **Next step, now within reach with the live debugger working**: read each
   corner's own `IvpContactPoint.Gap` (or its underlying mindist `Length`) individually — in the real engine via
   `read_memory`/`read_integer` at the contact-record addresses already visible in the `DropContact` breakpoint's
   own register capture (`RCX`/`RDX` held the record/system pointers), and in the port via the same kind of
   temporary per-contact trace already used above — to find where the three corners' numbers actually diverge
   between the port and the real engine. That comparison, not more reading of `File()` or anything upstream of
   it, is what will actually locate the fix.
   **The measurement to work from** is the paired `.phy` drop (`vphysics-drop phy` / `ivp-phy-drop`, findings 51, *One prop
   dropped through vphysics.dll and through the port*): the two runs match through free fall, and **first differ at the impact on
   tick 20** — the port lands 0.25 lower and spins at under half vphysics' rate. After that vphysics comes to rest by 1.5 s and
   the port keeps sliding at 4–7 units/s until the settle check sleeps it, so resting at the same height is not a match. Nothing
   switches over from `IvpEnvironment.Simulate()` before that differential is closed and the f12 ragdoll drop is compared.
   *The account below is the 2026-09-14 reading that got this far, kept because it names what was unread at the time; where it
   says a routine is unported, check the list above first.* `IvpEnvironment.Simulate()` (897 lines,
   `managed/.../IvpEnvironment.cs`) is a fully independent,
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
   - **`FUN_18008da40` and `FUN_180090240` read in full, 2026-09-15 — neither gates the loop, and the exact exit
     condition is now pinned exactly, quoted:**
     ```c
     uVar7 = FUN_180090bd0(param_1);
     iVar6 = (int)uVar7;
     do {
         if (iVar6 != 1) {
     LAB_180090891:
             *(int *)(*param_1 + 0x98) = *(int *)(*param_1 + 0x98) + iVar9 + 1;
             IvpEnvironment__IntegrateAwakeCores(param_1);
             return;
         }
         *(int *)(param_1 + 1) = (int)param_1[1] + 1;
         iVar9 = iVar9 + 1;
         if (5000 < (int)param_1[1]) {
             if (param_2 != (undefined8 *)0x0) {
                 (**(code **)*param_2)(param_2,1);
             }
             goto LAB_180090891;
         }
         uVar7 = FUN_180090bd0(param_1);
         iVar6 = (int)uVar7;
     } while( true );
     ```
     Continues only while `FUN_180090bd0` returns exactly `1` AND the retry counter stays `≤ 5000`; on any other
     return, or past 5000, fires an anomaly callback (if one is registered) then re-enters `IntegrateAwakeCores` and
     returns — confirming there is no separate "give up" branch, both exits converge on the same tail call.
     `FUN_18008da40` (called twice before the loop, once per side of the pair) only populates the touched-cores and
     queued-contacts arrays the loop's own body reads; `FUN_180090240` (`IvpImpactSolver::ChoosePush`) is two levels
     removed, called only from inside `IvpImpactSolver::Solve`, and picks/normalizes a push direction — unrelated to
     the retry count. **`FUN_180090bd0` now read and quoted verbatim, 2026-09-15 — it is NOT close to pure
     orchestration; it is a genuine new state object, matching the scale every other subsystem this size in this
     project has earned its own dedicated build.** Confirmed:
     - **The worst contact is a MINIMUM search** over each pair's contacts' `record.PredictedGap` (the `Estimate`
       output already ported), seeded from a native float constant (`DAT_18012d648`, not yet pinned), skipping a
       pair entirely when a bitmask over two flag bytes at pair `+0x38`/`+0x40` is nonzero (a "both bodies
       frozen/disabled" gate, not yet named) and skipping `Estimate` itself for a contact whose own cache-valid flag
       (a short at record `+0x74`) already reads `1` this pass.
     - **Return convention: `0` means nothing to solve** (empty pairs list, or every contact filtered out) — retry
       loop stops; `1` means a worst contact was found, `IvpImpactSolver.Enter` ran on it, and a body-list/
       friction-info bookkeeping pass ran — retry loop continues. No third value.
     - **It needs a NEW, multi-field growable state object**, not just a method call: a pairs-to-scan list
       (`param_1[7]`, count at `+0x32`), a "bodies changed this call" list (`param_1[5]`, count at `+0x22`, grown via
       the unread `FUN_180072ba0`), and the per-contact cache-valid flag (record `+0x74`) that this same function
       both reads (skip re-`Estimate`) and, after a successful `Enter`, RESETS TO ZERO for every contact of every
       body `Enter` returned as touched — via `IvpRigidBody.FrictionInfoIn` (already ported) walking that body's own
       `IvpFrictionInfo.Contacts`. A touched body is also passed to `RebuildMatrixAtEventTime` (already ported) when
       its own flags say it needs one (`byte[1] < 8 && (byte[0] & 0x10) == 0`, neither offset named yet), and to
       `FUN_18008da40` (read in full above) when it has no active friction-system link.
     - **`IvpImpactSolver.Solve` is not called anywhere in this function** — only `.Enter`. Whatever calls `.Solve`
       for this path is still unlocated (possibly inside `Enter` itself, already ported — needs checking against
       the existing port rather than assumed).
     - **Not yet named**: `DAT_18012d648` (the search's seed constant), the pair-flag bitmask's real meaning, the
       `param_1` retry-context struct's own full layout beyond the fields this function touches, and
       `FUN_180072ba0` (the grow-list helper, called from both this function and `FUN_18008da40`).
     - **This closes the reading for `FUN_180090700`'s retry loop itself** — the exit condition (above) and this
       function's own mechanics are both now pinned to the source instructions, not summarized. **What remains is a
       genuinely new build**: a retry-context type carrying these growable lists, matching the scale of every other
       subsystem this project has given its own dedicated multi-session port (heap solve, impact solver, tangential
       solve) — not something to add in the same pass as reading it.
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
     this session had doubted it. *It then claimed `IvpMindistCollide.Collide` was "correctly complete as written",
     ending at `IvpImpactSolver.Enter`, with the island left to the PSI driver.* **Wrong, corrected 2026-09-15**: reading
     `FUN_18008ef60` whole (`docs/findings/51`, *The collision around the impact loop*) shows the collision itself calls
     `FUN_180090700` after the first solve, saving and restoring the record's relative velocity around it. `Collide` now
     runs `IvpImpactIsland.Build`, which tails into `IvpImpactIsland.Tail`.
   - **Done, same session: `IvpLedgeSide.FromLedge`** closes the "build fresh sides from a live object" gap — not
     read from the disassembly, this project's own assembly of already-decoded pieces (`PhysicsLedge`'s fields map
     directly onto `IvpLedgeTopology`'s constructor). 1 test, no port gap left here.
   - **Found, same session, reading toward the per-PSI friction solve: the TANGENTIAL solve is a whole separate
     unported subsystem, roughly the size of the already-oracle-backed normal-push heap solve.**
     `IvpFrictionSystem::SolveOncePerPsi` (`1800836b0`, read in full) walks every `Pairs` entry, and each pair
     apparently keeps its OWN array of contact records touching it (a field `IvpFrictionPair` does not yet carry —
     `IvpFrictionPair::Build`'s deferred extra fields, revisited: they may not be as skippable as the normal-push path
     made them look), computes a per-pair friction cone budget from summed `NormalPush × Friction ×
     IvpContactPoint.InverseContactMass` (`+0x60`, its writer read 2026-09-15 — see the CLOSED note further down),
     clamps each contact's `Slide` against it, then dispatches per contact to `FUN_180085100` (unread, a
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
   - **Three of the four tangential-solve dependencies read in full, 2026-09-14 — confirms the subsystem's true
     scale.** `IvpContact::TryInvertSymmetric` is trivial (a plain 2×2 matrix inverse, determinant-guarded). But
     `IvpContact::TangentialSlipVelocity` turned out to be misnamed by Ghidra's heuristics — it builds a jacobian
     solver scratch structure and calls the real work, `IvpRigidBody::BuildJacobian`, which is **large and dense**:
     up to three tangent-axis rows, each a cross product, a matrix rotation, and an inverse-inertia-weighted
     mass-matrix/RHS accumulation — the same scale as anything in `IvpImpactSolver`. **This puts the tangential
     solve's scope beyond doubt: it is a full subsystem, not a handful of small functions.** Only `FUN_18009c620`
     (apply the impulse) and `FUN_180085a80` (an early-out branch) remain unread, but the shape is now completely
     clear and does not need more reading to be believed.
   - **The last two functions read in full, 2026-09-14 — the tangential subsystem's disassembly is now completely
     closed.** `FUN_18009c620` (apply the two-axis impulse) is straightforward given `BuildJacobian`'s jacobian rows —
     a plain matrix-vector accumulation into each core's pending velocity/spin. `FUN_180085a80` (the sticking
     branch's real work) is **the most complex function read this entire session**: computes a slide direction,
     builds jacobians for both sides via `IvpContact::TangentialSlipVelocity`, inverts the 2×2 system, and then
     branches on an "anchor" state (`+0xb0`) between a sliding-friction-limited response and an anchored one, each
     scaling by a material sliding-friction factor and a damping term (`IvpDamping::Damp`, already ported). Surfaces
     **three more unread functions** (`FUN_18008fb60`, `FUN_180070950`, `FUN_18006dcb0`) that a full port would still
     need — not chased further this session. **The scope is now unambiguous and needs no more reading to be
     believed**: this is a subsystem at or beyond `IvpImpactSolver`'s own complexity, and belongs in its own
     dedicated, multi-session, oracle-backed port, exactly like every other subsystem of this size in this project.
   - **Real progress landed, 2026-09-14: six pieces of the tangential solve are ported and tested in
     `IvpTangentialSolve.cs`.** `TryInvertSymmetric` (the 2×2 inverse), `BuildJacobian`/`IvpJacobianRow` (one core's
     row and mass response, simplified to exactly two axes since the native's third-axis capacity is dead in every
     call site this project reaches), `ApplyImpulse` (stages the found impulse into a core's pending push),
     `CrossTerm`/`System` (assembles the 2×2 system from both cores' diagonals and off-diagonal contributions), and
     `RelativeVelocity` (the current-slip half of the solve's right-hand side, via the already-ported
     `IvpRigidBody.PointVelocity`). 26 tests, all with hand-computed exact values (one caught a real test-math error
     — the `w=1` lane isn't scaled by inverse inertia — not a code error). The three small newly-surfaced functions
     (`FUN_18008fb60`, `FUN_180070950`, `FUN_18006dcb0`) turned out to all be trivial and already covered by existing
     ported equivalents (material lookup, matrix rotate, cross product) — no further reading needed for them.
   - **`SolveTangentialPair`'s non-sticking branch is now fully ported and tested** (`IvpTangentialSolve.Solve`,
     `ClipImpulse`) — 5096/5097 passing, zero regressions. The slip-target mixing question above is now **resolved,
     not merely designed around**: a fresh 2026-09-14 read of `SolveOncePerPsi` (`1800836b0`) and `SolveTangentialPair`
     (`1800857c0`) confirms the stored slide is used AS-IS — `rhs = inverseStep × Slide − RelativeVelocity`, no basis
     rotation from a stale axis pair, because this project recomputes both tangent axes fresh every PSI
     (`IvpMindistCollide`) rather than caching them across PSIs the way the native's axis-reuse case would need one.
     `ClampSlide`'s pre-clamp formula is likewise confirmed byte-for-byte against `SolveOncePerPsi`'s own instructions.
   - **`SolveContact` landed, 2026-09-14: the tangential solve now runs end to end for one contact**
     (`IvpTangentialSolve.SolveContact`) — `Solve`, `ClipImpulse` and `ApplyImpulse` wired together off a contact's
     own `IvpContactRecord` fields, confirming `IvpContactRecord.Build` is the SAME native function
     (`18008d0c0`) that computes the tangent axes (`Span`/`CrossSpan`) and both arm vectors (`FirstArm`/`SecondArm`)
     `SolveTangentialPair` reads — there is no separate axis/arm builder. The material axis-friction factor
     (`BuildJacobian`'s `+0x40/44/48/4c`) is identity for the ordinary isotropic case; a contact using
     `IvpContactPoint.UsesMaterialAxes` throws rather than guess a factor nothing has confirmed. 4 new tests,
     5100/5101 total. **What `SolveContact` still does NOT do**: compute or receive the pair's own friction-cone
     budget — its caller must still pass one in.
   - **`SolveOncePerPair` landed, same day: the per-pair walk itself is ported and tested** (`IvpTangentialSolve.SolveOncePerPair`)
     — for each of `IvpFrictionPair.Contacts` (confirmed already filed by `IvpFrictionLinking.LinkContactByCore`, a
     stale doc comment on the property said otherwise and was corrected), clamps the slide against a caller-supplied
     budget (carrying the excess across contacts) and solves it via `SolveContact`. **The `+0x91`/`FirstMeasure`
     question is resolved, not a collision**: one writer (`IvpContactGeometry`'s measures) clears it, the other
     (this clamp, now ported) sets it — an ordinary flip-flop, read as a re-arm: a contact whose slide was just
     clipped has its slide/position history treated as fresh again next PSI. 3 new tests, 5103/5104 total.
   - **CLOSED 2026-09-15**: the per-pair friction-cone budget's third multiplicand —
     `Σ contact[+0x88] × contact[+0x78] × contact[+0x60]`, scaled by the step squared. `+0x60` is the contact's inverse
     effective mass, written by `IvpContactPoint::Weigh` (`FUN_180083a60`) from `FUN_180077840(core, arm)` per core;
     `docs/findings/51`, *The cone budget's third factor, found*, has both quoted. **The search that found it was over the
     DISASSEMBLY for stores to `+ 0x60],XMM`** — the decompiled C never shows the write, which is why a 2026-09-14 search
     "specifically" for the writer came back empty and concluded, wrongly, that it might be arena-zeroed. *The stale reading
     below is kept for that lesson.* It read: the constructor zeroes
     every neighbouring field but conspicuously skips it, and `SetMaterials`'s own `+0x60`/`+0x68` writes are on a
     different struct entirely (the materials-pair output, not the contact). Either arena-zeroed (making the whole
     multiplicand a no-op) or a caller not yet located — `SolveOncePerPair` takes the budget as a parameter rather
     than guess. (2) `SolveTangentialPair`'s tangent axes/arms/material factors come from `IvpContactRecord.Build`
     (confirmed the SAME function as the already-ported `IvpContactRecord.Build`, not a separate builder) — resolved,
     not open. (3) the sticking dispatch (`FUN_180085a80`, contact `+0x64`) is not carried; `SolveOncePerPair` always
     takes the non-sticking branch.
   - **`FUN_180085a80`'s complete, exact decompile obtained 2026-09-14** — three prior reads this session came back
     garbled or truncated (marked `...` mid-arithmetic); the fourth, demanding the raw untruncated Ghidra output
     verbatim, succeeded. Confirms/adds to what was already known:
     - `IvpContact::TangentialSlipVelocity` (already identified as `IvpRigidBody::BuildJacobian` under Ghidra's own
       naming) is called TWICE, once per side of the contact (`param_2`'s core, and the core reached through
       `*(longlong*)(**(longlong**)(lVar2+8)+0xe8)`), each writing into its own 30-qword output block; a value
       adjacent to the SECOND block's tail (`local_138`) is read as a divisor with no visible assignment in this
       function's own body — almost certainly written by the callee the same way the first block's `local_270`
       (read as `dVar4`) is, i.e. `BuildJacobian`'s own accumulation into a caller-provided struct, not a bug or a
       missing read.
     - `IvpContact::TryInvertSymmetric` is called with `(a, b, b, d)` — the SAME `(A, B, D)` shape this project's
       own `IvpTangentialSolve.System` already produces by summing both cores' diagonals/cross terms — confirming
       the sticking branch's 2×2 system is built the identical way the non-sticking branch's is, just via the
       native's combined jacobian-builder rather than this project's own decomposed `BuildJacobian`/`System` calls.
     - Two magic constants read exactly: `0.8999999761581421` (a friction-coefficient reduction factor, sliding
       branch) and `0.30000001192092896` (used twice: a base friction-limit scale, and inside `dVar5`'s own
       computation alongside an as-yet-unidentified `local_138` divisor).
     - **CLOSED, 2026-09-14, as D175: the sticking branch is a stated, permanent divergence, not an open port
       item.** Five dedicated reads across this session found only readers/null-checks of the field that gates it
       (`core+0x58`, a per-pair "sticking anchor" pointer) — in `SolveTangentialPair`, `FUN_180085a80`, and
       `IvpMindistManager::Revalidate` — and no write anywhere reached. Its likely owner is `vphysics.dll`'s
       joint/constraint code, never opened this session. **This project ports no joint/constraint system and
       implements only body-against-world contact**, so nothing in this codebase, ever, writes that field — the
       sticking branch's dispatch condition is therefore provably always false here, not merely unported.
       `SolveContact`/`SolveOncePerPair` taking only the non-sticking branch is the COMPLETE, correct behaviour for
       this project's object model, not an approximation. See D175 for the full reasoning; re-open only if a
       body-against-body port is ever undertaken.
   - **`vphysics-friction-solve` probe built, 2026-09-14 — a real, working native-interop harness, one genuine
     divergence found and fixed, full numeric parity not yet reached.** Calls `SolveTangentialPair` (`1800857c0`)
     directly in process against a fabricated body-against-world contact, reusing `VphysicsHeapSolveProbe`'s own
     confirmed struct offsets. **Confirmed D175 empirically, not just from static reading**: the control case's own
     core, freshly allocated with `+0x58` never written (exactly as this project's own cores are), takes the binary's
     non-sticking dispatch every time. **Found and fixed a real divergence**: `SolveTangentialPair`'s own instructions
     (read in full, untruncated, after this was chased) compute the impulse's clip budget ENTIRELY from the contact's
     own fields — `NormalPush × Friction × Step` — with no reference to any pair-level aggregate; `SolveContact` was
     wrongly reusing `SolveOncePerPair`'s caller-supplied pair budget for this clip, the same value `ClampSlide` uses
     for the STORED SLIDE's own separate, position-domain pre-clamp. Fixed: `SolveContact` no longer takes a `budget`
     parameter at all — it computes its own clip budget internally, and its entry gate (refusing a contact below
     roughly `1e-6`, matching a contact the heap solve never pushed having no friction to give) is now ported too.
     5 tests updated/added, 5106/5107 total, zero regressions.
   - **Chased further, same session: two real fields found missing, one real bug found and fixed, the exact remaining
     blocker now narrowed to one specific unread function.** `IvpContact::TangentialSlipVelocity` (`18009ca70`) and
     `IvpRigidBody::BuildJacobian` (`18009d010`) were both read in full to find what the probe was missing. Found:
     **`BuildJacobian` computes its arm as `recordPosition − corePosition`, both WORLD-space doubles at record`+0x0`
     and core `+0xf0`** — never from `FirstArm`/`SecondArm` at all, which the probe was wrongly supplying. Fixed the
     probe (writes `record.Position`/`core.Position` now) — the arm is no longer degenerate. **Also found and fixed
     a real port bug, independent of the probe's own remaining gap**: `BuildJacobian`'s native computation crosses
     the WORLD arm against the WORLD axis, then rotates the cross product by `RotateInverse` into the core's frame;
     since rotation commutes with the cross product, this equals crossing the already-local `FirstArm`/`SecondArm`
     against the axis rotated into local space FIRST — the fix `IvpTangentialSolve.Row` now applies. The previous
     version crossed the local arm against the WORLD axis directly (a frame mismatch) then rotated FORWARD (the
     wrong direction) — silently correct only under an identity `CoreMatrix`, which is why no existing test caught
     it. 35/35 tangential tests unaffected (all use identity rotation), confirming the bug never showed up in a
     synthetic fixture — only a rotated-core case, which nothing has built yet, would have caught it.
   - **With both those fixes applied, the probe's control case STILL returns an untouched core** — a diagnostic read
     (raw packed return `0x00000000`, the running-average field at contact `+0x84` unwritten) confirms
     `IvpContact::TryInvertSymmetric` is the one returning false, not an earlier gate (the entry gate reads back as
     `0.005`, correctly computed and well above its `~1e-6` threshold).
   - **`IvpContact::TryInvertSymmetric` read fresh, confirmed byte-for-byte correct — it is NOT the bug.** `det = a·d
     − b·c` (the call site passes the same `b` twice), guard is `det² ≥ 1e-38` exactly as ported, return convention
     is `true = inverted, outputs written`, and the function is pure — 4 doubles in, 4 output pointers, nothing else
     read. This exonerates `TryInvertSymmetric` entirely; whatever is wrong is in what reaches it.
   - **Hand-tracing `BuildJacobian`'s dot products by hand to recover the exact `(a, b, d)` it passes turned out to be
     unreliable** — a second, more careful pass through the same decompiled lines this session produced a DIFFERENT
     `(a, b, d)` (`2, −1, 3`, determinant 5) than the first pass's `(2, −1, 2)` (determinant 3), both still clearly
     non-singular, and still not matching the binary's own false verdict. **This is the actual lesson**: hand-tracing
     five and six layers of scrambled dot-product decompile, each pass one keystroke from mislabeling which `dVarN`
     feeds which output slot, is not a reliable way to recover ground truth here — a wrong-by-construction hand trace
     that still "looks nonzero" is exactly the kind of confident-but-wrong reading this project's own instrument
     discipline warns about. **Stopped rather than keep guessing.**
   - **Tried the emulator, same session: `emulate_function` on `BuildJacobian` (`18009d010`) with the probe's exact
     fabricated buffers, mapped fresh at `0x20000000`+.** Faulted after 148 steps — `RDX` (the record pointer) read
     as `0` mid-execution, then a computed jump to `PC=0x10` decoded as invalid. The decompile shows `param_2`
     (RDX) is only ever dereferenced as three doubles (`*param_2`, `param_2[1]`, `param_2[2]`) — nothing in the
     visible code reassigns it — so either the emulated entry point drifted from the real function start, Ghidra's
     own function boundary for `18009d010` includes something not shown in the decompile view, or the emulator's
     synthetic memory (valid only from `0x20000000` up) doesn't cover a RIP-relative read the real code makes into
     the loaded image, and unmapped-memory-reads-as-zero cascaded into a bad jump. **Not resolved.** No `(a, b, d)`
     values were recovered.
   - **Decision: stop chasing this specific probe's numeric match.** Two independent tools (hand-traced decompile,
     the emulator) have both failed to produce a trustworthy `(a, b, d)`, and continuing to alternate between them
     is now costing more than the remaining gap is worth relative to what this session already banked: D175 closed
     with a direct empirical measurement, and two real, confirmed, FIXED divergences (`SolveContact`'s wrong clip
     budget source, `BuildJacobian`'s frame-mismatched cross product) — both landed in the port with tests, both
     independent of whether this one probe's control case ever numerically matches. The friction-solve probe itself
     stays in the tree as a real, working harness that already proved its worth twice; it is not yet a fixture/oracle
     test, and should not be treated as one until it matches.
   - **Next, in order**: (1) as its own dedicated pass, in a FRESH session with a clear run of tokens: retry the
     emulator, first confirming `18009d010` is genuinely `BuildJacobian`'s entry (not an offset into it) and that the
     emulator's memory model actually maps the loaded module's own image (not just synthetic scratch), before
     re-attempting; once the friction-solve probe's control case matches, resolve the `+0x60` budget field
     (likely needs a caller of `SolveOncePerPsi` itself, not yet located, or confirmation the arena zero-inits it),
     and turn the probe into a proper fixture/oracle test; (2) **DONE, 2026-09-15**: `IvpRigidBody.LedgeTreeRoot`
     closes the "moving body needs a node" gap for the ordinary case — `PhysicsLedgeTree.SingleLedge` builds a
     terminal node directly from a body's one `PhysicsLedge`, and `LedgeTreeRoot` throws `NotSupportedException`
     for a genuinely compound body (more than one ledge) rather than guess a split heuristic nothing has confirmed;
     (3) the top-level `IntegrateAwakeCores`-shaped PSI/island driver — `FUN_180090700`'s mini-island
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

## Resolved 2026-09-18 — the virtual-terrain drop rests, and two boxes driven together no longer pass through

**Both B369 symptom tests pass**, found by running the real `vphysics.dll` beside the port and diffing call by call
(`vphysics-virtual-terrain-drop` and its new `boxes` mode, with `TF2VPHYSICS_PROBE_TRACE_IMPACTS=1` and
`TF2VPHYSICS_PROBE_TRACE_QUEUE=a-b`). Three divergences, each fixed with its tests and sabotaged (commit `334662df`):

1. **The fixture, not the engine.** `IvpTestCube` was a hand-wound cube; the engine's `BBoxToCollide` box has another vertex order
   and triangulation (dumped at first attach, now the fixture word for word). Every minimize starts at triangle 0 slot 0, so the
   start vertex decides the path: the hand cube walked one corner onto a terrain triangle's diagonal, where the triangle weights
   are ±3.6e-12 on the seam and the backside walk turns back to the face it started behind — faithfully, the port's walk and
   weights match `FUN_180094e30`/`FUN_18007cdf0` byte for byte. The engine's box never goes there. **The earlier section's
   "B not a vertex" claim below is retracted**: the centre contact is a terrain vertex against a cube face, which is legitimate.
2. **The wake is deferred.** `IPhysicsObject::Wake` (`18001e3d0` → `FUN_180073a30`) only lists a sleeping object's core
   (`FUN_180087e00`, `env+0x160`); `RunPipeline` drains the list first (`FUN_180089210` → `FUN_180077c80` → `FUN_1800758e0`). The
   revive's refile holds state `0x21`, so `FUN_1800977f0` examines a newborn pair **without** removal, and the PSI's own walk files it
   far with speeds. The port woke in `Add`, paired at `Collide`, filed far at t = 0 with zero speeds, split the hull allowance
   evenly, and so passed the hull at length 0.545 instead of the engine's 0.18 (tick 9). This closes the "not what writes an entry
   INTO this list" gap noted in the 2026-09-17 entry below.
3. **The contact feature match** (`FUN_180086a50`): a point matches any edge from the same vertex of the same ledge, an edge only
   itself or its twin. The port matched edges by triangle, so the second corner of a face–face overlap reused the first corner's
   contact and was solved at its arms. The engine makes a second contact and its island solves both.

With all three, the drive-together scene's event sequence matches the binary through the second impact: hull pass at 0.1818,
the look counter `0→1→2→3→0` at the same events, collide A at length 0.0063, collide B after a feature change.

*Not established:* whether the two scenes' later trajectories match the binary tick for tick (compared only through the first
impacts), and the other nine broad-phase tests' behaviour against the binary — they pass, but none has a probe twin.

## `Advance_ABodyDroppedOnVirtualTerrain_ComesToRestOnIt` — traced to the ground, 2026-09-17

**In plain terms first, per the owner's own framing**: the cube should land on the displacement and stay there — "it should
lay over displacements" — the same way a TF2 ragdoll always does. Instead it bounces once, correctly, then falls straight
through and is never caught again for the rest of the run, even though it physically crosses back through the ground plane
on the way down. This is B369's virtual-terrain fall-through, now traced all the way to its floor.

**The setup is a genuine edge case, not an ordinary flat drop.** The cube (half 4, X/Z at 25.4/25.4 — dead centre of the
basin's flat middle) lands exactly on the diagonal seam between the flat middle's two ground triangles (hull triangles
6-16-18 and 6-18-8 in the test's own vertex numbering — see `IvpSimulationBroadPhaseTests.cs`'s `HullBlob` comment). A
temporary per-corner trace (`bodyObject.FrictionInfo.Contacts`, reverted after use) caught the actual bounce at t≈1.09–1.12:

```
t=1.09  gap=0.012967  pos=(21.40, ~0, 21.40)   corner A
t=1.09  gap=0.006770  pos=(25.40, 0, 25.40)    dead centre — sitting ON the shared triangle edge
t=1.09  gap=0.000583  pos=(29.40, ~0, 29.40)   corner B
```

All three contacts sit on the line x=z, i.e. exactly the seam — not the cube's four actual corners. Two of the cube's four
bottom corners (21.4,29.4 and 29.4,21.4) never register a contact at all. By t=1.12 all three are gone (each contact's own
`Gap` never printed a value near `IvpCollisionTolerance.RestingContactGap` (≈0.0286 m) before vanishing — the drop happens
between two 0.01 s test-loop samples, faster than this trace's resolution), the body has vy≈−3.16 (a real, correct bounce),
and from there it is in unconstrained free fall for the rest of the 3 s run: `impacts` stays at 3 forever, the body sails
back down through y=0 at t≈2.41 with no new contact, and ends at y=+7.22 (should be −4).

**Ruled out, each by direct measurement, not inference:**
- **`IvpFrictionSystem.File()`'s drop rule is correct** (confirmed earlier, `managed/.../IvpFrictionSystem.cs:995`) — not
  re-litigated here.
- **The pair watcher is not deleted and its scheduling is not broken.** `bodyObject.Node.Watchers.Count` stays 1 for the
  whole run. A temporary trace of `bodyObject.Hull.{Value,Gradient,NextPsiValue}` and
  `bodyObject.Environment.WatcherRefreshes` (reverted after use) showed refreshes climbing the whole time — 4 at t=1.10, 6 by
  t=1.90, 10 by t=2.51, 12 by t=2.91 — and `Gradient` growing with the body's own speed, exactly as
  `IvpHullManager.Advance`/`GradientFor` is supposed to (`IvpHullManager.cs:81-106`). **The conservative-advancement bound
  that is supposed to wake the pair up again works.** It wakes up repeatedly, right through the moment the body re-crosses
  y=0. Something downstream of the wake-up just never turns it into a new contact.
- **`IvpPairMindists.Refresh`/`Keep`'s ledge-matching is very unlikely to be the fault.** Read `docs/findings/51`'s *The
  pair's mindists, instruction by instruction* and *The larger mindist's tables beside the plain one's*: slot 3 (the ledge-
  pointer accessor `Keep` depends on, `FUN_180097510`) is **shared, byte-identical code between a plain `IvpMindist` and a
  recursive `IvpRecursiveMindist`** (findings 51 line ~5307). A hypothesis that a recursive mindist's own refinement moves
  its ledge pointers somewhere `Keep` can no longer recognize would have to apply to the real engine too — and this whole
  area is the most heavily cross-validated code in the project (12,000 cases, 36 sabotages, an independent re-reader, per
  findings 51's *The larger mindist is ported*). Not impossible, but the evidence points elsewhere first.

**CORRECTION, same session: the paragraph that stood here claimed `FUN_180025bc0` was outside vphysics.dll and that
`DisplacementCollisionTree.TrianglesInSphere` had "no oracle" to check it against. Both were wrong, and this whole section
duplicated ground `docs/findings/51` had already covered, more precisely, under *A wiring bug found chasing the
contact-drop fall-through, and the deeper gap it does not close* — that section was not read before this one was written.
Read that section, not this correction, for the real state.** For the record: `FUN_180025bc0` decompiles cleanly out of
`vphysics.dll` (confirmed live, this session) — it is real vphysics code that makes one virtual call out to the engine's
own `AABBTree_BuildTreeTrisInSphere_r`, and `DisplacementCollisionTree`/`TrianglesInSphere` is an already-ported, already
sabotage-tested `CDispCollTree` (findings 51, *The virtual mesh's cache entry* and *The whole map in the ported driver*) —
not an unverified invention.

**What findings 51 already established, tracing this exact same test to the exact same `y=7.220574437630701` result**:
`IvpPairMindists::Refresh` matches an existing mindist to keep purely by a hash of its two ledge pointers, with no check on
its state — so once a child mindist's flags land at exactly `0x4000` ("permanently parked," per
`IvpCollisionObject::RecheckInvalid`'s own condition, quoted verbatim there), the identical dead object is handed back for
that ledge pair on every later refresh instead of ever being discarded and rebuilt. The outer `IvpRecursiveMindist` is stuck
the same way: its `HullPassed` re-minimizes the same fixed, dead child synapses forever, so it never satisfies the one
condition that would close it back to a plain pair and rebuild. Confirmed there that `IvpRecursiveMindist`'s own port is
byte-correct against the disassembly — the open question is one level down, in `IvpMindistMinimize`'s solver dispatch and
in whether `PhysicsVirtualMesh.Build` attaches neighbor/edge-adjacency data per triangle at all. A point sitting on this
test's seam needs `BacksideWalk` to hand its closest-feature search off to the NEIGHBORING triangle once the true closest
point moves past this one's edge; if our virtual-mesh triangles carry no record of which triangle is across each edge —
unlike a real compiled `.phy`'s `PhysicsLedge` — that walk has nowhere to go, and the search just keeps re-reporting the
same dead answer for the same stale triangle.

**Update, same day: the neighbor-topology half of that question is answered, and it is not a bug.**
`PhysicsVirtualMesh.TriangleLedge` (`managed/Tf2DemoSalvage.Content/Assets/PhysicsVirtualMesh.cs:117-134`) wires each
terrain triangle's edges to hop only to its own mirrored backside — never to a different, neighboring triangle. Checked
against the disassembly quoted in this same file's own doc comment (`FUN_180003f70`: front hops `6,4,2`, back hops
`−2,−4,−6`) — an exact match. **Real vphysics also gives a virtual-mesh triangle zero neighbor-hop data.**
`BacksideWalk` giving up on a lone triangle is Valve's own behavior here, not a port gap. Cross-triangle recovery was never
supposed to happen inside one ledge's topology for a displacement — it happens by the OUTER recursive mindist re-closing
and asking `RefreshChildren` for a fresh set of nearby triangles, which is the piece that is actually stuck.

**Narrowed further: `IvpRecursiveMindist.HullPassed`'s re-close check (`Flags & 0xC000 == 0 && Length > ContactGap`,
`IvpRecursiveMindist.cs:174`) needs `Length` to grow past tolerance from the outer pair's own fixed placeholder synapse
(both sides start as vertex 0 of their ledge, per `IvpMindist.Attach` — confirmed it never touches the synapse feature,
only the object/ledge references `Keep` reads). That `Length` is computed fresh in `Solver.PointPoint`
(`IvpMindistMinimize.cs:555-628`, `FUN_1800b1b80`) — it is NOT frozen data — but only on a call that does not first return
`GaveUp` from `LoopsBack` (`IvpMindistMinimize.cs:1379-1385`). `HullPassed` always calls the NO-BUDGET minimize
(`budget=0`), and with budget 0, `LoopsBack`'s own arithmetic (`_budget--; return _budget < 0 && _loop.Seen(...)`) makes
the loop-detector live from the very first feature-pair check, instead of tolerating ~20 free revisits the way the
budgeted minimize used everywhere else does. A closest-feature search straddling a seam is exactly the case that
legitimately needs a few back-and-forth steps between two triangles' edges to converge — which the budgeted minimize can
absorb and the zero-budget one may not.**

**Checked, same day: `FUN_180094c70` and `FUN_1800b1b80`, decompiled live and compared.** `Route`'s per-kind dispatch and
`PointPoint`'s loop-check are both an exact match to this port's copies — same dispatch values, and the loop-check's real
form is `budget--; if (budget >= 0 || !Seen(...)) proceed; else GaveUp`, which is the identical formula to
`IvpMindistMinimize.cs`'s `LoopsBack` (`_budget--; return _budget < 0 && _loop.Seen(...)`), not merely equivalent-looking.
**This rules the loop-check out as a port divergence with hard evidence, not inference.** If the zero-budget minimize
gets permanently stuck reporting `GaveUp` on this exact seam geometry, it gets stuck the identical way in real
`vphysics.dll` — this specific mechanism cannot be the site of a fixable divergence, because there is nothing left for it
to diverge from.

**`Steepest` (the edge-walk `PointPoint` uses) checked and matches exactly** — same operand order, same `+1e-18` inside
the reciprocal square root (`RiseFloor`), confirmed against the inlined loop in `FUN_1800b1b80`'s own disassembly. **One
apparent mismatch turned out to be a decompiler trap, caught before it was written down as a finding**: Ghidra's
pseudocode renders the `Start<0` branch as `if (local_140 < 0.0)`, which in C semantics is false for NaN — but the real
instructions are `COMISS`/`JNC`, and `JNC` does NOT jump on an unordered (NaN) comparison, so NaN actually takes the SAME
branch as a genuine negative value. That is exactly what this port's `!(weights.Start >= 0f)` already does. Read the
disassembly, not the decompiler's synthesized comparison operator, for every NaN-adjacent branch — this project's own
convention for exactly this reason.

**Where "Backside" actually comes from, read and pinned:** `Solver.PointFace` (`FUN_1800b1910`) routes a point either
into `PointFaceProximity` (inside the triangle) or onward to the nearest edge. `PointFaceProximity`
(`IvpMindistMinimize.cs:880-945`, `FUN_1800b0c20`) is where a settled point with no downhill neighbor gets checked for
being behind the surface (`Length + ExtraRadius < 0`) and, if so, is marked `Backside` — the exact mechanism findings 51
named. `BacksideWalk`'s one retry can only flip a triangle's own front/back (confirmed faithful to Valve, see above) — it
cannot cross to a genuinely different neighboring triangle, in either engine. Recovery from that is supposed to be the
OUTER recursive mindist discarding the dead attempt and asking for fresh candidates.

**The empirical smoking gun, from tracing every child of the outer recursive mindist directly (temporary
instrumentation, reverted after use).** The ground's hull opens **8** candidate triangle children (not 2), each an
ordinary `IvpMindist` against the cube. Through t=1.60 most sit around `flags=...40xxx` (bits 14–15 clear — NOT parked)
with `Length` in the 0.17–2.5 range. **Starting at t=1.80, every single one of the 8 children, AND the outer mindist
itself, flips to `flags & 0xC000 == 0x4000` (parked) simultaneously — and none of them ever clears it again for the rest
of the run.** Crucially, `Length` keeps changing normally the whole time it is parked — outer `Length` goes
5.3579 → 3.7936 → 1.0247 → 1.0247 → 0.8688 across t=1.80…2.81, values enormously past `ContactGap` (≈0.0127) — so
`HullPassed`'s re-close condition (`Flags & 0xC000 == 0 && Length > ContactGap`) is failing purely on the FLAGS half.
**The `Length` computation is not the blocker. The solver is refusing to ever return `Settled` again, for the outer pair
and for all eight children at once, from this point on.** That is a single, precisely-characterized fact, not an
inference.

**`PointEdge` (`FUN_1800b1aa0`) checked at the instruction level, exact match — including NaN.** Both its `Start<0` and
`End<0` branches use `COMISS`/`JNC`, and `JNC` does not jump on unordered, so NaN routes identically to a real negative
in both engines, matching `!(weights.Start >= 0f)` / `!(weights.End >= 0f)` precisely.

**`PointEdgeProximity` (`FUN_1800b11c0`) checked, and a second apparent mismatch resolved by hand before being written
down as a finding.** The real code's outer branch is `if (face.Edge <= 0.0)`; this port's is `if (face.Edge > 0f)` —
opposite polarity on the same variable (`local_1d8[0]` confirmed to be `weights.Edge` via
`IvpTriangleWeights(float Edge, float Next, float Previous, float Determinant)`'s declared field order). Enumerating
all four `(face.Edge, twinFace.Edge)` sign combinations by hand for both sides shows the SAME four outcomes
(`PointFace(twin)` / `PointFace(k)` / `PointFace(twin)` / the big Backside-or-settle block) — this port just checks the
two conditions in the opposite order, which is a harmless reordering, not a divergence. The `overFace<0 && overTwin<0`
Backside trigger inside that block matches this port's `!(overFace>=0d) && !(overTwin>=0d)` structurally; its exact NaN
routing was not re-verified at the instruction level (unlike the two cases above), but every other NaN-adjacent branch
checked this session has matched this port's `!(x >= 0)` convention exactly, with zero exceptions found.

**Where this leaves B369, as of the end of this pass.** Every function actually reachable from this test's specific
scenario — the outer pair's `PointPoint` (with `Steepest` and `LoopsBack`), `Route`'s dispatch, `PointFace`,
`PointFaceProximity`, `PointEdge`, and `PointEdgeProximity`'s own dispatch — has been read against the live disassembly
and found to match, several at the raw-instruction level specifically because the decompiler's pseudocode is known to
mis-render NaN routing (`docs/findings/51` already warns of this pattern; this session hit it twice and caught both
before writing them down as findings). Combined with the earlier-confirmed facts (the drop rule, the wake-up scheduler,
the mindist ledge-matching, the virtual-mesh triangle's lack of cross-triangle neighbor data, and the loop-check budget
arithmetic), **there is now no confirmed divergence anywhere in the path from bounce to fall-through.** The body's
angular velocity through the stuck period is constant and physically unremarkable (`(-0.3317, 0, 0.3317)`,
`|ω|≈0.4691`, unchanged from t=1.10 to t=2.91) — real, conserved spin from the original asymmetric bounce, not a
runaway or a bug — which is consistent with, not against, the emerging picture: a genuinely rotating body's closest
feature is a moving target for a zero-retry-budget search re-run independently every hull pass, against triangles that
by Valve's own design cannot hand a search across to a genuine neighbor.

**`EdgeEdge` (`FUN_1800afa40`) and `EdgeEdgeProximity` (`FUN_1800b0280`) checked structurally against the disassembly —
both match.** `EdgeEdge`'s four-quadrant `(K-status, L-status)` case split lands on the identical outcome in both
engines for every quadrant, verified by hand-enumeration (this port checks the two "both non-negative" conditions
first; the real code checks "at least one negative" first — the same reordering pattern found twice already, and
equally harmless once every case is mapped). `EdgeEdgeProximity`'s two Backside triggers (`facing[0]+facing[1]==2` and
`facing[2]+facing[3]==2`) match the real `iVar11+iVar12==2` / `iVar13+iVar14==2` exactly, including which side gets
marked `Backside` vs `Triangle` and the `Lerp` weights used for each. **One detail confirmed as correct, not a
concern**: the real code marks a `Backside` synapse with the literal kind value `5` — this port's own
`IvpFeatureKind.Backside = 5` (`IvpMindistMinimize.cs:24`) already declares that exact value, with `4` deliberately
unused. **The two blocks flagged as too risky to rush are now checked too, carefully, and both match.**
`EdgeEdge`'s `kNear`/`lNear` tail: the real C has a genuine trap — `(0.0 <= A) && (puVar3 = B, C < 0.0)` mutates `puVar3`
as a side effect of evaluating the right operand, EVEN WHEN the overall `&&` is false (i.e. even when `C < 0.0` turns
out false, the comma's left half `puVar3 = B` already ran). Working through all three reachable cases by hand
(`cond1` false; `cond1` true and `cond2` true; `cond1` true and `cond2` false) shows this port's three-way
`if (!cond1) ... if (!cond2) ... else ...` produces the identical `PointPoint` call, arguments and side order, in
every case — including the case where the trap matters. `EdgeEdgeProximity`'s `facing`/`cosine` loop: every operation
maps in the same order (the `pointIndex`/`from`/`to`/`edge`/`along` computation, both early-exits, `normalScale`,
`edgeScale`, the exact `candidate = edgeScale * along * normalScale` multiplication order, the `TriangleWeights` call
and its `inside.Edge > 0f` gate), and the loop's starting constant matches exactly:
`CosineStart = -4e-12d` (`IvpMindistMinimize.cs:462`) against the disassembly's `dVar27 = -4e-12`.

**Every line of `IvpMindistMinimize` that this session set out to check has now been checked. Zero divergences found,
anywhere, including the two pieces held back earlier out of caution.**

**`Solver.FaceFace` (`FUN_180094f80`) checked — exact match, and this closes the entire solver.** Every phase
corresponds directly: the 3×3 point-vertex search over both triangles; the point-vs-plane check run in both directions
(`TriangleWeights(...).Inside` gate, the exact same `1.000000000001` tie-breaking epsilon this port's `Handicap`
constant uses); the edge-vs-point check in both directions; the full 3×3 edge-vs-edge search; the final
Point-kind-takes-priority swap before dispatch; and the terminal dispatch table itself, whose unreachable default case
is a direct port of the real engine's own crash guard — `Error(..., "ivp_mindist_minimize.cxx", 0x1d9)` in the
disassembly, matching this port's own citation of `ivp_mindist_minimize.cxx:473` for the same `InvalidOperationException`
almost exactly (0x1d9 = 473 decimal — the same line, not a coincidence). The engine's own embedded build path,
`C:\buildworker\rel_hl2_win64\build\src\ivp\ivp_collision\ivp_mindist_minimize.cxx`, is the exact file this whole port
has been citing by name all session.

**Every function in `IvpMindistMinimize` has now been read against the live disassembly and matches Valve exactly.**
Combined with everything from earlier in this document (the drop rule, the wake-up scheduler, the mindist
ledge-matching, the virtual-mesh triangle format), **there is no confirmed divergence anywhere in the entire path from
the bounce to the fall-through, in any function that governs it.** What remains unverified is two small, dense
bit-manipulation blocks inside `EdgeEdge` and `EdgeEdgeProximity` (named above) that were deliberately not hand-traced
to avoid a transcription error after this much verification — and whether real TF2 reaches the identical stuck state
on a dead-centred seam landing, which only a live trace can answer.

**Live verification result: the real engine reaches the identical stuck state, live, during ordinary gameplay.**

Ran a headless `tf_win64.exe` dedicated/listen server (`cp_dustbowl`, 20 bots via `tf_bot_quota`, cvars and bot
commands driven through an `+exec`'d cfg rather than the owner's own `autoexec.cfg`, which was left untouched — real
combat confirmed in `console.log`, e.g. *"The G-Man killed ZAWMBEEZ with sniperrifle. (crit)"*). Attached the Cheat
Engine MCP bridge, matched vphysics.dll's live runtime base to the Ghidra image exactly (re-derived fresh each relaunch
as ASLR moved it), and armed non-blocking hardware breakpoints on `IvpRecursiveMindist::HullPassed` and
`IvpRecursiveMindist::Freeze` (the hull-open entry point).

**In one 60-second window of ordinary bot combat on dustbowl's canyon terrain: 3,117 `HullPassed` hits and 265
`Freeze`/open hits** — this mechanism fires constantly in normal play, not just in the synthetic reproduction. Several
specific mindist objects (same pointer, captured live in `RCX`) were hit repeatedly within the same second — the same
"keeps getting re-checked" signature the port showed while stuck. Reading two of them directly, live, seconds after
their last hit:

| mindist | `[+0x20]` flags | `flags & 0xC000` | `[+0xa8]` length (float) |
|---|---|---|---|
| `0x1DC69A52300` | `0x0FD40000` | `0x4000` (parked) | `0x4014B690` ≈ 2.32 |
| `0x1DC69A65A00` | `0x0FD40000` | `0x4000` (parked) | `0x3F4315F9` ≈ 0.76 |

**Both numbers are the exact signature this document already found in the port**: `Length` is far past `ContactGap`
(≈0.0127) — the re-close condition's length half is trivially satisfied — while the flags half stays parked, live, on
the shipped binary, during nothing more exotic than bots fighting on a stock map's terrain. **This is not a synthetic
artifact. Real vphysics.dll holds mindists in exactly this state routinely.**

What this settles and what it does not: it confirms the mechanism itself — a mindist sitting well past tolerance while
flagged parked — is genuine, common Valve behavior, not a port invention. It does **not** by itself prove real TF2
never falls through terrain, because real gameplay has many overlapping contacts (other corners, other props, other
players' own contacts) that can keep a body supported even while one specific mindist sits stuck; the synthetic test
isolates a single simple body with few candidate triangles, which is far more exposed to a stuck mindist actually
mattering.

**CORRECTION, same night: the seam was never the cause, and this is now a stronger, unified finding, not a weaker
one.** Moved the test's drop position off the exact seam centre (20, 30) — a completely ordinary single-triangle
landing, nowhere near the diagonal. **It still falls through** (`y=10.42`, `impacts=5`, `last hull pass Refiled` — a
different status than the seam case ever showed, and briefly *did* register 2 contacts and hover near `y≈-4.1` for a
few ticks before losing them all and free-falling forever). The seam-specific framing above was wrong; tracing this
new failure found the real, shared mechanism.

**The plain (non-recursive) path, traced and read against disassembly for the first time tonight.** `IvpMindistHull.
HullPassed` (`FUN_180097f00`) — the far-pair path a non-hull-with-children contact actually takes — does a *cheap
linear estimate* of remaining distance instead of a real re-measurement each hull-pass, only escalating to a real
check (`HandedOff`) once the estimate looks close. Checked its full formula and its `6.0`-step threshold
(`RefileSteps = 6d`) against the disassembly: **exact match, term for term, including the grouping.** The scheduling
is not the bug — proven twice now, in two different mindist kinds.

**Where `HandedOff` actually leads, and where the two paths converge.** `HandedOff` → `IvpMindistHull.BecomeExact`
(`FUN_1800977f0`, checked, matches) → a real `Minimize` call → if it does not settle (`FrozenBits` set), a **plain**
mindist's `Freeze` (`IvpMindist.Freeze`, `FUN_180097440`, checked, matches) does not open a hull like the recursive
one does — it calls `IvpMindistManager.Invalidate`, putting the pair on the object's own **invalid list**.
`IvpCollisionObject.RecheckInvalid` (`FUN_180074240`, already read and fixed earlier tonight for its budget-wiring
bug) then re-runs the same **zero-budget** `Minimize` on every invalid pair, **every single PSI**, unconditionally,
for as long as `flags & 0xC000 == 0x4000` keeps coming back — checked and matches the disassembly exactly.

**The two failure paths are not two bugs. They are the same root cause wearing two faces.** A hull-with-children
contact that cannot settle goes recursive/parked, re-minimized on the hull manager's own conservative schedule. A
plain contact that cannot settle goes invalid, re-minimized unconditionally every PSI — *more* often, not less — and
still does not resolve, because retrying changes nothing: it is the identical zero-budget solver, already verified
byte-for-byte faithful to Valve down to the exact loop-check formula (`docs/HANDOFF.md`, earlier this session), run
again on the same geometry. If it cannot converge once, retrying it every PSI forever does not help — and the
live-verified fact that real vphysics.dll holds recursive mindists in exactly this parked state during ordinary
gameplay (above) applies identically here, since it is the same solver code either way.

**Where this actually leaves it.** Every scheduling path (conservative hull-manager estimate, unconditional per-PSI
invalid recheck) and every solver function reachable from either path has been read against the disassembly or
verified live tonight, and all of it matches Valve. **There is no confirmed port divergence anywhere in this chain.**
What remains is the same question named above, now sharper: IVP's zero-budget minimize can persistently fail to
converge on a body whose geometry keeps changing PSI to PSI — confirmed live, on the shipped binary, for the
recursive path — and a body with too few other contacts to fall back on when that happens will fall through,
regardless of which of the two mindist kinds its contact took. That is a real, load-bearing limitation of the
engine's own solver, observed for real, not an artifact of this port or of one adversarial test position.

**RETRACTION: the "not a port bug" conclusion above is wrong, per direct oracle evidence obtained right after
writing it.** `tools/Tf2DemoSalvage.Probe/Probes/VphysicsVirtualTerrainDropProbe.cs` drives the real, shipped
vphysics.dll in-process against the **identical** `DisplacementCollisionTree`/hull-blob geometry the failing
C# unit test uses — same seam-centred drop, same gravity. Fixed the probe's timestep from the engine's default
100Hz to the test's own 66Hz (`InverseStep = 66d`) for a genuine apples-to-apples run (committed separately,
`14adbc51`). **The real engine settles cleanly at this matching timestep**: final position 157.86in vs the
expected 157.480in. The port does not. That is a controlled, tick-rate-matched, geometry-matched refutation of
every "shared Valve behavior" conclusion above — there is a real port divergence, and the live bot-combat
signature (parked flags, length past tolerance) documented above is genuine Valve behavior **in general**, but
is not what is happening to this specific test's mindist, which the probe proves recovers on the real binary.

**Two more candidate explanations chased down and ruled out with hard evidence, same night:**

- **PSI/`Advance`-cadence aliasing.** Changed the test's stepping loop from `at += 0.01d` (misaligned against
  the internal ≈0.01515s PSI) to `for (double at = 1d/66d; at <= 3d; at += 1d/66d)` — exact lock-step with the
  engine's own PSI rate. **Bit-identical failure** (`y=7.220574437630701`, `impacts=3`, last hull pass
  `Recursive`). The outer test loop's stepping is not the cause.
- **The recursive-mindist open/close/reopen lifecycle itself** (`IvpRecursiveMindist.HullPassed`,
  `IvpHullManager.Advance`/`NotifyPassed`, `IvpRangeManager.PairRange`) — live-traced tick by tick with
  temporary `Console.WriteLine`s (reverted before commit, never landed). Over the whole ~198-PSI run: the
  hull-manager `due` check fires repeatedly and correctly (at PSI #19, 37, 39, 54, 58, 71, 80, 82, 84, 86, 107,
  108, 115, 120–124, 140, 143, 161, 164, 179, 183, 186 — not just at the start), and `RefreshChildren` reopens
  the pair **five separate times** as the body falls, bounces and re-approaches, finding the same 8 candidate
  terrain triangles every time (`openSide=0` constant throughout). This machinery is not stuck, not starved,
  and not silently abandoned — it tracks the body correctly right up to the run's natural end. **This whole
  subsystem, exhaustively checked against the disassembly earlier and now checked live, is cleared.**

**The sharper finding this leaves: only 3 impacts ever commit, across 5 correct reopens, while the body makes
several close approaches.** `simulation.Environment.Impacts == 3` in every run of this test, seam or off-seam,
0.01-stepped or exact-1/66-stepped. The mindist-tracking layer (verified byte-for-byte against Valve, above)
finds the right triangles every time; the reopen/reschedule layer (just verified live) refreshes on time every
time. Neither is where an approach gets dropped. **What has not been examined this session at all is the path
from "a child mindist is exact and near" to "an impact actually commits and changes velocity"** —
`IvpPairScheduler.Examine`, the event queue a scheduled pair is drained from, and whatever runs the actual
impulse. That is the next concrete place to look, not another pass over the solver or the reopen lifecycle.

**CORRECTION, same session: it is not the scheduler.** Live-traced `IvpPairScheduler.Examine` itself, correlated
tick-by-tick against `RefreshChildren` (temporary instrumentation, reverted, never committed). The 8 children
are examined cleanly through the first bounce (t=0.82–1.09, `closingEnough` true, length shrinking 0.30→0.006),
then all eight go silent from **t=1.088 to t=2.86 — 1.77 of the run's 3 seconds** — despite four separate
`RefreshChildren` calls in between (t=1.77, 2.06, 2.38, 2.71) finding the same 8 candidates every time. That
silence is not `Examine` returning `LeftAlone` (which keeps a pair on the Exact list and still calls `Examine`
every PSI) — a pair only stops being examined at all by leaving the Exact list, which happens exactly one way:
`IvpMindist.Freeze` (`IvpMindistMinimize.cs:220`, unconditional) calling `Invalidate`. **This is the same
mechanism already documented above** (`Freeze` → `Invalidate` → `IvpCollisionObject.RecheckInvalid`'s
zero-budget retry, parked at `flags&0xC000==0x4000`) — the new fact is its precise duration: for these 8
mindists, on this drop, it consumes 1.77 straight seconds while the body is demonstrably still moving the
entire time (its own independently-traced trajectory climbs from y≈-4.2 to y≈+7.2 across this exact window).
A zero-budget retry that is genuinely re-evaluated fresh every PSI against a body in a materially different
position each time, and reports the identical `0x4000` verdict for 100+ consecutive ticks regardless, is
either a real single-step-budget limitation that is mathematically bound to persist exactly this way for this
geometry, or a sign that the retry is not actually seeing the updated state — and every formula that could
decide between those two (the retry itself, `IvpMindistManager.Recheck`, `RecheckInvalid`) has already been
read against the disassembly and matches. **Telling those two apart needs a live comparison against the
shipped engine's own retry** (a Cheat Engine breakpoint on the equivalent zero-budget minimize, for the same
geometry, the same way the earlier live-verification pass in this document worked) — not another pass over
C# source, which has now been read about as far as reading alone can settle it.

**LIVE RESULT: the real engine's contact never goes invalid at all — the whole retry mechanism is never
entered.** Ran `vphysics-virtual-terrain-drop` under a live debugger (Cheat Engine MCP, hardware breakpoints,
non-blocking) against the shipped `vphysics.dll`, breakpointed at two addresses Ghidra confirms by name —
`IvpMindistMinimize::MinimizeWithoutBudget` (`0x180095ad0`) and `IvpRecursiveMindist::HullPassed`
(`0x1800b28a0`) — for the probe's full, successful (settles correctly) run of this exact geometry. **Both
addresses verified live**: `disassemble` at the computed runtime address shows a genuine function prologue
(`push rdi; sub rsp,0x870; ...`), and `enum_modules` independently confirmed the runtime base matched the
probe's own self-reported load address, so this is not an address-translation miss. **Zero hits on either,
across three independent runs.** `MinimizeWithoutBudget` is called from exactly one place,
`IvpCollisionObject::RecheckInvalid`, once per entry of `InvalidSynapses` — zero calls means the invalid list
is empty the entire run, i.e. **this contact's mindist never leaves the Exact list at all.** `HullPassed`
never firing means the recursive parent never gets far enough from its children to need a hull-manager
recheck either — it just never has to reopen.

**This flips where the divergence lives.** Every piece of the "genuinely parked" mechanism — `Freeze`→
`Invalidate`, `RecheckInvalid`'s zero-budget retry, `BacksideWalk`'s inability to cross an isolated
single-triangle ledge — was independently confirmed byte-for-byte faithful to the disassembly earlier this
session, and is mathematically guaranteed to stay parked forever once entered, for exactly the reason already
found (no neighbor topology to walk to, by design, matching the shipped engine's own per-triangle ledges).
**None of that is wrong, and none of it is where the bug is** — because the real engine, for this identical
drop, never enters it in the first place. The port's contact freezes (leaves bits of `0xC000` after a
minimize) shortly after the first bounce; the real engine's identical contact does not. Both go through the
same regular, BUDGETED per-PSI minimize on the Exact list first (`IvpMindistMinimize::Minimize`,
`FUN_180095cb0`, not the zero-budget one) — so the question is no longer "why can't the retry escape a dead
end", it is **"why does the port's ordinary budgeted minimize freeze here when the real engine's does not."**
The concrete next thing to check is the STEP BUDGET actually wired into that call for the normal Exact-list
path (`IvpSimulation.Minimize`/`MinimizeExact`'s delegate) — this session already found and fixed one budget
mix-up on the *invalid* side (`RecheckInvalid` was wired to the wrong, budgeted delegate; fixed, and the
fall-through numbers didn't change) — an equivalent mix-up on the *regular Exact-list* side, silently starving
the FIRST post-bounce minimize of the budget it should have, would produce exactly this signature: a normal
contact that resolves once, then freezes on its very next look instead of getting the extra steps a working
budgeted search needs to keep tracking a body that just changed direction.

**Checked and ruled out: the budget wiring is not the bug.** `IvpSimulation.Minimize` (`IvpSimulation.cs:740`,
the delegate `MinimizeExact`/`Recheck` actually call) uses the 4-argument `IvpMindistMinimize.Minimize`
overload, which defaults to `StepBudget = 20`; `MinimizeWithoutBudget` (`:875`) explicitly passes `budget: 0`.
No mix-up — this session already found and fixed the one budget-wiring bug that existed (`RecheckInvalid`'s
own closure, corrected earlier tonight), and there is no second one on the regular Exact-list side.

**So the divergence is not "the search can't escape" on either side — that limitation is real, shared, and
by design** (an isolated per-triangle ledge has no neighbor to walk to in EITHER engine, budgeted or not).
The real question is why the port's search ever NEEDS to escape this specific triangle at all, when the real
engine's identical contact never does. The one number that differs so far: at the first bounce (t≈1.09,
tick≈78 at this test's stepping), the port's post-impact vertical speed is **≈60% larger in magnitude than the
real engine's** at the same instant (port −2.70 vs real −1.67, both converted to the same Y-metres/second
scale) — a smaller, gentler rebound would plausibly keep the contact tracking the SAME triangle face the whole
time (matching the real engine's immediate, quick settle by t≈1.2), while a bigger one could push the search
far enough to need the escape this topology can never provide. **Not yet measured**: whether this speed gap
is itself the cause (a restitution/elasticity difference at the moment of impact) or a downstream symptom of
something upstream of it. That is the next concrete, still-untested thread, not this session's stopping point.

**FOUND. The restitution-gap theory above was itself an artifact — the test's material was wrong, not just
its own bounce.** `Environment()`'s shared `Materials` hardcoded `IvpReplayMaterials(..., friction: 0d,
elasticity: 0d)` for the whole file. Valve's real "default" surface (`scripts/surfaceproperties.txt`,
confirmed live against the shipped `vphysics.dll` via `vphysics-materials parse`) is **friction 0.8,
elasticity 0.25** — a frictionless, perfectly inelastic pair was never the scenario the probe was comparing
against. Fixed, isolated to just this test (`14adbc51`'s sibling commit `c0bc276b`; the other 12 tests in this
file keep 0/0, since forcing the real values onto all of them surfaces a separate, real friction/damping
instability — one static-slab test runs away to a 67 units/second creep instead of settling — that needs its
own investigation and is out of scope here).

**With the correct material, the first bounce now matches the real engine almost exactly**: port
`vy=-1.6481` at t=1.18 vs the real engine's `vy≈-1.667` at the same instant — under 2% apart, not 60%. The
"hotter first bounce" finding above is retracted; it was purely the material mismatch. **The fall-through
still happens** (now via a `Refiled`/2-impact path instead of `Recursive`/3-impact, but still permanent) —
so materials were never the actual bug, only a real, separate bug worth having fixed anyway.

**The actual cause, found by printing angular velocity alongside linear on both sides (`ec8e0d15`):**

```
REAL:  spin=(0.00, 0.00, -0.00)   -- at EVERY printed tick, start to finish
PORT:  spin=(0,0,0) before the bounce -> (-0.8663, 0.0000, 0.8663) at tick 78, unchanged forever after
```

**The real engine's cube never rotates, at all, for the entire run — a flat, centred, corner-symmetric drop
has no net torque, and the real physics gets exactly zero.** The port's cube picks up a large, permanent spin
at the very first bounce and keeps it exactly, to four decimal places, through complete free-flight afterward
(no further contact ever touches it — the same "zero further impacts" fact already established, now with a
concrete, physical cause instead of just a symptom). **This is the root cause, not another symptom**: a
spinning cube's second approach presents a completely different, off-axis contact configuration than a
level one, which is far more likely to land on the single-triangle, no-neighbor topology this document
already proved cannot be escaped once entered — while a real, non-spinning cube keeps re-presenting the same
flat, symmetric face and never needs to.

**Narrowed further: both impacts land in the same PSI, and spin is already nonzero at the very first one.**
Traced `simulation.Environment.Impacts` (temporary, reverted): with the correct material it goes 0→2 in a
single tick (108, t=1.09) — not staggered across ticks — and `body.AngularVelocity` is already
`(-0.8663, 0, 0.8663)` at that first print. Nothing after tick 108 ever changes the count again (matching the
"never touched again" fact already established). So the torque is injected within that one PSI's contact
resolution itself, not by a later event arriving too late or too early.

**One specific hypothesis checked and ruled out**: that two triangles' contacts land in two separate,
uncoupled `IvpFrictionSystem`s instead of one merged system. Read `IvpFrictionLinking.FindOrAllocate`/
`LinkContactByCore` in full (`IvpFrictionLinking.cs`) — `LinkContactByCore` explicitly checks
`movable.FrictionInfo` first and reuses that existing system (`system = info.System`) rather than building a
new one when the moving core already belongs to one; a second corner's contact under the same PSI would join
the first's system, not start its own. Structurally correct as read; this is not where the asymmetry comes
from.

**Read `IvpImpactIsland.Build`/`Drain`/`Grow` in full — the sequential design itself is not the bug.**
`Drain` (`FUN_180090bd0`) explicitly "solves the contact predicted to close first" one at a time, in a
`while (Drain(...))` loop that keeps re-picking whichever remaining pair's contact is soonest until none are
left — a Gauss-Seidel-style iterative solve, sequential by construction, and that IS what the disassembly
says Valve does. Both engines being sequential this way rules out "sequential vs. simultaneous" as the
divergence — a faithfully-sequential solve that iterates to convergence is not inherently asymmetric.

**Live-traced the exact queue/collide/grow sequence in the port** (temporary instrumentation in
`IvpPairScheduler.Examine`, `IvpMindistCollide.Collide`, `IvpImpactIsland.Grow`; reverted, never committed).
Seven candidate mindists queue for the **exact same time**, `1.08730`. Only two ever fire `Collide()` — mindist
`34138141` ("A"), then `63504289` ("B") nine microseconds later — each building its own single-pair island
(`bodyFrictionSystem=none` at *both* entries, confirmed via `firstCore.FrictionInfo`), never coupled. The other
five get examined again, found no longer close enough once the body starts moving, and never fire at all.

**Checked `IvpFrictionLinking` (ruled out) and `IvpImpactSolver` (ruled out) as the site.** `LinkContactByCore`
correctly looks for an existing `FrictionInfo` before building a new system — structurally sound; the reason B
gets its own fresh system is that A's system was already gone (torn down once its single contact separated),
not a linking bug. `IvpImpactSolver`'s per-contact math (`UnitPush`, virtual mass, the push loop) is pinned
lane-for-lane against the shipped binary by `IvpImpactSolverConformanceTests` — not a plausible location for a
silent bug, and a single isolated off-center contact producing *some* torque is correct physics, not a defect.
The defect is that only two isolated contacts ever run, when a symmetric drop needs enough of them, seen
together, to cancel back to zero.

**Live-checked the real engine's own `IvpMindist::Collide` (`0x18008ecb0`, confirmed by Ghidra by name) for
the identical drop — and it is not two calls, it is three: A, then B, then A AGAIN.** Cheat Engine breakpoint,
register capture, three hits, two distinct `RCX` (mindist pointer) values in the pattern A/B/A. **This is the
concrete divergence** — the real engine's A gets a third pass the port's A never does.

**CORRECTION, traced precisely: it is not a reschedule landing past the PSI end.** Instrumented every return
path of `IvpPairScheduler.Examine` (temporary, reverted, never committed) rather than guess from the formulas.
A is re-examined at `now=1.08739` — right after B's `Collide()` — and returns `LeftAlone` with
**`closing=-2.558135`**: strongly *negative*, meaning A's own contact point now reads as separating, not
approaching. That is `!closingEnough` (line ~199), which returns before the recheck/`Dropped` path is ever
reached — the mode-2 `AfterMiss` reschedule this entry previously named is not what happens here. **This is a
direct, physical reading of the state B's impulse left behind**: once B's impulse imparts spin to the whole
body, A's own point — a different location on a now-rotating object — genuinely sweeps away from the ground,
and the scheduler is correctly leaving alone a contact that is, at that instant, truly separating. Nothing
here is a scheduling bug; it is a faithful measurement of an already-wrong velocity/spin state.

**So the divergence is upstream of the scheduler, in B's own impulse — and traced to its exact input.**
`IvpImpactSolver`'s math was already ruled out as a formula bug (lane-for-lane conformance-tested against the
shipped binary), so printed the two contacts' actual resolved arms and normals (temporary, reverted, never
committed):

```
A: secondArm=(-4.000000, 4.000000, -4.000000)  normal=(0.000000, -1.000000, 0.000000)  -> spin (-1.3212, 0, 1.3212)
B: secondArm=( 0.000295, 4.005848, 0.000295)   normal=(0.000129, -1.000000, 0.000129)  -> spin (-0.8663, 0, 0.8663)
```

**A's contact point is an exact cube corner — `(±4, ±4, ±4)` is the whole of this fixture's geometry**
(`IvpTestCube.Box`, checked: eight points, exactly those eight coordinates, no others). `(-4, 4, -4)` is one
of them. **B's is not.** `(0.0003, 4.006, 0.0003)` is not a vertex of this cube under any tolerance worth the
name — it sits within a hair of `(0, 4, 0)`, the centre of the +Y face. A symmetric landing on a seam should
put B on a *different corner*, not the middle of a face. **This is the concrete, mechanistic bug**: whatever
feature B's closest-feature search actually resolves to, it is being reported as a near-face-centre point on
the body rather than the second corner the geometry calls for. A contact there is nearly collinear with the
body's centre of mass — almost no lever arm — so B's resolve should barely touch A's already-large spin at
all; instead the observed spin *drops* from A's own 1.3212 to 0.8663, meaning B's iterative push loop is
still interacting with the existing spin through `PointVelocity` at that near-centre arm, adjusting it
without ever being positioned to correct it toward zero the way a true second-corner contact could.

**The concrete next step, precisely bounded now**: find why B's synapse resolves to a near-face-centre point
instead of a genuine second vertex — read `IvpMindistMinimize`'s feature transition for this specific mindist
(already exhaustively verified in isolation earlier this session, but not for this exact multi-step sequence:
created, refreshed across five `RefreshChildren` cycles, examined repeatedly, finally resolving 9 microseconds
after a sibling mindist already moved the body) — or capture the same arm/normal pair from the real engine's
own `IvpImpactSolver::Enter` (`FUN_18008ed60`) for its own "B" to confirm it lands on a real corner where the
port's does not. Either would settle it. The comparison already in hand (port spin should land at exactly
zero, matching the real engine) is what a fix needs to reproduce to be verified correct.

**Where this leaves the decision the owner already anticipated** ("we are probably doing 1 though... this
isn't even a better-than-valve thing, this is a they-probably-made-this-happen-with-collision-optimization,
and it never happens in game"): disproven — this is a real, now precisely-located port bug (spurious torque
from an asymmetric multi-corner contact), not a shared engine limitation and not something that "never
happens in game" — any TF2 drop that lands straddling a displacement's internal triangle seam is exposed to
it. Evidence class: measured live (Cheat Engine, addresses confirmed by Ghidra by name) for the invalid-list
finding above; measured directly (matched-material tick traces on both sides) for the spin finding. Neither
interpolated.

## How the ports are built, so the next one matches

A port in `managed/Tf2DemoSalvage.Animation/Animating/`; its lanes in `tools/Tf2DemoSalvage.Probe/Oracle/Ivp*Replay.cs`
(linked into `Tf2DemoSalvage.Animation.Tests.csproj` with its `Data/*.txt`); a probe in `tools/.../Probes/Vphysics*Probe.cs`
with `sweep n` and `fixture path` modes; a `*ConformanceTests` class with a fixture control. **Isolate a routine by detouring its
callees** (`VphysicsDetour`, twelve bytes, restored on dispose) rather than fabricating their state. Constants by their bits or as
widened floats — a decimal literal cost an ulp twice. Every product and sum through `IvpMath` with the disassembly's destination.
Use the MCP servers: `mcp__ghidra__*` (`disassemble_function`, `disassemble_bytes` for code Ghidra has no function for,
`read_memory`, `get_xrefs_to`) and `mcp__agent-lsp__*` for the C# side.
