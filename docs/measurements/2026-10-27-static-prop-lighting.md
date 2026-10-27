# Measurement 2026-10-27: static-prop lighting (`baked`) against the engine's per-draw work

**Status: measured 2026-10-27.** The plan above the Environment was committed (`714b2938`) before any timed run; one
replacement ABBA (09-12) was added after the grid, for the reason given in the Environment.

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

RTX 3060, i5-10600K; Release builds as in the Method (A from `f9edd46c`, B from `8122feca`); 2026-10-27 10:44-11:54.
Every run passed the gate first time (load before and after each: `grid-log.txt`). **Machine control failed for 01-A, 03-B and 04-A**: `sound` 0.23-0.33 and `between` 0.41-0.53 against
0.16-0.17 and 0.31-0.34 elsewhere, with load after 01 at 82/96/71 and after 03 at 69 (another workload on the machine);
they also have 10-14 moment lines against 16. They are set aside. **Added after the grid, not in the plan:** one more
ABBA (`grid2.ps1`: 09-A, 10-B, 11-B, 12-A) to replace them; it ran on a slightly busier machine (`sound` 0.16-0.19,
`between` 0.31-0.37).

## Result

Raw output: `2026-10-27-static-prop-lighting/NN-*.txt`; table by `read.ps1` (moment lines after the first, plain mean;
frame is the window's own `measured … mean` line; `sound`/`between` from the window's `phases` line). ms a frame.

| run | frame | baked | pose | camera pose | sound | between | posed | built |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 02 B | 8.69 | 0.431 | 3.51 | 3.29 | 0.17 | 0.33 | 34.7 | 34.7 |
| 05 B | 8.52 | 0.488 | 3.64 | 3.20 | 0.17 | 0.34 | 36.2 | 36.1 |
| 06 A | 8.05 | 0.075 | 3.23 | 2.90 | 0.17 | 0.31 | 37.1 | 37.1 |
| 07 A | 7.95 | 0.056 | 3.16 | 2.82 | 0.16 | 0.31 | 37.4 | 37.4 |
| 08 B | 8.43 | 0.450 | 3.61 | 3.24 | 0.17 | 0.31 | 35.8 | 35.9 |
| 09 A | 8.06 | 0.050 | 3.21 | 2.88 | 0.16 | 0.31 | 36.8 | 36.7 |
| 10 B | 8.97 | 0.444 | 3.70 | 3.36 | 0.18 | 0.37 | 36.2 | 36.2 |
| 11 B | 9.13 | 0.450 | 3.76 | 3.42 | 0.19 | 0.37 | 36.5 | 36.5 |
| 12 A | 8.68 | 0.047 | 3.17 | 3.04 | 0.19 | 0.36 | 35.4 | 35.3 |
| *01 A, set aside* | 9.51 | 0.050 | 3.42 | 3.20 | 0.23 | 0.41 | 33.7 | 33.8 |
| *03 B, set aside* | 13.91 | 0.520 | 4.69 | 4.43 | 0.33 | 0.53 | 34.6 | 34.5 |
| *04 A, set aside* | 11.03 | 0.215 | 3.94 | 3.37 | 0.28 | 0.52 | 32.1 | 32.1 |

Empty view (`E*-*.txt`): frame A 3.44-3.45, B 3.46-3.49; `baked` 0.000 and `posed` 0 in all four — no prop is visited
there, so nothing this change touches runs.

Identity (`shots.txt`, `compare.ps1`): A's shot equals both B shots on every pixel outside the frame-rate text, where B
and B also differ (25 pixels) — the control.

Controls, applied as the plan wrote them:

- **Machine (`sound`, `between` within 10% across runs):** passed by 02, 05, 06, 07, 08, 09 (0.16-0.17 and 0.31-0.34).
  **10-B, 11-B and 12-A fail it** (`between` 0.36-0.37, `sound` 0.18-0.19, ~19% above) and are set aside with 01, 03, 04.
- **Same work (`posed`/`built` within 3%, or the pair is void):** of the runs left, 05-B/06-A (2.5%), 05-B/09-A (1.7%)
  and 08-B/09-A (2.8%) pass. **02-B against every A run (6-8%), 08-B/06-A (3.6%) and 08-B/07-A (4.5%) are void.** In
  every void pair A did MORE work than B, so the direction cannot flatter A, but the rule is the rule.

## Verdict

**From the three pairs that pass both controls** (05/06, 05/09, 08/09):

- **`baked`: −0.40 to −0.44 ms a frame** (0.450-0.488 → 0.050-0.075), **4.7-5.1% of each pair's B frame** (8.43-8.52).
  Across every run, kept or not, `baked` is 0.43-0.52 in B and 0.05-0.08 in A apart from 04-A's 0.22 (a disturbed run), so
  the result does not depend on which runs are kept.
- **Frame: −0.37 to −0.47 ms** (A 8.05-8.06 against B 8.43-8.52; ranges apart), 4.4-5.5% of B. `camera pose` −0.30 to
  −0.39 (2.88-2.90 against 3.20-3.24).
- With the control-failing second ABBA included the frame is still lower in each adjacent pair (09/10 −0.91, 11/12
  −0.45), but those runs say nothing under the plan's own rule.

Prediction A (−0.2 to −0.35 on `baked`) **falsified on the high side**: the split read the walk's two paths at 0.31-0.34
ms and the fix took 0.40-0.44 off. Why the split under-read it was not measured (*interpolated*: the split averaged over
the opening seek's frames too, and its window and this one differ).

Prediction C **falsified as worded** ("A equals B wherever B's two shots agree"): the A-B difference box is
(1101,2)-(1119,10) and the B-B box (1113,2)-(1119,9), so A differs from B at a few pixels where the two B shots agree.
**Every one of them is inside the red frame-rate readout** (top right, about (1000,0)-(1280,12)), which prints a live
measurement and which no lighting state reaches; every other pixel is identical in all three. **What the shot can and
cannot show:** this tick has no styled light and no live dlight at any drawn static prop, so it checks that nothing
else changed, not the dlight path itself — the conformance tests carry that.

**What is left in `baked` (~0.05 ms):** the colour-mesh and static-state lookups (cached, as the engine's handle is) and
whatever first-use builds fall inside the window. **Not drawn or not compared** (D129): skinning runs on the GPU where
Valve's is on the processor; muzzle flashes, shell ejection and `DoAnimationEvents` unchecked since D129; f12 has no
styled light and no live dlight at this window, so the bit-4 cache and the dlight ranking were not exercised by these
runs — the conformance tests cover them.
