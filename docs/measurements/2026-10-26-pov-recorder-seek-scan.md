# Measurement 2026-10-26: what the seek scan costs for a point-of-view demo's recorder (B584, batch-31 review 2)

## Observation

Fable's second review of batch 31 (read, not measured): since batch-31 review 7, `TimelineMoments.ActivityPlayerAt` answers
the viewed recorder through `RecorderPrediction.VelocityThrough`, a full `PredictFrom` per probe — `PlayersAt(packet.Tick)`,
`MovementWorld.At`, `ServerConVarsAt`, `RulesAt` twice, `Restore`, and every pending command re-run. `EntityModelSet.Scan`
probes up to `ActivityScanTicks` (2048) ticks per seek, and `HoldsAcross` scans the skipped ticks of every forward gap past
`ContinuityTicks`. Its own remark says "One movement prediction per recorder probe; not measured". The earlier seek-scan
record (`2026-10-25-d233-activity-tuples-cost.md`) set `EarlierPlayer = DemoTimeline.PlayerAt`, so it never ran prediction.

## Question

On a POV demo, how many milliseconds of a seek's scan are the recorder's probes through the production route, mean and worst
case; what does a fast-playback frame's `HoldsAcross` add; and do the counts (scanned ticks per packet, pending commands per
probe) leave room for a per-packet prediction cache that stays bit-identical?

## Method

A probe, `recorder-seek-scan`, over the production wiring: `TimelineMoments` built as `DemoSystems.Open` builds it
(`Player = new DemoPlayer(timeline)`, `Prediction = new RecorderPrediction(timeline, () => map.Level)`), and
`MomentScene.EarlierPlayer = moments.ActivityPlayerAt` as `MomentPresenter` sets it. The frame's players come from
`moments.PlayersAt`, as the viewer's do. **Deviation:** the prediction's `Hooks` (item attributes) and `GroundSurface` are
left null — both come from the viewer's install wiring; with them each prediction can only cost more, so the figures are a
lower bound.

- **Timed at the call**: the `EarlierPlayer` delegate is wrapped, and a Stopwatch runs around each call whose entity is the
  recorder; the sum per seek is that seek's recorder cost. Nothing is replayed to get it (B243).
- **40 landings** spread over the demo, as `seek-scan` places them, then the held frame; first landing reported apart (JIT).
- **Worst case, measured**: 2048 consecutive `ActivityPlayerAt` calls for the recorder backwards from a mid-demo tick where he
  is alive and viewed — the scan's cap.
- **Fast playback**: 300 frames 12 ticks apart (past `ContinuityTicks`), timed per frame and the recorder part.
- **Counts**: recorder probes; distinct `LastPacket`s among them (scanned ticks per packet); mean pending commands per probe
  (`RecorderPrediction.Pending`).

Command (`-c Release`, through `run-exclusive.ps1 -Shared`, after `Get-CimInstance Win32_Process` shows no other CPU-heavy
work):

```
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- recorder-seek-scan <lcor>/etf2l-12025-pov-2020-07-21.dem 40
```

**The demo is etf2l-12025 (a recorder-POV ETF2L match, 10 players), not f12.** The first exploratory run on
`D:/measure-f12/f12.dem` tripped the control: recorder entity 1, but a fresh `DemoPlayer.InterpolateViewpoint` at mid-demo
is null — its packets carry no recorder view, so the viewed route never runs there. The second exploratory run, on
etf2l-12025, tripped it differently: `InterpolateViewpoint` gave a view where `ViewsAt` said none. That was this branch's
fix 2 counting `dem_signon`s, whose ticks there are 146,124-146,256 against packets from 0, in its "first packet past the
tick" index — `ViewsAt` false everywhere and the frame restarting on every read. Fixed before this run (the index counts
only `dem_packet`, which is all that waits for its tick), with a synthetic regression test.

## Control

- The demo's `RecorderEntityIndex` is printed and must be non-null (a SourceTV demo has no recorder and measures nothing).
- The fraction of recorder probes on which `DemoPlayer.ViewsAt` is true (the viewed route) is printed.
- How many recorder probe ticks give a non-null `VelocityThrough` is printed: if nearly all are null, the early exits (no
  level, no pending commands, dead) were measured, not prediction.
- The same probe count with the recorder's calls answered by `DemoTimeline.PlayerAt` instead (the pre-review-7 route) is
  timed beside it, so the prediction's share is a difference of two observed runs.

