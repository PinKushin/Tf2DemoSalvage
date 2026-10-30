# Measurement 2026-10-30: what selects the two GPU states of the per-pass timing, and does the state move the frame

## Record (written and committed before the first run)

## Observation

`2026-10-30-sky3d-gpu-shift.md` (branch `perf/b597-gpu-split`, merged there) found that the per-pass GPU figures of
Clear, Sky3D and World flip between two states in runs of the same binary in the same session, in both directions, on
every record from 2026-10-25 to 2026-10-30, and that both builds of an A/B pair flip together:

| pass, GPU ms | state 1 | state 2 |
|---|---|---|
| Clear | about 1.3 | about 0.1 |
| Sky3D | about 0.08 | about 0.8 |
| World | about 0.35 | about 0.5 |
| OpaqueModels | unchanged | unchanged |

`2026-10-25-render-thread-cpu-vs-gpu` runs 06-M (state 1) and 15-M (state 2) are one record and one binary. Clear GPU
classifies a run: above 1.0 is state 1, under 0.3 state 2. No driver, OS or display change occurred between 10-25 and
10-30. A lead, not a measurement: in `2026-10-27-static-prop-handle-at-load`, build A read frame mean 8.37-8.38 ms in
state 1 (runs 07, 09) and 9.20-9.33 ms in state 2 (06, 12). Whose observation: the lead's records and the 10-30 shift
record's reading of the archive. One more, my own, made while testing this record's instrument (`winsample.ps1`, observe
only, against another agent's viewer at 13:37): that viewer's window was NOT the foreground window, and one window
above it in z-order overlapped it, the foreground app `T3 Code (Beta)`. So an agent-launched run has so far been a
background, covered window, which is the plain condition below.

## Question

(Q1) On this machine, under the f12 condition below, what selects the state: the viewer window's foreground state and
whether another window overlaps it (the DWM presentation path), the GPU's performance state and clocks, the time since
the previous viewer process exited (cold against warm GPU), the window's position, or `mat_queue_mode`?
(Q2) Does the state move the frame mean, the render thread time, or the main-thread phases (`camera`, `advance`, `hud`,
`between`), by more than the spread within one state?

## Hypothesis

H-P (presentation): the state follows the DWM presentation path, which a window's foreground state and occlusion select
(a FLIP_DISCARD swap chain is eligible for independent flip only when unoccluded); the plain runs flip because the
covering window changes as the owner works. H-G (clocks): the state follows the GPU's clock or P-state. H-K (cold/warm):
it follows how long the GPU idled before launch. H-W (window position). H-Q (`mat_queue_mode`). H-P is favoured; it is a
guess from the observation above, not a measurement.

## Prediction (before the runs)

- **Base rate:** both states occur in 8 plain runs (the archive has both under agent launches). If all 8 read one state,
  the candidate search below looks for a condition that produces the other.
- **H-P:** forced foreground selects one state in 4 of 4 runs while counterbalanced plain runs are mixed or the other
  state. My guess, which the verdict does not rest on: foreground reads state 1. Among plain runs, the state follows the
  sidecar's `above` (covered or not) and `fgproc`.
- **H-G:** the state follows the median graphics clock or P-state over the measured 20 s, one band per state with no
  overlap. Read on every run, no manipulation (nothing may be changed in the driver or power plan).
- **H-K, H-W, H-Q:** I predict none of these selects the state (both states appear within each condition's runs, or the
  condition's runs match the plain runs' mix).
- **Q2:** the frame mean is higher in state 2 by about 0.8 ms (the lead), with no overlap between the per-state ranges
  of one binary; render thread draw and `camera` follow it; `advance` and `hud` do not move.

## Falsified if

1. A candidate SELECTS the state if all of its runs (at least 4) read one state, and the counterbalanced runs at the
   other level (plain, or the other value) read the other state in at least 2 of 4. It is NOT a selector if both states
   appear among its own runs. Anything else is inconclusive for that candidate.
2. H-P is falsified if forced-foreground runs read both states; H-G if the clock bands of the two states overlap or the
   P-state is the same in both; H-K if cold runs read both states.
3. If every run of the whole record reads one state, Q1 is inconclusive (no variation to explain), and the record says
   which conditions were tried.
4. Q2: the state moves a figure if, among runs of this binary, the per-state ranges of that figure do not overlap and
   the means differ by more than the larger within-state range; it does not move it if the ranges overlap and the means
   differ by less than the within-state range. The archive is compared within one binary only (pairs of runs of one
   build in different states), because the frame mean moved with the builds.

## Instrument

`Render/PassProfiler.cs` (one Stopwatch read and one `D3D11_QUERY_TIMESTAMP` at each mark inside one `TIMESTAMP_DISJOINT`
per frame), printed by `--measure` as `render passes, mean per frame at WxH`, plus the rate line's `frames, mean` and
`phases, mean ms`. Two sidecars, read-only towards the viewer, run in EVERY run including the plain ones so they are
part of the constant load: `nvidia-smi --query-gpu=timestamp,pstate,clocks.gr,clocks.mem,power.draw,utilization.gpu
--format=csv -lms 500` (nvidia-smi exists at `C:/WINDOWS/system32/nvidia-smi`), and `winsample.ps1` (every 500 ms:
whether the viewer's top-level window is the foreground window, the foreground process, the viewer's rect, and how many
visible, uncloaked top-level windows above it in z-order intersect it). `summ.ps1` reads the last 20 s of the sidecars,
which is the measured interval, since the viewer exits right after it.

## Method

**Build.** `git archive --format=tar 19f59cc3` (origin/main) extracted to `D:/measure-f12/gss-19f59cc3-src`;
`MSBUILDDISABLENODEREUSE=1 dotnet build managed/Tf2DemoSalvage.Viewer3D -c Release` there (whole output read: 0 errors,
117 warnings, all native Speex/Silk: C4311, the Speex tag notice, LNK4070); output copied no-clobber to the fresh folder
`D:/measure-f12/gss-19f59cc3`. One binary for the whole record.

**One run.** `pwsh docs/measurements/2026-10-30-gpu-state-selector/grid.ps1 <names>`, which calls `run.ps1` per name:
the quiet gate of `2026-10-30-sky3d-gpu-shift/run.ps1` unchanged (no other viewer, dotnet, testhost or MSBuild process;
three `LoadPercentage` samples under 25%), then under `run-exclusive.ps1`:
`TF2VIEW_AUTOPLAY=1 TF2VIEW_VIEWPORT_SIZE=1280x720 <exe> D:/measure-f12/f12.dem --tick 30000 --spectate gummo --first-person --measure 20 +fps_max 0 +mat_queue_mode 2`,
`TF2VIEW_MODEL_VERTS`, `TF2VIEW_CAMERA` and (unless the condition sets it) `TF2VIEW_WINDOW_POS` unset. The run name's
letter is its condition:

| letter | condition | how |
|---|---|---|
| P | plain | the command above; the window opens wherever the viewer puts it, not foreground |
| F | foreground | `winsample.ps1 -Foreground`: once the window exists, AttachThreadInput + SetForegroundWindow + BringWindowToTop, re-applied on any sample that finds it lost |
| C | covered | `winsample.ps1 -Cover`: a TopMost, never-activated form over the left half of the viewer's window |
| O | position | `TF2VIEW_WINDOW_POS=40,40` |
| K | cold | launch only after 300 s with no tf2demoview process seen (the plain runs follow the previous run within the gate's 10-30 s) |
| Z | queue mode 0 | `+mat_queue_mode 0` instead of 2 |

**Order.** (1) Base rate: 01-P to 08-P, consecutive. (2) Foreground counterbalanced against plain: 09-F 10-P 11-P 12-F
13-F 14-P 15-P 16-F. (3) Then, in this order and each counterbalanced the same way against plain (F P P F pattern),
covered (C), cold (K), position (O), queue mode 0 (Z), as time allows; a condition that selected the state in (2) is
re-run once more as 4 runs before any other is tried. A run whose after-load sample has two of three at or above 25, or
whose frame mean is more than 1.5 ms off the median of its own condition, or whose sidecar shows another tf2demoview
running, is discarded, listed as discarded, and repeated.

**Q2, archive.** `summ.ps1 -Dir` over the committed raw runs of the records the 10-30 shift record listed for this
condition, read per build, plus this record's runs.

## Control

(a) The profiler's own: frames read on the GPU about equal to frames drawn, skipped and disjoint near 0 (else the run
is void). (b) OpaqueModels GPU within 0.80-0.95 in every run: a pass both states leave alone must not move. (c) The
sidecar must see what must be there: `fg` reads 1 in the F runs (a forced foreground it cannot see means the instrument,
not the viewer, is wrong), `above` reads at least 1 in the C runs, and the GPU sampler reads P0 with nonzero utilisation
during every run. (d) Each condition is interleaved with plain runs on the same binary in the same session.

## Environment

RTX 3060, driver 32.0.16.1088, Windows 11 Pro 10.0.26300, one monitor 1920x1080 at 239 Hz, High performance power plan
(as the 10-30 shift record read them; nothing changed). The viewer swap chain presents `FLIP_DISCARD` with sync
interval 1, which the driver's global vsync-off overrides (MainForm comment). Build `19f59cc3` as above. Record branch
`investigate/gpu-state-selector` from origin/main `19f59cc3`. Load before and after: per run, in the run log.

## Result

Raw stdout/stderr of every run, discarded ones included, with both sidecars (`NN-L.win.csv`, `NN-L.gpu.csv`; `NN-L.pmon.txt`
from 14-P on): `2026-10-30-gpu-state-selector/`. Console lines (time, condition, idle seconds, load before and after):
`runlog-base.txt` (01-08), `runlog-fp.txt` (09-16), `runlog-wu.txt` (17-18), `runlog-xmp.txt` (33-44), `runlog-s.txt` (45-50).
`summ.ps1` prints every figure here. One binary, `19f59cc3`, the whole time, 13:54 to 17:24. Every run that measured:
1280x720, frames read on the GPU equal to frames drawn (one short at most), 0 skipped, 0 disjoint (control a met), and
ending at tick 31346-31348 (no stray key paused or sought a foreground run).

**Amendments, labelled as added after the base rate (the Method above is unchanged; what was run instead is here):**

1. **Instrument defect, found by its own control.** The first `winsample.ps1` took `Process.MainWindowHandle`, which
   is the viewer's 376x129 loading splash. In 01-P the SPLASH was the foreground window, so control (c) would have
   passed a forced-foreground run whose main window was still in the background. Fixed before 03-P: the sidecar now
   takes the process's largest visible top-level window and waits for one at least 1000 px wide. 01-P and 02-P have
   no main-window rows.
2. **Columns added mid-record:** `viewers` (tf2demoview processes per sample, from 09-F; never above 1 in a measured
   interval), `covered` (from 14-P: share of a 32x18 point grid over the viewer's rect lying under a window above it
   or off the 1920x1080 screen; `above` only says one window intersects), and `nvidia-smi pmon` (from 14-P).
3. **Conditions added:** W (the whole rect under a TopMost form), M (minimized with SW_SHOWMINNOACTIVE), U (TopMost
   without activation), X (`TF2VIEW_WINDOW_POS=-3000,100`, the whole window off the screen), S
   (`TF2VIEW_WINDOW_POS=1820,100` plus TopMost without activation, so a 100 px strip is on screen and nothing covers
   it). **C, O, K and Z were not run.** The reason: the owner was at the desktop. He minimized both
   forced-foreground windows that stayed in front (13-F at 15:40:42, 16-F at 16:04:45). He minimized the TopMost
   18-U within seconds. He closed 17-W under its gray cover form. After 18-U only the non-intrusive conditions (X, M,
   P) and the 100 px strip (S) were used.
4. **17-W is the stalled run the coordinator reported** (the owner saw a viewer stall at boot shortly before 16:11).
   It launched 16:09:07. Its last loader line is `[demo] cp_process_f12, 106313 ticks, protocol 24` at 16:09:08.796.
   A `UI Automation client attached` line follows at 16:09:35, the only UIA attach in any of today's 40 viewer logs.
   At 16:10:25, while it was the foreground window, the window was closed: `render shutdown`, then `the message loop
   returned; Main is done`. Stdout holds only the lock line. Exit 0, no measurement.
   **It was not a hang by this log**: a normal run of this binary logs nothing for about 168 s after that same line,
   while it builds the position timeline (16-F: `16:04:25.139 ... protocol 24`, then `16:07:13.130 [demo] building
   the position timeline took 167.99s`). 17-W was closed 77 s into that silent stretch. It is not a state sample. The
   rerun is 18-U (state 1, 6.29 ms), but the owner minimized 18-U, so its condition was in effect M, not U.

**Discarded by the registered after-load rule** (two of three at or above 25): 01, 07, 08, 10, 12, 13, 14, 15.
The `viewers` column read 1 in every measured interval from 09-F on, and in each case the breach was the next process
starting after the viewer exited. Their state is read during the run, so they are listed for Q1. They are kept out of
the frame figures for Q2. 07-P (Opaque 1.049) and 12-F (frame 12.15, Opaque 0.996) also show contention inside the
run.

**Every run, ms per frame (`cov` is the share hidden; `M` = minimized; `fg` = viewer was the foreground window).**

| run | condition | window during the measured 20 s | state | frame | render thread | Clear / Sky3D / World / Opaque GPU | kept |
|---|---|---|---|---|---|---|---|
| 01-P | plain | (splash sampled) | 2 | 6.99 | 2.18 | 0.087 / 0.820 / 0.486 / 0.886 | load |
| 02-P | plain | (splash sampled) | 2 | 6.94 | 2.12 | 0.079 / 0.782 / 0.485 / 0.877 | yes |
| 03-P | plain | behind T3 Code, partly visible (a) | 2 | 7.36 | 2.31 | 0.090 / 0.851 / 0.507 / 0.873 | yes |
| 04-P | plain | behind T3 Code (a) | 2 | 7.04 | 2.16 | 0.089 / 0.794 / 0.487 / 0.868 | yes |
| 05-P | plain | behind T3 Code (a) | 2 | 7.01 | 2.17 | 0.084 / 0.806 / 0.493 / 0.883 | yes |
| 06-P | plain | behind T3 Code (a) | 2 | 6.94 | 2.11 | 0.078 / 0.775 / 0.478 / 0.867 | yes |
| 07-P | plain | behind T3 Code (a) | 2 | 8.19 | 2.53 | 0.103 / 0.963 / 0.587 / 1.049 | load |
| 08-P | plain | behind T3 Code (a) | 2 | 7.14 | 2.20 | 0.085 / 0.808 / 0.492 / 0.882 | load |
| 09-F | foreground | fg, taskbar over its bottom edge | 2 | 6.99 | 2.16 | 0.083 / 0.793 / 0.462 / 0.852 | yes |
| 10-P | plain | behind T3 Code (a) | 2 | 7.00 | 2.15 | 0.084 / 0.790 / 0.488 / 0.879 | load |
| 11-P | plain | behind maximized floorp (b) | 1 | 9.15 | 2.48 | 1.489 / 0.089 / 0.350 / 0.865 | yes |
| 12-F | foreground | fg, nothing above | 2 | 12.15 | 3.49 | 0.219 / 1.224 / 0.609 / 0.996 | load |
| 13-F | foreground | minimized by the owner | 1 | 8.02 | 2.47 | 1.534 / 0.077 / 0.324 / 0.868 | load |
| 14-P | plain | behind floorp, cov 1.00 | 1 | 8.17 | 2.59 | 1.530 / 0.090 / 0.353 / 0.903 | load |
| 15-P | plain | behind floorp, cov 1.00 | 1 | 9.16 | 2.48 | 1.438 / 0.085 / 0.346 / 0.886 | load |
| 16-F | foreground | minimized by the owner | 1 | 6.68 | 2.10 | 1.295 / 0.082 / 0.312 / 0.798 | yes |
| 17-W | full cover | closed during load (item 4) | - | - | - | - | stalled |
| 18-U | TopMost | minimized by the owner | 1 | 6.29 | 2.05 | 1.261 / 0.082 / 0.320 / 0.816 | yes |
| 33-X | off screen | cov 1.00, nothing above | 1 | 6.82 | 2.18 | 1.284 / 0.085 / 0.326 / 0.820 | yes |
| 34-M | minimized | M | 1 | 6.21 | 2.01 | 1.248 / 0.083 / 0.322 / 0.797 | yes |
| 35-P | plain | behind floorp, cov 1.00 | 1 | 7.08 | 2.12 | 1.241 / 0.085 / 0.340 / 0.855 | yes |
| 36-M | minimized | M | 1 | 6.32 | 2.02 | 1.259 / 0.083 / 0.320 / 0.821 | yes |
| 37-X | off screen | cov 1.00 | 1 | 6.57 | 2.14 | 1.259 / 0.085 / 0.323 / 0.825 | yes |
| 38-P | plain | behind floorp, cov 1.00 | 1 | 6.85 | 2.12 | 1.256 / 0.083 / 0.338 / 0.869 | yes |
| 39-P | plain | behind floorp, cov 1.00 | 1 | 7.00 | 2.16 | 1.265 / 0.084 / 0.338 / 0.852 | yes |
| 40-X | off screen | cov 1.00 | 1 | 6.74 | 2.12 | 1.243 / 0.084 / 0.328 / 0.811 | yes |
| 41-M | minimized | M | 1 | 7.06 | 2.21 | 1.352 / 0.065 / 0.269 / 0.857 | yes |
| 42-P | plain | T3 Code in front, floorp under it, cov 1.00 | 1 | 6.89 | 2.12 | 1.261 / 0.085 / 0.331 / 0.821 | yes |
| 43-M | minimized | M | 1 | 6.39 | 2.02 | 1.276 / 0.084 / 0.315 / 0.785 | yes |
| 44-X | off screen | cov 1.00 | 1 | 6.63 | 2.11 | 1.268 / 0.083 / 0.318 / 0.797 | yes |
| 45-S | 100 px strip | cov 0.94, nothing above, not fg | 2 | 6.78 | 2.11 | 0.084 / 0.768 / 0.484 / 0.863 | yes |
| 46-X | off screen | cov 1.00 | 1 | 6.65 | 2.11 | 1.267 / 0.083 / 0.320 / 0.796 | yes |
| 47-S | 100 px strip | cov 0.94 | 2 | 6.65 | 2.07 | 0.084 / 0.754 / 0.474 / 0.866 | yes |
| 48-S | 100 px strip | cov 0.94 | 2 | 6.63 | 2.12 | 0.088 / 0.776 / 0.475 / 0.876 | yes |
| 49-X | off screen | cov 1.00 | 1 | 6.74 | 2.12 | 1.262 / 0.086 / 0.321 / 0.794 | yes |
| 50-S | 100 px strip | cov 0.94 | 2 | 6.85 | 2.15 | 0.087 / 0.798 / 0.485 / 0.886 | yes |

45-50 console: every load before and after under 20. (a) The `covered` column did not exist yet. "Partly visible" is
interpolated from T3 Code's window read at about 16:00 (410,126 to 1526,914, not maximized), which leaves the
viewer's edges uncovered. (b) floorp was read maximized (-8,-8 to 1928,1040) at about 16:00. 11-P predates the
`covered` column.

**Q1 by condition** (all runs, discarded included, since the state is read during the run):

| condition | state 1 | state 2 | what the window was |
|---|---|---|---|
| any part of the window composed on screen (P behind T3 Code, F visible, S) | 0 | 15 | 01-10, 09-F, 12-F, 45/47/48/50-S |
| no part composed (P with cov 1.00, minimized by anyone, off screen) | 18 | 0 | 11, 13-16, 18, 33-44, 46, 49 |
| S (100 px visible, TopMost, background) against X (off screen), interleaved 17:01-17:24 | X 6 of 6 | S 4 of 4 | manipulation |
| M (minimized by the script) | 4 of 4 | 0 | manipulation |
| F (forced foreground) | 2 (both minimized by the owner) | 2 (both visible) | foreground does not decide it |

- **GPU clock:** the GPU was P0 at 1837 MHz graphics and 7501 MHz memory in every measured interval of every run,
  in both states, idle between runs included. The one exception is 16-F: median 1935 MHz, state 1. Utilization
  does differ with the state: X runs 26-29%, S runs 34-35%, at the same frame.
- **Idle before launch:** 12-238 s, both states at both ends. 08-P read state 2 after 144 s; 45-S state 2 after 43
  s; 46-X state 1 after 35 s; 49-X state 1 after 12 s. No 300 s cold run was taken.

**Q2, this record (kept runs, one binary).** The matched pair is S against X, interleaved in the same quiet half hour:
both background, nothing above either, differing only in whether a strip of the window is on screen.

| figure | S, state 2 (n 4) | X, state 1 (n 6) |
|---|---|---|
| frame | 6.63-6.85, mean 6.73 | 6.57-6.82, mean 6.69 |
| render thread draw | 2.07-2.15 | 2.11-2.18 |
| camera | 5.30-5.50 | 5.28-5.51 |
| advance | 0.63-0.65 | 0.61-0.64 |
| hud | 0.32-0.33 | 0.32-0.33 |
| between | 0.19-0.20 | 0.17-0.18 |

All kept runs pooled: state 2, n 10, 6.63-7.36, mean 6.92. State 1, n 17, 6.21-9.15, mean 6.83 (6.65 without
11-P, which ran while the owner was browsing in floorp in front of it). Minimized runs (M, 16-F, 18-U) read `between`
0.03 and `hud` 0.21-0.23, against 0.17-0.33 and 0.32-0.34 for every window that is not minimized, in either state. That
follows the window being minimized, not the state: X is state 1 too and reads 0.17-0.18 and 0.32-0.33.

**Q2, the committed archive**, read per record and build. Same condition: f12 tick 30000, gummo first person, 1280x720,
mode 2. E, h, v and verify runs excluded. OpaqueModels GPU beside each, the pass both states leave alone:

| record, build | state 1 frame (Opaque) | state 2 frame (Opaque) |
|---|---|---|
| 10-25 render-thread, M | 9.41-9.76, one 11.58 (0.83-0.87, 11.58 at 0.999) | 15-M 9.52 (0.846) |
| 10-26 constant-ring, A | 9.58-10.67 (0.83-1.05) | 10.67-10.75 (0.99-1.04) |
| 10-26 constant-ring, B | 9.89-10.31 (0.84-0.87) | 10.41-10.65 (1.00-1.03) |
| 10-26 threaded-particles, C-T | 04 8.74 (0.994) | 02 8.74, 08 8.94 (1.01-1.03) |
| 10-26 threaded-particles, C-S | 01 8.93 (0.991) | 05 9.03, 07 8.98 (1.02) |
| 10-27 static-prop-lighting, A | 7.95-8.68, one 9.51 (0.80-0.94) | 04 11.03 (0.883) |
| 10-27 static-prop-handle, A | 07 8.37, 09 8.38 (0.86); 01 13.39, 04 15.01 | 06 9.33, 12 9.20 (0.94-0.95) |
| 10-27 static-prop-handle, B | 8.35-8.88 (0.84-0.87); 02 14.85 | 05 9.83 (0.932) |

Where the archive's state 2 runs are slower (constant-ring, static-prop-handle), their OpaqueModels GPU is also
0.07-0.17 higher, on a pass the state does not move in any run of this record. static-prop-lighting 04-A (11.03) is
the exception: its Opaque is within its state-1 runs' range. Where Opaque matches
(threaded-particles, 10-25 15-M), the frames match within 0.2 ms. That is the pattern, stated without a conclusion:
the archive cannot separate the state from whatever else slowed those runs.

## Verdict

**Q1, confirmed (a selector found, by manipulation): the state follows whether any part of the viewer's window is
composed on screen.** Visible, even as a 100 px strip, background and not foreground: state 2 (Clear GPU about 0.08,
Sky3D about 0.8, World about 0.48). Not composed at all, whether minimized, entirely off screen, or covered by a
maximized window: state 1 (Clear about 1.25-1.5, Sky3D about 0.08, World about 0.32). The split is 33 of 33 runs with
no exception, and the decisive interleaved manipulation is 4 of 4 S against 6 of 6 X and 4 of 4 M.

- **H-P, partly confirmed.** The DWM presentation path selects the state, but through visibility, not foreground.
  My guess "foreground reads state 1" is falsified: a visible foreground window read state 2 (09-F, 12-F).
- **Base-rate prediction falsified.** I predicted both states among the 8 plain runs; they read 8 of 8 state 2,
  because T3 Code, the window in front all afternoon, never fully covered the viewer.
- **The archive's flips follow the same rule, by inference not observation.** A plain agent-launched run lands in
  whichever state the owner's windows put it: fully hidden behind a maximized app means state 1, partly visible
  means state 2.
- **H-G falsified.** Both states ran at the same P0, 1837/7501 MHz.
- **H-K inconclusive by its rule:** no 300 s cold run. Idle times of 12-238 s read both states, and visibility
  explains every run.
- **H-W:** position matters only by making the window visible or not (X against S).
- **H-Q not tested.** No mode 0 runs.

**Q2, falsified: the state does not move the frame mean, the render thread or the main-thread phases.** Matched pair,
same session: frame 6.73 (6.63-6.85) in state 2 against 6.69 (6.57-6.82) in state 1. The ranges overlap and the
means differ by 0.04 ms, under the 0.22-0.25 ms within-state range. Render thread, camera, advance and hud overlap.
`between` reads 0.19-0.20 against 0.17-0.18, a 0.02 ms difference (0.3% of the frame) that does not
overlap. I attribute it to the window condition (a TopMost on-screen strip against an off-screen window), but that is
untested: this record did not separate the two. The 10-27 lead (state 2 about 0.8 ms slower) did not reproduce. In the
archive the state-2 penalties of constant-ring and static-prop-handle come with a rise in OpaqueModels. The one that
does not is static-prop-lighting 04-A (11.03, Opaque 0.883), a single run 2.4 ms above its build's others.

**What changes:** a per-pass GPU figure for Clear, Sky3D or World is comparable only within one visibility state. A
measurement that reads those passes should fix the window's visibility, by placing it off screen
(`TF2VIEW_WINDOW_POS=-3000,100`) or keeping part of it on screen, rather than letting the owner's windows choose. The
frame mean needs no such control. Why a composed window moves GPU time out of Clear into Sky3D and World, and adds
about 6 points of GPU utilization at the same frame, is not measured here: plausibly DWM's composition work being
time-sliced into the viewer's timestamps, an interpolation.
