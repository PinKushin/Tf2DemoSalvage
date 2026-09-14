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
   (`IvpLedgeTreeReplay.InnerWithLedge`) so `FUN_180096680` makes one; the exact tail detoured to a recorder that only links
   (flags and the lists `FUN_180098dd0` unlinks); `FUN_180095ad0` and `FUN_18008ecb0` detoured to recorders driven by lanes;
   then slot 7, slot 8 and `FUN_180097f00` called on it.
2. **Units.** vphysics runs IVP in metres and converts once at `CPhysicsEnvironment`'s boundary. The new ports are metres (their
   constants are the binary's), but `IvpMindistHull`, `IvpPairScheduler` and `IvpEnvironment` still carry metre constants
   converted to inches (`* IvpTransform.InchesPerMetre`). Convert them to metres before anything is wired together — mixing the
   two is a defect, not a style choice.
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
