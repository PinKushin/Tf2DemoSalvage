# B729: what placing move children after every entity's state costs a frame

## Observation

B729's fix splits the pose pass's simulate: a move child's placement (`PlacementOf`) is no longer composed inside
`SimulateEach` but in a loop after it (`PlaceMoveChild`), with one `HashSet<int>` add and remove per simulated move child
and a set lookup on each in-pass attachment query while one is pending. It touches the `simulate` phase of every frame.

## Question

On f12 at the standard measurement moment, what is the frame's mean cost and the pose's `simulate` phase before and after
the change, in milliseconds and as a share of the frame?

## Method

The tf2-measure command, Debug builds, interleaved before, after, before, after:

```
TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 <exe> D:/measure-f12/f12.dem --tick 5970 --first-person --measure 20 +fps_max 0
```

Before: `origin/main` built in a detached worktree at `F:/wt-b728-before`. After: branch `fix/child-parent-pose-order`
with this change. The frame mean is each run's own summary line; `simulate` is read from the `moment cost` lines, weighted
by each window's frames.

## Control

Each run must print the `moment cost` line with `simulate` non-zero (the phase ran) and end about 66.7 × 20 ticks past
5970 (the playback window was measured, not loading).

## Environment

The owner's machine, hardware Direct3D 11, the viewer's default window, Debug builds. Before: `f372bd01f` (`origin/main`)
in `F:/wt-b728-before`; after: `c865e6fe6`. Runs at 19:23, 19:41, 19:51 and 19:59. Other agents' UI suites and a shared
gate held the machine-wide lock before three of the runs; each run waited for them under `run-exclusive.ps1` and ran
alone. CPU load was not sampled.

## Result

Raw output: the session scratchpad's task outputs `bi18b6qo6` (before 1), `b6lku2da7` (after 1), `bxfgpp6ef`
(before 2), `bo2d25zs4` (after 2). The frame mean is total frames over total seconds, each window's seconds being its
frames over its printed fps (`parse.sh` in the session scratchpad); `simulate` is the mean of the 18 windows' `moment
cost` values, which are printed to 0.1 ms.

| run | windows | frames | seconds | mean frame ms | simulate ms | last tick |
|---|---|---|---|---|---|---|
| before 1 | 18 | 2,676 | 17.85 | 6.672 | 0.517 | 7243 |
| after 1 | 18 | 2,741 | 17.71 | 6.462 | 0.500 | 7249 |
| before 2 | 18 | 2,584 | 18.05 | 6.987 | 0.522 | 7241 |
| after 2 | 18 | 2,779 | 17.40 | 6.260 | 0.494 | 7247 |

Control: every run printed `simulate` non-zero in all 18 windows, and ended 1,271 to 1,279 ticks past 5970 over 20 s
(about 64 a second).

## Verdict

No cost the instrument can see. `simulate` reads 0.52 ms before and 0.50 after (means of two each), a 0.02 ms difference
below the 0.1 ms the phase is printed to, so it is not attributed to the change in either direction; the frame means
(6.83 before, 6.36 after) differ by less than the spread between two runs of the same build (0.32 ms before, 0.20 after).
What the change adds is one `HashSet<int>` add and remove per simulated move child and a `Count` test per in-pass
attachment query — arithmetic, not measured, a few microseconds a frame at most.
