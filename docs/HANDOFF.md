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
     `SplitGap`'s split, `1e-10f` on a clear side, both records through `InstallAtNextPsi` — never `FileFar`; (4)
     `IvpRecursiveMindist : IvpMindist` with a nested child delegator mirroring the `+0xe0` object (child removal
     `IvpCollisionList.Remove`, slot 2 adds to its own `+0xfc` total and tails outward, slot 3 asks outward first) — **the limit
     reads the outermost total, never a child list's count**; `Delete` = children deleted last first then `-count` told, then
     the base; slots 7/8 and `FUN_1800b28a0` as read, `FUN_1800b29b0` as a call to the unchanged `IvpPairMindists.Refresh` with
     the open side as ROOT and the other as LEDGE; `IvpPairMindists.Construct` builds it for a ledge with children, and
     `IvpMindistHull.HullPassed` sends the recursive state to it (a flags test, as `FUN_180097f00` does, not a vtable call);
     (5) the oracle above, then a sabotage round.
   - **Not added:** a slot-6 `IsRecursive` member — `FUN_180028aa0` answers 1, but no ported caller reads slot 6.
   - **Slot 7's side choice reads each ledge's OWN node** (`ledge + ledge+0x4`, `tree.Node(LedgeNodeOffset)`), a null node's
     radius `1e15f`; not the found node, and not `Left`.
2. **Units — done (D173).** vphysics runs IVP in metres and converts once at `CPhysicsEnvironment`'s boundary, so every IVP
   port now does too: `IvpCollisionTolerance`, `IvpMindistHull`, `IvpPairScheduler`, the time-of-impact searches and
   `IvpRootFinder` were converted from inches with their tests. The running path (`IvpEnvironment`, `IvpContact`,
   `RagdollSimulation`) stays in Source units until it is replaced; the conversion belongs at the `CPhysicsEnvironment` and
   `CPhysicsObject` seam, nowhere inside the core.
3. **Displacements**: the mesh manager's `FUN_180025bc0` calls the engine's virtual-mesh query, which is not in vphysics;
   `FUN_18007bea0` is unread.
4. **The running path**: the unit/scheduler layer and the filing layer, then replace `IvpContact`/`IvpEnvironment`, then step 7.
   `vphysics.dll` exports `CreateInterface` with `VPhysics031` and `VPhysicsCollision007` (`src/public/vphysics_interface.h`), so a
   real corpse drop can likely be simulated in process and compared end to end. Not tried.

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
