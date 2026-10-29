# 2026-10-28 — B259's per-frame entity walks: the sample's track walk and the undrawable tally

What changed (branch `perf/b259-entity-walks`, `eb9910a3`): `DemoTimeline.Sample` collates from a maintained list of the
tracks that currently answer a prop (`_live`, kept by `SetLive`) instead of walking every track of the recording; and
`DrawTally.NotDrawable` works out its two key strings once per prop OBJECT and then only counts. Both are B259 walks
(`docs/RISKS.md` B259, the 2026-10-23 entry: "`sample`, `drawlist` and `collate` walk every entity").

**Status: the untimed half (engine reading, divergences, the count-first probe, predictions, method, controls) was
written and committed before any A/B run.** Deviations go under Result.

## The engine's shape, read today (evidence class: read from published source, source-sdk-2013)

- **The per-frame entity walks iterate the entities that exist.** `SimulateEntities` (`cdll_client_int.cpp:2000-2017`) and
  the rare full walk in `InterpolateServerEntities` (`c_baseentity.cpp:3241-3252`, only when the threading mode changes)
  use `C_BaseEntityIterator`, which walks `CClientEntityList::m_BaseEntities` and skips dormant ones
  (`cliententitylist.cpp:497-510`). An entity joins that list when it is created (`OnAddEntity`, `:340`) and leaves it
  when it is deleted (`OnRemoveEntity`, `:402-403`). **A recording's dead and unborn entities are on no list at all.**
- **Interpolation walks a maintained list.** `ProcessInterpolatedList` walks `g_InterpolationList` only
  (`c_baseentity.cpp:3123-3136`); `UpdateClientSideAnimations` its own list. Ours already does both (B259 stage C, fix 1).
- **The leaf system is told, and pays per change.** `UpdateVisibility` (`c_baseentity.cpp:1370-1432`) runs on a data update
  and adds or removes the renderable on `ShouldDraw() && !IsDormant()`; `AddToLeafSystem` (`:3938-3952`) either creates a
  handle (dirty) or calls `RenderableChanged`; a move calls `MarkRenderHandleDirty` (`:4533-4541`). `PreRender`
  (`clientleafsystem.cpp:528-598`) walks `m_DirtyRenderables` only, and `BuildRenderablesList` (`:1813-1836`) reads the
  visible leaves' lists through `CollateRenderablesInLeaf` (`:1574-1752`), which computes the box of each collated renderable
  (`CalcRenderableWorldSpaceAABB`, `:1636`). **An entity with no model never enters the leaf system, so no per-frame pass
  ever visits it.**
- `C_BaseEntity::Simulate` is `AddEntity` → `CreateLightEffects` (`c_baseentity.cpp:3142-3150`, `:3210-3213`): the one
  full walk the engine does each frame has an almost empty body.

## What we do differently (each a divergence; this record fixes the first two)

