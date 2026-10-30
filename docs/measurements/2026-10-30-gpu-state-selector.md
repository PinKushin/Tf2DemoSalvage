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

(not yet run)

## Verdict

(not yet run)
