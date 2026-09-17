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
   avoids this exact trap without any of the functions read so far needing to differ from ours. This needs a fresh read,
   not a retrace of anything already covered. *Not a regression the drop introduced*: the earlier resting contact
   this project's own drop had been (wrongly) keeping was propping the body up over
   this gap the whole time.
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

## How the ports are built, so the next one matches

A port in `managed/Tf2DemoSalvage.Animation/Animating/`; its lanes in `tools/Tf2DemoSalvage.Probe/Oracle/Ivp*Replay.cs`
(linked into `Tf2DemoSalvage.Animation.Tests.csproj` with its `Data/*.txt`); a probe in `tools/.../Probes/Vphysics*Probe.cs`
with `sweep n` and `fixture path` modes; a `*ConformanceTests` class with a fixture control. **Isolate a routine by detouring its
callees** (`VphysicsDetour`, twelve bytes, restored on dispose) rather than fabricating their state. Constants by their bits or as
widened floats — a decimal literal cost an ulp twice. Every product and sum through `IvpMath` with the disassembly's destination.
Use the MCP servers: `mcp__ghidra__*` (`disassemble_function`, `disassemble_bytes` for code Ghidra has no function for,
`read_memory`, `get_xrefs_to`) and `mcp__agent-lsp__*` for the C# side.
