# Measurement 2026-10-30: where the GPU time between Clear and Sky3D moved, 2026-10-25 to 2026-10-30

## Record (written and committed before the first run)

## Observation

Two records of the same condition (f12 from tick 30000, gummo first person, 1280x720, `--measure 20 +fps_max 0
+mat_queue_mode 2`), read from their own tables:

| pass, GPU ms | `2026-10-25-render-thread-cpu-vs-gpu.md` (build `4a2878b0`, runs 06, 07) | `2026-10-30-b597-gpu-split.md` (build `22704fa9`, runs 01-N, 06-N, 07-N) |
|---|---|---|
| Clear | 1.29-1.35 | 0.078-0.084 |
| Sky3D | 0.083-0.084 | 0.769-0.797 |
| World | 0.355-0.361 | 0.475-0.484 |
| OpaqueModels | 0.828-0.846 | 0.845-0.878 |
| frame mean | 9.41-9.65 | about 6.8 (06-N: 6.84) |

Sky3D's primitives (7,300 then; 7,373-7,534 now), pixels (about 640,000) and CPU (0.19-0.22) are the same in both. So the
same Sky3D work reads nine times longer on the GPU, the first interval reads 1.2 ms shorter, and OpaqueModels reproduces.
The 10-25 record read Clear as "the GPU waiting from the frame's first timestamp to the first command". The 10-30 record
left the shift open. Whose observation: the lead's two records; nobody has run the old build since.

## Question

On this machine today, under the condition above: does build `4a2878b0` still read Clear about 1.3 and Sky3D about 0.08
GPU ms? If it does, which first-parent commit on main between it and `22704fa9` is the first to read Sky3D GPU above 0.4,
and what did that commit change about when the GPU waits, or where a mark sits?

## Hypothesis

None is favoured. (H1) environmental: driver, GPU clock, DWM, another process. (H2) a commit moved when the GPU starts
waiting within the frame (pacing: the frame went from about 9.5 to about 6.8 ms over these commits, and a GPU timestamp
delta is roughly max(submission pacing, execution), so the interval that absorbs the GPU's idle time can follow pacing).
(H3) the instrument's marks moved: `35000cda` replaced every `profile?.Mark` with `Passed(profile, ...)`, and `87d075e7`
(in `9ce67bb3`) added marks to the world cull. (H4) the Sky3D work itself got slower on the GPU (a state, a buffer wait).

## Prediction (before the runs)

Two builds are run today, interleaved, so H1 has a same-day control: C = `4a2878b0` (the control), B = `22704fa9` (the
known-bad end, the 10-30 record's own exe, which sits on origin/main `19f59cc3`).

- **H1** predicts C and B read the same today: C's Sky3D GPU about 0.78 (within 0.2 of B's) and C's Clear under 0.2.
- **H2, H3, H4** predict C reproduces its own record (Sky3D GPU 0.07-0.12, Clear GPU 1.1-1.5) and B reproduces its own
  (Sky3D GPU 0.70-0.85, Clear GPU under 0.15).
- The bisect then separates H2/H3/H4 by the first bad commit's diff: H3 if it moves a mark or adds GPU commands between
  marks without changing the work; H4 if Sky3D's GPU time rises with no fall in Clear (the sum Clear + Sky3D rises by
  about 0.7); H2 if Clear falls by about what Sky3D gains (sum roughly kept) and the commit changes CPU pacing rather
  than the render passes. My guess, not a prediction the verdict rests on: H2, at `35000cda` (double-buffer pose), the
  largest pacing change in the range.

## Falsified if

1. C's Sky3D GPU is above 0.4 in two of three runs and B's is within 0.2 of C's: then H1 (environment) explains the shift,
   and H2/H3/H4 are falsified for this range.
2. C's Sky3D GPU is under 0.2 in all three runs and B's above 0.4 in all three: H1 is falsified; the bisect runs.
3. Anything else (C or B split across runs, or between the bands): inconclusive for H1; run three more of each after a gap
   and report both sets.
