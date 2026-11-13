# B727: what a rewind's entity rebuild costs mid-match, and a backward scrub drag per event

## Observation

Since B727's follow-up a rewind (D246) forgets every entity's built-up state and poses the `RewindPreRollTicks` (133)
ticks before its target (`FramePresenter.RebuildEntityState`). It was never timed: the UI suite's only rewind goes to
tick 0, before the demo's first tick, and posed nothing ("-6 ticks before 0 in 0.4 ms"). `PlaybackPresenter.OnScrubbed`
counts a jump per scrub event, so a backward drag pays the pre-roll once per event.

## Question

On f12, mid-match, how many milliseconds does one rewind's rebuild take (the presenter's own `rewind rebuilt entities
from` line), what is that per pre-roll tick, and what does one event of a backward scrub drag cost? Is that a visible
stall (above a 16.7 ms frame) per drag event?

## Method

Probe `rewind-cost` (new), which wires the production `FramePresenter` → `MomentPresenter` → `TimelineMoments` →
`EntityModelSet` as `seek-pose` does, with the map's geometry, and no device (the pre-roll poses with
`ViewFrustum.Nothing` in the viewer too):

```
pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 dotnet run --project tools/Tf2DemoSalvage.Probe -c Debug -- rewind-cost D:/measure-f12/f12.dem 60000
pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- rewind-cost D:/measure-f12/f12.dem 60000
```

Opening jump to 60000, ten played steps back (no jump), five rewinds of 600 ticks each (59390 down to 56990), then a
drag of 30 events 20 ticks apart. Debug is what the owner runs (D176); Release is the probe's usual build.

## Control

The ten played steps back must log no rebuild (`control pass` on each line), and each rewind must log a rebuild of
133 ticks. The played step's median is one show and pose, the baseline a pre-roll tick is compared to.

## Environment

The owner's machine, branch `fix/b727-owed`.

## Result

## Verdict
