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

## Result

## Verdict
