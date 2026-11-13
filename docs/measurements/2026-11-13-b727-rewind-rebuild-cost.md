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

Run first on `8b19b19c4` (the rebuild in the call), then — if the drag event is a visible stall and the rebuild is spread over
frames — the Debug command again on the commit that spreads it, which adds a second drag of 30 events with one drawn frame
(`StepSkip` then the window's pose) after each, and the frames the last event takes to land.

## Control

The ten played steps back must log no rebuild (`control pass` on each line), and each rewind must log a rebuild of
133 ticks. The played step's median is one show and pose, the baseline a pre-roll tick is compared to.

## Environment

The owner's machine, branch `fix/b727-owed`, no device (the probe), each run alone under `run-exclusive.ps1` (the Release run
waited for another agent's shared probe first). Before: `8b19b19c4`, Debug then Release. After: `ffb545f0f` plus the
probe's per-event print, Debug, twice (the second added the per-event series). CPU load was not sampled. The commit after
`ffb545f0f` changes only what a FORWARD move does during a rebuild (it lands it rather than rebuilding for the new tick);
every move this probe makes during a rebuild is a jump further back, whose path did not change, so it was not re-run.

## Result

Raw output: the session scratchpad's task output `bgc59lruy` (before, Debug) and the tool results of the other three runs.
Every run printed `control pass` on all ten played steps and a 133-tick rebuild on every rewind.

| run | rewind rebuild, 5 rewinds of 600 ticks | ms a pre-roll tick | first rewind | drag event in the call (30, 20 ticks apart) |
|---|---|---|---|---|
| before, Debug | median 230.9 ms (184.0-491.5) | 1.736 | 491.5 ms | median 94.8 ms, max 165.0, total 3,092 ms |
| before, Release | median 242.3 ms (180.6-457.6) | 1.822 | 457.6 ms | median 116.8 ms, max 363.9, total 3,850 ms |
| after 1, Debug | median 239.4 ms (177.0-472.6) | 1.800 | 472.6 ms | median 93.1 ms, max 158.8 |
| after 2, Debug | median 273.7 ms (201.7-591.3) | 2.058 | 591.3 ms | median 92.1 ms, max 173.9 |

The rewinds above still rebuild in the call (`SkipsAcrossFrames` false, as a `--shot` and the presenter tests run). The
drag as the viewer runs it after the fix, one drawn frame after each event:

| run | event and its frame | frames to land the last | landing frames | its rebuild |
|---|---|---|---|---|
| after 1, Debug | median 18.7 ms, max 438.1 | 16 | median 13.3 ms, max 17.5 | 133 ticks in 74.0 ms over 17 frames |
| after 2, Debug | median 18.4 ms, max 76.1 | 16 | median 12.8 ms, max 16.8 | 133 ticks in 68.0 ms over 17 frames |

After 2's series, in order: 19.1 18.9 35.7 18.8 15.9 17.8 17.3 18.4 18.1 54.7 73.2 14.2 17.2 16.9 14.0 15.8 13.5 18.5 18.4 20.7
15.9 18.8 18.8 75.6 67.0 59.8 76.1 62.3 17.6 15.2. The played step back (show and whole-view pose, the control) read a
median of 43.4-44.0 ms in every run: the probe poses with no frustum, so every entity, and it is not the viewer's frame.

## Verdict

A rewind's rebuild is a visible stall: 231-274 ms median (about 1.7-2.1 ms a pre-roll tick, Debug and Release alike), up to
590 ms for the first, and a backward drag paid 92-117 ms an event in the call. Spread over frames, 8 ticks a frame, an event
and its frame cost a median 18.4-18.7 ms and the last target lands 16 frames later at about 13 ms a frame. Spikes of 55-76 ms
on 6 of 30 events and one of 438 ms (run 1 only, not reproduced) are not attributed. The 133 ticks posed for the landing cost
68-74 ms when spread against 230-274 ms in the call; *interpolated:* the window's whole-view pose between batches rebuilds
the forgotten skeletons outside the batches' timer, so the batch figure is not the whole of the work.
