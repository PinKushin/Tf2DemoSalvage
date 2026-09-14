# Handoff — IVP's collision path, ported function by function against vphysics.dll (B369, D172)

Written 2026-09-14, superseding the handoff of 2026-09-07 (the ragdoll constraint solve; read it in git history before
`290052f7` if needed — its items 1, 3 and 5 are what D172 now covers).

**Branch `fix/b369-ivp-narrow-phase`, pushed, tree clean at the commit that adds this file.** Nothing is mid-edit. The
last full Animation run: 4057 total, 4056 passed, 1 skipped (the medic medigun bone test, skipped before this work too).

**Read `docs/findings/51-vphysics-is-ivp-and-it-is-readable.md` first** — every port cites it by address, and the sections
from *The broad phase* on are this session's reading.

## The direction, unchanged

D172: port ALL of IVP's collision path to parity and put it on the running path, replacing the invented
`IvpContact`/`IvpEnvironment` structure; then step 7 — delete TerrainDepth/TerrainReach and the compensators, run the
corpse-drop measurement, gate (`TF2DEMOSALVAGE_GCOR_ONLY=1 bash build/gate.sh`), merge. Valve's way, always (D89/D129/D131).

## Done this session — each pinned to the shipped binary called in process

| port | engine | probe | cases agreeing | fixture |
|---|---|---|---|---|
| `IvpOvTree` | `FUN_18009ecb0` insert, `FUN_18009efc0` removal and helpers | `vphysics-ov-tree` | 50,000 | 300 + 11 searched |
| `IvpRangeManager` | `FUN_1800a0420` policy 1, slots 1–2 | `vphysics-range` | 100,000 | 400 + 1 searched |
| `IvpLedgeTree` + `PhysicsHull.Tree` | polygon manager slot 4, `FUN_18007ada0`/`FUN_18007afb0` | `vphysics-ledge-tree` | 20,000 | 300 + 7 searched |
| `IvpObjectCache` | `FUN_180080a60` | `vphysics-object-cache` | 100,000, NaNs included | 400 |
| `IvpMatrix.FromRotation` | `FUN_180071330`'s NaN destinations, via `Addsd`/`Mulsd` | (through the object cache) | — | — |

Sabotage rounds (sonnet `sabotage-verifier`) killed every non-equivalent mutant of the first three, each survivor by a
searched case; the equivalences are argued in findings 51.

## In flight when the session stopped

**The object cache and matrix fill's sabotage round was stopped mid-run and its mutant restored** (verified: no diff under
`managed/`). Re-run it before building on the cache. The mutants: in `IvpObjectCache` — a NaN elapsed time interpolating
instead of copying; each position lane's sum and product operand order; the `Mulss` fraction's order; the offset
translation's operand order and grouping; one `Compose` product's order; `Compose` replaced by `IvpQuaternion.Product`; the
matrix refill after the object rotation deleted; `RefreshedAt = psi + 1`. In `IvpMatrix.FromRotation` — `M2`'s addends
swapped, `xTwoX` and `wTwoX`'s factors swapped, `M0`'s addends swapped, `M6`'s factors swapped. Run the WHOLE Animation
suite per mutant, since the matrix fill is shared.

## Next, in order

1. **The broad phase and its pair watcher.** `FUN_180098880` is read in full (findings 51, *The broad phase* — with the
   range sum's destination corrected at `290052f7`), with `FUN_1800962c0`'s partner table, the creator `FUN_1800a0650`
   (slots `FUN_1800a0690`, `FUN_1800a06f0`, `FUN_1800a07a0`), the watcher `FUN_1800b5dd0`/`FUN_1800b6080`, its records
   (`FUN_1800b61a0`, table `1800feb00`), and the node's filing `FUN_18009de80`/`FUN_18009de20`/`FUN_18009ef40`. The pair
   filter `FUN_1800161e0` calls the game's `ShouldCollide`, which `source-sdk-2013` publishes (`game/client/physics.cpp`).
   `IvpHullManager` already files records; check its key formula against `FUN_18009de80`'s before reusing it.
2. **A pair's mindists.** `FUN_180096680` (nine arguments), `FUN_1800975d0`, `FUN_1800977f0` (exact at birth; reuse
   `IvpMindistManager.LinkExact`/`AddRechecked`, `IvpMindistMinimize`, `IvpPairScheduler.Examine`), and the phantom
   `FUN_180097940` (its other path unread). The point goes into the object's frame through `IvpObjectCache.Matrix`
   (`FUN_180070800` — `IvpMatrix.ToObject`'s values, first two addends swapped).
3. **The larger mindist** `FUN_1800b21f0` and its slots (tables `1800fe960`, `1800fe9a8`; slots 6 and 8 unread), which a
   hull ledge (`+0x8 & 3`) gets instead of a plain one.
4. **Displacements**: the mesh manager's `FUN_180025bc0` calls the engine's virtual-mesh query, which is not in vphysics;
   `FUN_18007bea0` is unread.
5. The unit/scheduler layer, the filing layer, then the running path and step 7.

## Two things to decide by reading, not by asking

- **Units.** vphysics converts once at `CPhysicsEnvironment`'s boundary and runs IVP in metres; the new ports run in metres
  (their constants are the binary's), while older ones (`IvpPairScheduler`, `IvpEnvironment`) carry Source units converted.
  Valve's way is metres inside, conversion at the seam.
- **Step 7's instrument.** `vphysics.dll` exports `CreateInterface` with `VPhysics031` and `VPhysicsCollision007`
  (`src/public/vphysics_interface.h`), so a real corpse drop can likely be simulated in process and compared against the port
  end to end. Not tried.

## How the ports are built, so the next one matches

A port in `managed/Tf2DemoSalvage.Animation/Animating/`; its lanes in `tools/Tf2DemoSalvage.Probe/Oracle/Ivp*Replay.cs`
(linked into `Tf2DemoSalvage.Animation.Tests.csproj` with its `Data/*.txt`); a probe in `tools/.../Probes/Vphysics*Probe.cs`
with a control, `sweep n` and `fixture path` modes; a `*ConformanceTests` class with a fixture control. Constants by their bits
or as widened floats — a decimal literal cost an ulp twice this session. Every product and sum through `IvpMath` with the
disassembly's destination. GhidraMCP answers at `127.0.0.1:8089` (`disassemble_function`, `read_memory`, `get_xrefs_to`,
`list_exports`, and `POST /disassemble_bytes` for code Ghidra has no function for).