| ours | the engine's | cost on f12 (count-first probe below) | here |
|---|---|---|---|
| `Sample`'s closing collate walked **all 6,456 tracks** of the recording for ~492 live props | `m_BaseEntities`: live entities only | 0.13-0.14 ms a frame | **fixed**: `_live`, ordered by track |
| `DrawTally.NotDrawable` built two key strings and hashed three per call, for ~220 undrawable props a frame (220 of 220 the same object as the frame before) | never visits an entity with no model | 0.064-0.072 ms a frame | **fixed**: keys per object, counted in shared buckets |
| `EntityModelSet.Collate` still offers every prop of the moment to `CanDraw`, the identity check and the keyed path | told by `RenderableChanged`; walks dirty only | the rest of `collate.walk`, ~0.07 ms (220 identity hits, ~34 keyed unparented, ~53 parented) | filed, not changed: making it event-driven needs a change signal from the sample to the scene (B259 stage B's persistent drawn list, which D131 says is approved separately) |
| `ClientLeafSystem` leaf lists are `List<int>` with `Insert(0)` and `Remove` (O(n)) | `CBidirectionalSet`: O(1) at the head and by stored index | `pre.remove` + `pre.insert` ~0.03 ms (mixed with the device's own leaf system, below) | filed, not changed: under 0.03 ms and the head-insert order is load-bearing |
| `drawlist`: the `ShouldDraw` filters run per frame over every prop | `UpdateVisibility` on a data update | filters 0.022 ms empty, 0.06 first person; `players` 0.066 (players are on the interpolation list in the engine too) | filed: the filters read the viewer's vision, the round state and the owner's weapon, which change without the prop's own update, so moving them off the frame needs the engine's own trigger list for each, not read today |

## Count-first probe (throwaway instrument, Release, empty view and gummo first person, not kept)

One run per view of `main`'s code plus a stopwatch split (`B259Split`, untracked, never committed; its output was printed
to stdout every 500 rebuilds, the first two windows dropped as first-use builds, per `a-split-carries-its-first-use-builds`).
Figures are per rebuild (a rebuild is a frame here), ms; empty view first, first person in brackets.

| part | before | what it walks |
|---|---|---|
| `sample` | 0.29-0.31 (0.30-0.34) | |
| ↳ players (`PlayersAt`) | 0.06 | 24-ish players: the interpolation list's shape |
| ↳ wakes and lerps (`AdvanceSample`) | 0.015-0.036 (0.03-0.08) | 3-62 wakes, 2-6 lerping |
| ↳ **collate walk** | **0.132-0.139** (0.131-0.136) | **6,456 tracks, 489-494 live** |
| `drawlist` | 0.15 (0.21) | prepare 0.05, players 0.065, place 0.009, filters 0.022 (0.06) |
| `collate` (`Counters.Collate`) | 0.18-0.20 (0.22-0.26) | |
| ↳ reconcile walk | 0.125-0.146 | 524-531 props: 219-222 by identity, ~34 keyed unparented, ~53 parented (boxes 0.015-0.021), ~220 undrawable |
| ↳↳ **`NotDrawable`**, timed inside | **0.064-0.072** | 220 calls, **220 the same object as last frame** |
| ↳ stale sweep | 0.003 | 304-311 handles |
| ↳ `PreRender` | 0.043-0.049 | 29-40 dirty |
| ↳ build + gather | 0.004 (0.04-0.06) | 9 collated (143-226) |

After the change, the same probe (empty view, one run each): sample collate walk **0.0132-0.0142**; `NotDrawable`
**0.0096-0.0100** with its two stopwatch reads inside; `collate` 0.14.

**The `PreRender` split was mixed and is not used.** The device keeps a second `ClientLeafSystem` (detail models, the
draw's own collation) whose `PreRender` runs inside the draw, on the render thread in mode 2, and fed the same static slots
— so `pre.enumerateLeaves` read 0.08 ms against a model-set `PreRender` of 0.047 that contains it. Only the model set's
total is quoted.

**Control for the instrument:** the probe's `sample` and `collate` columns agree with the moment line's own (`sample 0.3`,
`collate 0.2` at its 0.# resolution) in the same runs, and its `#live` (489-494) agrees with `posed 0 of 1,852-1,888
selected` minus the ~1,360 static props and ~24 players (interpolated: the static count is from the 2026-10-27 record).

## Question

On f12, does B (`eb9910a3`) cost less per frame than A (`59b95325`, origin/main) in the empty view and in first person, by
the amount the probe predicts, with every drawn frame identical?

## Predictions, registered before the first run

- **Identity.** Paused `--shot` pairs at tick 30000, empty view and gummo first person: 0 world pixels differ
  (`2026-10-27-empty-view-breakdown/pixdiff.ps1 -WorldOnly`); whole-frame differences only in the top-bar HUD strip, no
  larger than the A/A control's. Mode-0 frame hashes (`TF2VIEW_FRAME_HASH=1 TF2VIEW_FIXED_FRAME=0.015`, gummo first person,
  `--measure 6`, A B A B): `pose` and `cull` equal on every tick compared by its first frame, A against B as often as A
  against A. Mode-2 verifier on B (`TF2VIEW_QUEUE_VERIFY=1`, same command as `2026-10-28-double-buffer-pose.md`):
  **0 changed frames**. Falsified by any world pixel, any pose or cull mismatch A/B that A/A does not also show, or a
  changed frame.
- **Empty view, steady window (seconds 6-20), means of four runs a build:**
  - `advance` (holds `sample` and `drawlist`) falls **0.11 to 0.16 ms**.
  - `camera pose` (holds `collate`) falls **0.04 to 0.08 ms**.
  - **Frame: −0.15 to −0.26 ms (4.5-7.5% of A's ~3.4 ms).** Falsified if under 0.10 ms or the two builds' ranges overlap;
    then no frame difference is claimed, only the parts.
- **First person, steady window:** the same `advance` and `camera pose` falls (the walks do not depend on the view); frame
  −0.15 to −0.26 ms (2-4% of A's ~6.9 ms), same falsifier.
- **Whole 20 s window** (`measured … mean`): reported beside the steady window; same direction, no separate band.
- `sound`, `hud`, `camera particles` inside each build's own spread (nothing they read changed).

## Method

Two Release builds out of the tree, each from a `git archive` at `D:/measure-f12/b259-{before,after}-src`, built
`-c Release` there and copied to `D:/measure-f12/b259-{before,after}`: A = `59b95325`, B = `eb9910a3`. Every run behind the
quiet gate (`run.ps1` beside this file: no other viewer, `dotnet`, `testhost` or `MSBuild` process, idle `/nodemode:1`
reuse nodes and `VBCSCompiler` not counted, then three `Win32_Processor.LoadPercentage` samples under 25%), under
`run-exclusive.ps1`, at 1280x720:

- first person: `<exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0`
- empty: `TF2VIEW_CAMERA="1613 -2821 154 89 0" <exe> D:/measure-f12/f12.dem --tick 30000 --measure 20 +fps_max 0`

Order (`all.ps1`): shots (A, B, and a second A as the control) per view; hash runs A B A B; the verifier run on B; then
ABBA BAAB first person, then ABBA BAAB empty; then a second shot pair. Reading:
`2026-10-27-empty-view-breakdown/read.ps1` (per-second lines after the first five, frame-weighted; % = part / that run's
steady frame mean); first-use (the first five lines) reported apart.

## Controls

- Every run ends at tick 31347 (~1,347 ticks in 20 s); empty-view moment lines read `posed 0`; first person 18 rebuild
  reports.
- The A/A shot and hash pairs bound what "identical" can mean between two runs of one build.
- A run whose load after exceeds 25% is judged against the other runs of its build, as in the earlier records.

## Environment

RTX 3060, i5-10600K, 2026-10-28 20:36-22:46. Release builds from `git archive` (the first build of each tree printed 117
warnings, an incremental rebuild 0; the same for both trees, and the in-tree build of `eb9910a3` is 0 warnings). Every run
passed the quiet gate and exited 0; TF2 was not running. Load (3 × 1 s, %) before / after, timed runs:

| run | before | after | | run | before | after |
|---|---|---|---|---|---|---|
| 01-fp-A | 21 13 18 | 20 24 11 | | 09-empty-A | 15 14 12 | 9 6 15 |
| 02-fp-B | 13 13 15 | 26 7 4 | | 10-empty-B | 13 9 18 | 18 17 9 |
| 03-fp-B | 19 14 19 | 9 13 17 | | 11-empty-B | 18 11 24 | 11 20 12 |
| 04-fp-A | 13 13 16 | 15 15 26 | | 12-empty-A | 20 7 24 | 14 11 17 |
| 05-fp-B | 19 18 12 | 10 16 9 | | 13-empty-B | 12 16 20 | 15 13 15 |
| 06-fp-A | 17 6 5 | 8 7 20 | | 14-empty-A | 17 10 21 | 12 4 6 |
| 07-fp-A | 5 10 8 | 9 13 10 | | 15-empty-A | 8 9 9 | 37 25 17 |
| 08-fp-B | 10 14 14 | 24 6 12 | | 16-empty-B | 2 20 6 | 40 9 7 |

The after-reads over 25% (04, 15, 16) are single samples as the viewer exited; no slow frames in those runs. This record
quotes every number it uses; the raw stdout, shots and hash logs are not committed (as `59b95325` settled for the
double-buffer record). Scripts kept: `run.ps1`, `all.ps1`, `hashcmp.ps1`.

**Deviations.** (1) Two hash runs were added after the plan (`hash-5-B`, `hash-6-A`), because `hash-2-B` disagreed with
every other run on `pose` (below) and the plan's four could not say whether that was the build. (2) Run 11-empty-B started
with a 24% sample, under the gate's 25%.

## Result

**Controls.** Every timed run ended at tick 31347. Empty view: `posed 0` in every moment line of all eight runs. First
person: 16 moment windows each.

**Identity: held.**

| pair | whole frame | world only |
|---|---|---:|
| empty A / B | 49, box (1101,2)-(1119,10) | 0 |
| empty A / A2 (control) | 28, box (1113,2)-(1119,10) | 0 |
| empty A / B, second pair | 0 | 0 |
| first person A / B | 24, box (1113,3)-(1119,10) | 0 |
| first person A / A2 (control) | 42, box (1101,2)-(1119,10) | 0 |
| first person A / B, second pair | 36, box (1113,2)-(1119,10) | 0 |

Every whole-frame difference is the frame-time digits of the overlay, as large in the A/A controls.

Mode-0 frame hashes, gummo first person, each tick by its first frame (`hashcmp.ps1`): **`cull` equal on every shared tick
of every pair** (229-237). `pose`: A1/B4 229/229, B5/A6 237/237, A3/B4 226/231, A1/A3 224/229 (the control), B5/A1 221/229.
**`hash-2-B` alone disagrees with all five other runs from tick 30010 on** (14 of 229-230 equal, against A and B alike),
while B4 and B5 match A exactly — so it is a run-to-run state, not the build. What that run's pose diverged on is not
established (*interpolated*: something with its own clock, such as the corpse worker; `cull` stayed equal, so the
collated set did not change). `particles`, `rest` and `image` differ in the A/A pairs as much as A/B, as finding 97
records for this instrument.

**Mode-2 verifier on B: `queue verify: 745500 frames checked, 0 changed`**, `mat_queue_mode -1: QueuedThreaded on 6
physical cores`, no exception, exit 0.

**Moment line, mean of each run's windows (0.01 ms resolution), A → B:**

| view | `sample` | `collate` |
|---|---|---|
| empty | 0.31-0.35 (mean 0.330) → 0.19-0.25 (0.215): **−0.115** | 0.20-0.21 (0.205) → 0.11-0.17 (0.135): **−0.070** |
| first person | 0.34-0.37 (0.353) → 0.22-0.24 (0.228): **−0.125** | 0.27-0.29 (0.278) → 0.20-0.23 (0.213): **−0.065** |

Ranges apart in all four; each inside the probe's after figures.

**Steady window (seconds 6-20), per-second lines frame-weighted, ms (% of A's mean steady frame):**

| view | part | A (four runs) | B (four runs) | B − A | prediction | |
|---|---|---|---|---|---|---|
| empty | `advance` | 0.530-0.628 (0.585) | 0.400-0.486 (0.444) | **−0.141 (4.7%)** | −0.11 to −0.16 | **held**, ranges apart |
| empty | `camera pose` | 0.523-0.610 (0.572) | 0.458-0.546 (0.509) | −0.063 (2.1%) | −0.04 to −0.08 | in band, **ranges overlap** |
| empty | frame | 2.731-3.231 (3.017) | 2.497-2.956 (2.755) | −0.262 (8.7%) | −0.15 to −0.26 | **not claimed**: ranges overlap (14-A 2.731 under 10-B 2.956) |
| first person | `advance` | 0.710-0.788 (0.749) | 0.556-0.629 (0.580) | **−0.169 (2.4%)** | −0.11 to −0.16 | above the band by 0.009, ranges apart |
| first person | `camera pose` | 3.177-3.520 (3.326) | 3.017-3.260 (3.117) | −0.209 | −0.04 to −0.08 | **outside**, ranges overlap |
| first person | frame | 6.718-7.480 (7.059) | 6.205-6.889 (6.477) | −0.582 (8.2%) | −0.15 to −0.26 | **not claimed**: outside the band and ranges overlap (05-B 6.889 above 07-A 6.718) |

Whole window (`measured … mean`): empty A 3.16-3.75 (3.49) → B 2.94-3.52 (3.25); first person A 7.51-8.18 (7.83) → B
7.04-7.70 (7.30). Same direction; no separate claim.

**First person moved by more than the change can explain, in parts it does not touch.** `camera particles` (unchanged
code) read A 1.584-1.768 (1.673) against B 1.502-1.721 (1.579), −0.09; `sound` −0.02. Every adjacent ABBA/BAAB pair has B
faster (by 0.36-0.86 ms), so it is not one bad run; but the measured parts of the change account for ~0.19 ms
(sample + collate on the moment line), and the rest is not attributed. One candidate, measured but not tested:
allocation fell 0.02-0.07 MB a frame (`gc:` lines: A 0.96-1.02, B 0.93-0.95 MB in first person; A 0.54-0.56, B 0.49-0.51
empty) and the gc pause by 0.00-0.03 ms, which is the tally's two strings per undrawable prop no longer being built.

**Scored.** Identity held. `advance` held in the empty view and overshot the band by 0.009 ms in first person (the band was
from one probe run per view). `camera pose` in the band in the empty view but overlapping; outside and overlapping in first
person. The frame is claimed in neither view, by the registered rule.

## Verdict

**Two of B259's whole-set walks are gone, measured where they run:** the sample's walk over the recording's 6,456 tracks
(−0.115 to −0.125 ms on `sample`) and the undrawable tally's string work (−0.065 to −0.070 ms on `collate`), together
**0.185 ms a frame in the empty view (6.1% of 3.02 ms) and 0.19 ms in first person (2.7% of 7.06)** (sum of the moment-line
differences; the frame itself is not claimed). Every drawn frame compared is the same: world pixels, `cull` hashes, and
the verifier's 0 changed frames.

**What B259 still walks per frame** (B, empty view): `sample` 0.215 (players 0.06, the wakes and lerps, the live list), 
`drawlist` ~0.15-0.21 (prepare 0.05, players 0.065, filters 0.02), `collate` 0.135 (the reconcile walk's identity and keyed
paths ~0.07, `PreRender` ~0.045). The divergences left are the three filed in the table above.
