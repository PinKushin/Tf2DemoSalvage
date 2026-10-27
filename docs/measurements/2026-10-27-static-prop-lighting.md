# Measurement 2026-10-27: static-prop lighting (`baked`) against the engine's per-draw work

**Status: plan.** Written and committed before the timed runs; the result is added below the Environment afterwards.

## Observation

1. `baked` (static props: colour mesh and the draw's lighting state, `EntityModels.Instances.cs` `bakedAt` … `BakedTicks`)
   read **0.5-0.6 ms a frame** on f12 gummo first person in the last grid (`2026-10-27-bone-clock-and-threaded-setup/0N-*.txt`,
   per-second `moment cost` lines: `baked 0.2`-`0.7`, mode 0.6) of a frame of 8.53-8.72 ms. Finding 101 had it at
   0.3-0.4 (`2026-10-22-f12-camera-breakdown.md`).
2. A one-off split of that bucket (a throwaway counter build, never committed; its three lines are `split.txt` here),
   20 s of f12 gummo first person, per frame over every frame of the process, opening seek included:
   - 163-169 prop visits; **51-54 take the full-lighting path** (bump-mapped model, B514), **73-76 the colour-mesh
     path**; the rest have neither (dynamic props, or static ones without `.vhv` or `$staticprop`).
   - **No visited prop has a styled light listed (0.0) and no dlight is live (0.00)**, yet each of those ~125 calls
     walked all 32 dlight slots, with a `ClusterAt` per slot, plus one for the prop and a `List` built by LINQ:
     3,963-4,154 slot reads a frame.
   - Timed parts: colour-mesh path 0.183-0.204 ms; full path 0.282-0.351 ms = static-state lookup 0.152-0.207, **of which
     its 174 first-use builds are 0.146** (split3), and the styled/dynamic gather 0.128-0.136; the prop's own `ClusterAt`
     0.019-0.023 (inside both). Colour-mesh lookups 0.031-0.037.
   - So **~0.31-0.34 ms a frame is the bit-2 walk over dead slots** (colour-mesh path plus the full path's gather), and
     ~0.15 ms is first-use static states built during play.

## What the engine does (read from disassembly; the citations are the repo's)

- **At load**, `CStaticPropMgr::PrecacheLighting` (`engine.dll 0x180205b20`) builds each prop's light cache handle
  (`FUN_1801b8350`) at its lighting origin (RISKS B427): the static state at `+0x18` (`ComputeStaticLightingForCacheEntry`,
  `0x1801b8270`, finding 65) and the styled-light list at `+0x1b0` (`FUN_1801b6bf0`, finding 67).
- **Per draw**, `FUN_1801ba590(handle, …, 6 + (+0x1a4 != 0))` for a colour-mesh prop, flags 7 for a bump-mapped one
  (finding 67, B514): bit 4 is the listed styled lights, **cached at `+0x88` and rebuilt only when a listed style's stamp
  passes `+0xf8`**; bit 2 is each dlight whose bit is in `+0x1a4`, **masked by the live set `DAT_1806996c4`
  (`r_dlightactive`)**, set from the dlight's side by `FUN_1801b5260` (finding 67 line 33).
- So with no listed styled light and no live dlight — every prop drawn here — the engine's per-draw lighting work for a
  static prop is the cached state and nothing else.

## Ours, branch by branch

| branch | ours | engine | |
|---|---|---|---|
| colour mesh | `_bakedByEntity`, built on first draw, then a lookup | built at load | per frame equal; first use differs (open, B595) |
| static state (flags 7) | `_staticStates`, built on first draw, then a lookup | `+0x18`, at load | per frame equal; first use differs (open) |
| styled list | `_styledByEntity`, built on first draw | `+0x1b0`, at load | per frame equal |
| styled ranking (bit 4) | re-ranked every frame when the list is non-empty | cached at `+0x88` until a style stamp | **diverges**; 0 props on f12 (open) |
| dlights (bit 2) | every one of 32 slots read per prop per frame, each with a `ClusterAt`, plus the prop's, plus a `List` and a LINQ copy | only the live set's bits | **diverges: the fix** |
| dlight marking | each prop tests each live dlight's PVS | each dlight marks the props it reaches (`FUN_1801b5260`) | shape differs, output equal; cost ∝ live dlights either way (0 here) |
| full path ranking | style-0 lights re-ranked with the added ones | added ranked against the cached static four | output equal while `StyleScale(0)` is 1; only runs when something is added (0 here) |

## Question

- A. With bit 2 read through the live set (and no allocation when there is nothing to gather), what does `baked` cost on
  f12 gummo first person, and the frame — before (B, `origin/main` `8122feca`) against after (A)?
- B. The same on an empty view (`TF2VIEW_CAMERA="1613 -2821 154 89 0"`, no `--first-person`).
- C. Is the drawn result unchanged: a `--shot` at the same tick and camera, B twice (control) and A.

## Prediction

- A: `baked` falls by 0.2-0.35 ms (the colour-mesh path's ~0.19 and the full path's gather ~0.13 go to near zero, less
  the split build's own timer overhead; the static-state first uses stay). Frame lower by about as much, possibly inside
  the spread. Falsified if `baked` falls by less than 0.1 ms.
- B: no change beyond noise unless static props are visited there.
- C: A's shot equals B's wherever B's two shots agree.

## Method

Release builds to `D:/measure-f12/spl-before/` (B, `8122feca`) and `D:/measure-f12/spl-after/` (A). One viewer at a time
under `run-exclusive.ps1`, each behind the quiet gate of `2026-10-27-threaded-bone-setup/run.ps1` (three
`Win32_Processor.LoadPercentage` samples under 25%, no other `tf2demoview`/`dotnet`/`MSBuild`/`testhost`).

- timing, 20 s each, `run.ps1 -Seconds 20` (f12 tick 30000, `--spectate gummo --first-person`, `+fps_max 0
  +r_threaded_particles 1 +cl_threaded_bone_setup 0`, 1280x720). Order ABBA BAAB: 01 A, 02 B, 03 B, 04 A, 05 B, 06 A, 07 A, 08 B.
- empty view, 20 s, same order on two pairs: E1 A, E2 B, E3 B, E4 A.
- identity: `--tick 30600 --shot` with `TF2VIEW_CAMERA` unset (first person), B, B, A; pixels compared.

## Control

- **Same work:** `posed N of M` and `built` within 3% across A and B, or the pair is void.
- **Machine:** `sound` and `between` within 10% across runs, or the outliers are named and set aside.
- **Shot control:** B against B; a pixel they disagree on says nothing about A.

## Environment

(filled in after the runs)