4. In the bisect, the H2/H3/H4 reading above names a hypothesis; a first bad commit whose diff fits none, or a Sky3D that
   climbs in steps across several commits, is reported as inconclusive for the mechanism.

## Instrument

`Render/PassProfiler.cs` as each build has it: one Stopwatch read and one `D3D11_QUERY_TIMESTAMP` at each mark inside one
`TIMESTAMP_DISJOINT` per frame, printed by `--measure` as `render passes, mean per frame at WxH`. It is the same instrument
in every build of the range; `87d075e7` and `35000cda` changed where and how marks are called, which is H3, so the
instrument itself is under test here and its diff is read, not assumed.

## Method

**Builds.** Each candidate `<sha>`: `git archive --format=tar <sha>` extracted to `D:/measure-f12/gs-<sha8>-src`, then
`MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release` there, the whole output read (the
native Speex compile's about 117 third-party C4311 warnings on a first build are ignored; nothing else is), then the output
folder `managed/Tf2DemoSalvage.Viewer3D/bin/Release/net10.0-windows` copied no-clobber (`cp -rn`) to a FRESH folder
`D:/measure-f12/gs-<sha8>`. Then `pwsh build/reap-dotnet.ps1`, so no build process holds the quiet gate. B is the existing
`D:/measure-f12/b597-split` (built 2026-10-30 10:51 from `22704fa9`), not rebuilt.

**One run.** `pwsh docs/measurements/2026-10-30-sky3d-gpu-shift/run.ps1 -Name <nn-label> -Exe D:/measure-f12/<folder>/tf2demoview.exe`:
the quiet gate (no other viewer, dotnet, testhost or MSBuild process; three `Win32_Processor LoadPercentage` samples
under 25%), then under `run-exclusive.ps1`:
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2`,
`TF2VIEW_MODEL_VERTS` and `TF2VIEW_CAMERA` unset. Load is sampled after. A run whose after-load sample has two of three
at or above 25, or whose frame mean is more than 1.5 ms off its build's other runs, is discarded, listed as discarded,
and repeated. Raw stdout, stderr and the console line of each run go in `2026-10-30-sky3d-gpu-shift/`; `summ.ps1` prints
the figures read.

**Read per run:** frame mean, render thread draw, frames read on the GPU / skipped / disjoint, and CPU and GPU ms of Clear,
Sky2D, Sky3D, World, OpaqueModels and Present.

**Order.** H1 first: C B C B C B. Then, only if falsification clause 2 holds, bisect the first-parent commits on main
that touch `managed/` from `cab3cb94` (the first carrying the profiler, the merge of `4a2878b0`) to `efd5d496`, then B:

| # | sha | # | sha | # | sha |
|---|---|---|---|---|---|
| 1 | cab3cb94 | 12 | 018360bf | 23 | 752d6981 |
| 2 | ad0bae7c | 13 | da7fe37d | 24 | 9ce67bb3 |
| 3 | db5cd0f5 | 14 | 9bc84697 | 25 | cb7dbb21 |
| 4 | d63d7fa4 | 15 | 7b64300d | 26 | 35000cda |
| 5 | f82fc01f | 16 | e70683e8 | 27 | be16b9bd |
| 6 | 6ed66190 | 17 | 9c579ae6 | 28 | b5e74730 |
| 7 | 89e3dce1 | 18 | 0f1a9298 | 29 | 09979e33 |
| 8 | d24cf424 | 19 | e004d7b5 | 30 | f1ccfecb |
| 9 | 30790a30 | 20 | 0a2f9d53 | 31 | efd5d496 |
| 10 | 02259a8f | 21 | bc5db0de | (B) | 22704fa9 |
| 11 | 004cf29a | 22 | bd92c334 | | |

(The list is `git log --first-parent --reverse cab3cb94~1..origin/main -- managed/`; commits that touch no `managed/`
file cannot change the viewer binary.) Three runs each at the midpoint, halving. **A candidate is GOOD when all three
runs read Sky3D GPU under 0.2, BAD when all three read above 0.4.** A candidate in between (any run 0.2-0.4, or a split
across runs) is not forced: it gets three more runs, and if it stays between, the shift is reported as gradual across that
range and the "first bad commit" as inconclusive. A candidate that does not build is named and its neighbour taken. The
first bad commit is the BAD candidate whose list predecessor is GOOD, both measured; then its diff is read for the why.

## Control

(a) The instrument's own controls per run: frames read on the GPU about equal to frames drawn, skipped and disjoint near
0 (a run where they are not is void). (b) Sky3D's primitives and pixels per frame about equal across builds (within 5%):
the same sky work was drawn, so a GPU move is not a different scene. (c) OpaqueModels GPU within 0.80-0.90 in every
build, as both records read it: a pass whose work did not change must not move, which shows the clock and the GPU are
comparable across builds. (d) Control and bad builds interleaved on the same day (H1's control).

## Environment

This machine (Windows 11, NVIDIA driver, windowed), 1280x720 back buffer, mode 2. Builds: C = `4a2878b0` built today under
`D:/measure-f12/gs-4a2878b0`; B = `22704fa9`, `D:/measure-f12/b597-split`; candidates as built. Record branch
`investigate/sky3d-gpu-shift` from origin/main `19f59cc3`. Load before and after: per run, in the run's console line.

## Result

Raw stdout and stderr of every run, discarded ones included: `2026-10-30-sky3d-gpu-shift/`; console lines (time, exe,
load before and after) in `runlog-h1.txt` (01-06) and `runlog-h1b.txt` (07-10); `summ.ps1` prints every figure below.
Exes: C = `D:/measure-f12/gs-4a2878b0` (built today from `git archive 4a2878b0`, Release, 0 errors, 117 warnings, all
from the native Speex/Silk fetch and compile: 115 C4311, one `refs/tags/Speex-1.2.1 ... is not a commit`, one LNK4070);
B = `D:/measure-f12/b597-split` (`22704fa9`, built 2026-10-30 10:51 by the lead, not rebuilt).

**Per run, ms per frame, GPU (CPU in brackets where read).** Every run: 1280x720, frames read on the GPU equal to frames
drawn, 0 skipped, 0 disjoint (control a met in all ten).

| run | build | load after | frame | render thread | Clear GPU | Sky3D GPU (CPU) | World GPU (CPU) | OpaqueModels GPU (CPU) | kept? |
|---|---|---|---|---|---|---|---|---|---|
| 01-C | 4a2878b0 | 23 7 38 | 13.86 | 2.91 | 0.191 | 0.921 (0.289) | 0.543 (0.195) | 0.970 (1.348) | discarded: frame 4 ms off 03-C/07-C |
| 02-B | 22704fa9 | 37 63 100 | 8.34 | 2.31 | 0.115 | 0.839 (0.201) | 0.516 (0.203) | 0.893 (1.165) | discarded: load after |
| **03-C** | 4a2878b0 | 6 5 8 | 9.88 | 2.34 | **0.117** | **0.799** (0.226) | 0.510 (0.159) | 0.851 (1.120) | kept |
| **04-B** | 22704fa9 | 9 23 18 | 7.76 | 2.37 | **0.091** | **0.883** (0.215) | 0.506 (0.214) | 0.905 (1.203) | kept |
| 05-C | 4a2878b0 | 55 77 82 | 12.65 | 2.90 | 0.146 | 1.013 (0.272) | 0.555 (0.223) | 0.891 (1.345) | discarded: load after |
| 06-B | 22704fa9 | 61 25 96 | 12.55 | 2.94 | 0.153 | 1.012 (0.253) | 0.625 (0.250) | 0.893 (1.483) | discarded: load after |
| **07-C** | 4a2878b0 | 8 29 7 | 10.06 | 2.35 | **0.113** | **0.813** (0.224) | 0.518 (0.156) | 0.864 (1.132) | kept |
| **08-B** | 22704fa9 | 11 3 17 | 7.04 | 2.16 | **0.089** | **0.801** (0.195) | 0.490 (0.194) | 0.877 (1.094) | kept |
| 09-C | 4a2878b0 | 27 77 63 | 10.13 | 2.37 | 0.121 | 0.805 (0.233) | 0.513 (0.160) | 0.842 (1.122) | discarded: load after |
| 10-B | 22704fa9 | 31 74 74 | 6.73 | 2.09 | 0.086 | 0.767 (0.190) | 0.475 (0.186) | 0.859 (1.055) | discarded: load after |

Another agent's viewer (`D:/measure-f12/flex-a`) ran between my runs under the same lock; the after-load breaches are
consistent with it starting as mine ended. **Two quiet runs per build, not three.** The planned 11-C/12-B were not run:
all ten runs, kept and discarded, read the same state, so a third quiet pair could not change the reading (the
coordinator agreed). Sky3D primitives 7,276-7,627 and pixels 636,000-653,000 in every run (control b met).
OpaqueModels GPU 0.842-0.905 in all but 01-C (0.970, discarded) (control c met).

**Today, the control build reads the 10-30 split, not its own 10-25 split**: Clear 0.113-0.117, Sky3D 0.799-0.813, World
0.510-0.518, against its record's 1.29-1.35 / 0.083-0.084 / 0.355-0.361. Its frame (9.88, 10.06) and render thread
(2.34, 2.35) are 0.2-0.6 ms and 0.05-0.10 ms above its record's 9.41-9.65 and 2.25-2.29.

**Evidence found after the record was committed, outside the pre-registered method: the committed raw runs of every
per-pass record since 10-25.** Same condition in each (f12 tick 30000, gummo first person, 1280x720, mode 2 by a nonzero
render-thread draw; E, h, v and 984x551 runs excluded). Builds from each record's Environment section. State 1 = Clear
above 1.0 and Sky3D under 0.15; state 2 = Clear under 0.3 and Sky3D above 0.5.

| record | build(s) | state 1 runs | state 2 runs |
|---|---|---|---|
| 2026-10-25 render-thread-cpu-vs-gpu | 4a2878b0 | 01, 06, 07, 12 (M) | **15-M** (Clear 0.109, Sky3D 0.776; discarded by its record for load, so not quiet) |
| 2026-10-26 b597-constant-ring | A c0584951, B its head | 01-11, 20-A | 12-B to 19-B, **A and B both** (Clear 0.138-0.149, Sky3D 0.926-0.958) |
| 2026-10-26 b597-face-cache | its A/B | all 8 | none |
| 2026-10-26 b597-flex-range | its M/R | only 09-R-verify (a verify run at a 21 ms frame) | 01-08, 11-14, M and R |
| 2026-10-26 threaded-particles-and-sun | its C/A | 01-C-S, 04-C-T | 02, 03, 05-11, C and A both |
| 2026-10-27 static-prop-lighting | A f9edd46c, B 8122feca | 01-03, 05-12 | 04-A |
| 2026-10-27 static-prop-handle-at-load | A e761fc6e, B 06ae76c2 | 01, 02, 04, 07-11 | 05-B, 06-A, 12-A (03-B between: 0.979 / 0.401, read as a flip inside the run, interpolated from a mean) |
| 2026-10-27 bone-clock-and-threaded-setup | its builds | none | 01-08 |
| 2026-10-28 pvs-node-prune (fp runs) | its A/B | 09-16 | none |
| 2026-10-30 b597-gpu-split | 22704fa9 | none | 01-N, 06-N, 07-N |
| this record | 4a2878b0, 22704fa9 | none | all 10 |

So both states occur on the same build within one session, and both builds of an A/B flip together (constant-ring 12-19,
static-prop-handle 05-06, threaded-particles 02-03). State 1 is seen as late as 2026-10-28; state 2 as early as 2026-10-25
(15-M, on the control build itself). In state 2 the sum Clear + Sky3D + World is lower (about 1.4 against 1.75):
Clear loses about 1.2, Sky3D gains about 0.7, World gains about 0.15; OpaqueModels does not move.

**Machine facts read today (read, nothing changed):**

| fact | value | what it proves |
|---|---|---|
| GPU, driver | RTX 3060, 32.0.16.1088, driver dated 2026-07-21; driver store folder and `nvwgf2umx.dll` dated 2026-10-14 / 2026-08-18 | no driver install between 10-25 and 10-30 |
| Windows | 11 Pro 10.0.26300, last boot 2026-10-16 06:25; last hotfixes 2026-10-14/16 | no reboot or OS build change in the window |
| Windows Update installs 10-24 to 10-30 (event 19, Store apps and Defender signatures left out) | .NET 10.0.14 and 9.0.22 runtimes 10-26 01:30; PowerShell 7.6.9 10-26 01:43; VS 2022/2026 security updates 10-26 to 10-29; Defender platform 10-28; Windows Security platform 10-30 07:24 | the .NET 10.0.14 runtime is shared by both builds and arrived 10-26 01:30, but **state 2 occurs before it (10-25 15-M) and state 1 after it (10-26 face-cache, 10-27, 10-28)**, so it does not select the state. Not ruled out as a contributor to the frame drift. |
| display | one monitor, 1920x1080 at 239 Hz, primary | single display, no second monitor now (10-25 not recorded) |
| power plan | High performance | not recorded on 10-25 |
| GPU state at one moment | P0, 1837 MHz graphics, 43% utilisation, 51 C (another agent's viewer was running) | one sample; says nothing about the state |
| HDR / overlays | `AutoHDROptOutApplicable 0`; no per-app GPU preference for tf2demoview; Xbox Game Overlay updated by the Store on 10-24, 10-25 and 10-29 | Store app updates are spread through both states; HDR state itself not read |

## Verdict

**H1 confirmed: no commit moved the split.** The control build `4a2878b0` rebuilt today reads Clear 0.11 / Sky3D 0.80
(two quiet runs, eight in all), the same as `22704fa9` (Clear 0.09 / Sky3D 0.80-0.88). The prediction's H1 branch held
and falsification clause 1 is met: **H2, H3 and H4 are falsified for this range**, and my guess of `35000cda` is
falsified with them. No bisect was run, because the signal does not depend on the build.

**The mechanism is narrower than a dated environmental change.** The committed archive shows the split is a **two-state
condition that flips between runs of the same binary in the same session**, in both directions, from 2026-10-25 to
2026-10-30, and both builds of an A/B pair flip together. No driver, OS or display change occurred in the window; the one
runtime install (.NET 10.0.14, 10-26) is bracketed by both states. **What selects the state is not settled** (candidates
not measured: DWM presentation mode of the window, GPU clock or power state, another process on the GPU). Two
consequences that hold whatever selects it:

- **Clear, Sky3D and World GPU are state-dependent; OpaqueModels is not.** Per-pass GPU figures from different records
  compare only within one state. Clear GPU classifies a run: above about 1.0 is state 1, under about 0.3 state 2.
- **The state may move the frame mean** (a lead, not a measurement): in `2026-10-27-static-prop-handle-at-load`, build A
  read 8.37-8.38 ms in state 1 (07, 09) and 9.20-9.33 ms in state 2 (06, 12). An A/B that mixed states could carry that
  as a build difference. Not tested here.

Inconclusive, and left open: what selects the state; whether it moves the frame; and the control build's 0.2-0.6 ms
frame drift since its record. The state alone may account for that drift, if the lead above holds.