## Prediction

Each prediction costs 0.05-0.5 ms (a `PlayersAt` of every player plus a few traced moves), so a full 2048-tick recorder scan
costs 100 ms-1 s, and a typical seek, which scans back only to the recorder's last activity change, 5-50 ms — well past the
2 ms threshold. Falsified if the worst case is under 4 ms (2 µs a probe) or the mean seek's recorder part under 2 ms.

## Environment

The owner's Windows 11 desktop, 2026-10-26. `Get-CimInstance Win32_Process` before the run: no dotnet, testhost, msbuild,
tf2demoview or hl2 process; CPU load 12% before, 11% after. Branch `fix/batch-31-review-2` at `b9577586` (fix 2's signon
regression already fixed), `-c Release`, through `run-exclusive.ps1 -Shared`. One run.

## Result

The probe's output, verbatim except the control line's `ScenePlayer` record dump, which is elided (it showed the recorder
dead at 60313, `LifeState = 2`, which is why he is not drawn there):

```
control at 60313: recorder drawn False, in the players True ([record elided]), drawn props 8 of 10 players, InterpolateViewpoint a view, ViewsAt True, view commands 118289, packets among them 118287
  Signon: 5, ticks 146124-146256, IsPacket True, default views 5, first index 0
  SyncTick: 1, ticks 0-0, IsPacket False, default views 1, first index 5
  Packet: 118282, ticks 0-120626, IsPacket True, default views 0, first index 6
  Stop: 1, ticks 120626-120626, IsPacket False, default views 1, first index 118288
etf2l-12025-pov-2020-07-21.dem: recorder entity 6, 40 seeks, ticks 0-120626
predicted route: seek frame first 23.59 ms, then mean 18.61 ms, max 94.79 ms; its recorder probes first 0.00 ms, then mean 14.05 ms, max 85.85 ms
recorder probes per seek: mean 120.0, max 1662, total 4681
control: 4681 recorder probe ticks, 4681 viewed (100.0%), 3204 with a predicted velocity (68.4%)
counts: 3948 distinct packets (1.19 probe ticks a packet), 4.20 pending commands a probe; 118282 packets over 120626 ticks
track route (control): seek frame first 1.42 ms, then mean 3.90 ms, max 13.32 ms; its recorder probes first 0.00 ms, then mean 0.49 ms, max 2.13 ms; 8142 recorder probes
worst case: 2048 recorder probes back from 60479: 102.4 ms (50.0 us a probe), 2048 answered, 523 with a predicted velocity
fast playback, 12 ticks a frame: frame first 1.67 ms, then mean 1.31 ms, max 5.84 ms; its recorder probes first 1.39 ms, then mean 1.03 ms, max 3.03 ms
```

| | predicted route | track route (control) |
|---|---:|---:|
| seek frame, mean of 39 / max | 18.61 / 94.79 ms | 3.90 / 13.32 ms |
| its recorder probes, mean / max | 14.05 / 85.85 ms | 0.49 / 2.13 ms |
| recorder probes | 4,681 | 8,142 |
| 2048-probe cap, back from 60479 | 102.4 ms (50.0 µs a probe; 523 of 2048 predicted a velocity) | — |
| fast playback, 12 ticks a frame, recorder part mean / max | 1.03 / 3.03 ms | — |

The control held: every recorder probe was on the viewed route (100%), and 68.4% ran a full prediction (the rest exit early:
no pending command, or dead). The track route asks more probes (8,142 against 4,681) because its differenced-position speed
breaks the activity at other ticks; it is the route review 7 replaced, timed only as the baseline.

## Verdict

**Prediction confirmed**: a seek's recorder probes cost 14.05 ms mean and up to 85.85 ms, the cap 102.4 ms, all past the 2 ms
threshold, and fast playback adds about 1 ms a frame. **The cache the brief proposed cannot be bit-identical and would barely
pay**: every prediction restores from its own packet's networked state (`Restore(recorder, …, packet.Tick)`), so a state
carried from packet N into N+1 differs from the per-probe route; and reuse within one packet is bounded by 1.19 probe ticks
a packet — at most about 16% of the probes. Not built; reported to the lead with these numbers. What a bit-identical saving
would have to target instead is the per-probe setup (`PlayersAt` of every player at the packet, a second `PlayersAt` in
`StunFadeUnknown`, `MovementWorld.At`) — not yet split, so not claimed.
