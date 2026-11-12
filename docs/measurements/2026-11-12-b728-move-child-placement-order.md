# B728: what placing move children after every entity's state costs a frame

## Observation

B728's fix splits the pose pass's simulate: a move child's placement (`PlacementOf`) is no longer composed inside
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

## Result

## Verdict
