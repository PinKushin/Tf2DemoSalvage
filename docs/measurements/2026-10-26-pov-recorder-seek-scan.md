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
dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- recorder-seek-scan D:/measure-f12/f12.dem 40
```

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

## Result

## Verdict
