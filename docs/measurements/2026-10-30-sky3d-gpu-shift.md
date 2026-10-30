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

(not yet run)

## Verdict

(not yet run)
