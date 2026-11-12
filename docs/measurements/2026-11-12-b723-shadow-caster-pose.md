# B723: what keying the shadow on the posed cycle, and posing undrawn casters for their cells, costs a frame

## Observation

B723's fix (review, 2026-11-12) adds work to every frame's shadow step: the gather reads each prop's simulated cycle, a
client-animated caster's cell is now redrawn whenever its cycle moves (where before it was drawn once), and a redrawn
caster that no view drew is posed by a second `Instances` pass (`EntityModelSet.ShadowPass`). The reviewer asked what that costs.

## Question

On f12 at the standard measurement moment, what is the frame's mean cost and the shadow step's cost (`camera particles/shadows`,
and the new `camera particles/shadows/pose`) before and after the change, in milliseconds and as a share of the frame?

## Method

The tf2-measure command, Debug builds, interleaved before, after, before, after:

```
TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 <exe> D:/measure-f12/f12.dem --tick 5970 --first-person --measure 20 +fps_max 0
```

Before: `origin/main` (`e1cedb49`) built in a detached worktree at `F:/wt-b723-before`. After: branch
`fix/b723-rtt-shadow-pose` with this change. Each run's stdout is kept whole in the session scratchpad. The per-window
`frame rate … ms` lines are averaged weighted by their frame counts; the shadow parts likewise.

## Control

Each run must print `camera particles/shadows` with its gather, casters, receivers and decals parts non-zero (shadows on, the
step ran), and the after runs must print the `camera particles/shadows/pose` part (the new timer ran); a run without them did
not measure the step. Ticks advanced must be about 66.7 × the measured seconds.

## Environment

The owner's machine, hardware Direct3D 11, the viewer's default window (984x551 drawn). Debug builds. Before: `e1cedb49`
(`origin/main`) in `F:/wt-b723-before`; after: `209653a3`. CPU load 23% before the first run (05:49 — another agent's
gate was queued and ran between runs; each run waited for it under `run-exclusive.ps1`), 16% after the last. Runs at 06:16,
06:21, 06:25 and 06:41. An earlier before run at about 05:35 was taken before this file existed and is not quoted.

## Result

Raw output: the session scratchpad's task outputs `bqu1738g7` (before 1), `b30unzd18` (after 1), `bk339my9t` (before 2),
`b5tqoca1r` (after 2). The frame mean is the run's own summary line (`measured 20 seconds … mean`); the shadow step and its
pose part are the per-window `parts:` values weighted by each window's frames (`parse.ps1`); the `Shadows` render pass is the
summary's.

| run | frames | frame mean ms | shadow step ms | of which pose ms | `Shadows` pass cpu / gpu ms | cell draws |
|---|---|---|---|---|---|---|
| before 1 | 2,910 | 6.87 | 0.510 | — | 0.147 / 0.033 | 32.4 |
| after 1 | 3,079 | 6.50 | 0.732 | 0.244 | 0.161 / 0.043 | 50.8 |
| before 2 | 3,246 | 6.16 | 0.478 | — | 0.129 / 0.032 | 32.1 |
| after 2 | 3,180 | 6.29 | 0.718 | 0.238 | 0.153 / 0.043 | 51.5 |

Control: every run printed the gather, casters, receivers and decals parts; both after runs printed the pose part in all 18
windows; every run ended at tick 7310, 1,340 ticks over 20 s (67 a second).

## Verdict

The change costs about 0.23 ms a frame on the game thread (shadow step 0.494 → 0.725 ms, mean of two each), all of it the new
pose pass (0.241 ms); the posed-cycle key itself costs nothing measurable. The render thread's `Shadows` pass draws about 19
more cells a frame (+0.019 ms CPU, +0.010 ms GPU). Together about 0.26 ms, 4% of a 6.3 ms frame; the frame means (6.52
before, 6.40 after) move by less than their run-to-run spread (0.7 ms), so the frame total cannot see it. The pose pass is a
second full `Instances` over the undrawn casters, and is the cost to reduce.
