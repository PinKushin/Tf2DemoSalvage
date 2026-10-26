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

**Prediction confirmed**: a seek's recorder probes cost 14.05 ms mean and up to 85.85 ms, one 2048-probe sample at the cap
102.4 ms, all past the 2 ms threshold, and fast playback adds about 1 ms a frame. The cap figure is one sample with 26% of its
probes predicting (523 of 2048), so it is not the worst case; and every figure is a lower bound (hooks and ground surfaces off). **The cache the brief proposed cannot be bit-identical and would barely
pay**: every prediction restores from its own packet's networked state (`Restore(recorder, …, packet.Tick)`), so a state
carried from packet N into N+1 differs from the per-probe route; and reuse within one packet is bounded by 1.19 probe ticks
a packet — at most about 16% of the probes. Not built; reported to the lead with these numbers. What a bit-identical saving
would have to target instead is the per-probe setup (`PlayersAt` of every player at the packet, a second `PlayersAt` in
`StunFadeUnknown`, `MovementWorld.At`) — not yet split, so not claimed.

## Follow-up plan: the owner's hybrid (B602) and the bounded rewind — recorded before the runs

**Owner decisions, 2026-10-26** (relayed by the lead): (1) the seek scan answers the viewed recorder by the cheap route by
default and runs prediction only where the cheap speed is close enough to an activity threshold that its error could flip
the activity, the margin justified by measurement; (2) `DemoPlayer.Restart`'s start-point loop is bounded by the packet index.

**What the engine reads.** Speed reaches the main activity at one threshold: `MOVING_MINIMUM_SPEED` 0.5
(`multiplayer_animstate.cpp:33`), `>` in `HandleMoving` (`:938`; TF `tf_playeranimstate.cpp:1302`, `:1314`) and `<` in
`HandleDucking` (`tf_playeranimstate.cpp:1351`). TF2 has no walk (`multiplayer_animstate.cpp:935`). So the gate is one band,
`|cheap − 0.5| ≤ margin` (`RecorderActivityGate`).

**Which cheap route.** The brief names the track route (0.49 ms mean a seek above). The networked `m_vecVelocity` costs the
same, is already the exact route's own answer wherever prediction declines (31.6% of probes above), and is not lagged by
the interpolation window as a differenced track is. Both are measured; the one needing the smaller margin is used, and if
that is the networked one it is reported as a deviation from the brief's word.

**Probe `recorder-speed-gate`** (`RecorderGate` over the production wiring, as `recorder-seek-scan`):

- *Fit*: the exact route's recorder probes on 300 landings placed as `seek-scan` places them (`recorder-speed-gate <demo> 300`). Per viewed, living probe: |exact − networked| and
  |exact − track| — mean, median, p99, max — and, the number that sets the margin, the largest |cheap − 0.5| among probes
  where cheap and exact fall on different sides of 0.5.
- *Margin*: that largest value, times a headroom factor of at least 4 and at least 10 u/s; written into
  `RecorderActivityGate.Margin` with this record cited.
- *Test*, on 300 landings disjoint from the fit (half a stride on): each route's recorder-probe ms a seek (mean, max), probe
  count, and how many landings chose a different recorder main sequence than the exact route; and per probe, how many of
  the hybrid's answers are on another side of 0.5 than exact. **Prediction: 0 mismatches for the hybrid**; the networked
  and track routes alone > 0 somewhere (the control that the comparison can fail); hybrid ms between cheap and exact.
- *Sabotage*: margin 0 in the fit's sweep must report > 0 probes decided wrong.

**Probe `demo-player-rewind`**: one `DemoPlayer` played to the end, then asked 40 earlier landings; ms of each rewind and
commands re-read (`DemoPlayer.CommandsRead`, carried). Run on the commit before the bound and on the bound. Control: each
rewound answer equals a fresh player's. Prediction: before, up to ~118k commands (from the start to the landing); after, ≤ 50.

**Conditions**: `-c Release`, `run-exclusive.ps1 -Shared`, only after the lead's "go", with three CPU samples under 25%.

## Follow-up result

**Environment:** the owner's desktop, 2026-10-26, after the lead's "go"; CPU samples before each run 3-17%, no testhost,
tf2demoview or hl2. Branch `fix/batch-31-pov-seek-hybrid`, `-c Release`, `run-exclusive.ps1 -Shared`.

### The rewind

`demo-player-rewind <lcor>/etf2l-12025-pov-2020-07-21.dem 40`, before the bound (`378a7d54`) and after it:

```
before: rewind: first 2.018 ms, then mean 6.978 ms, max 44.778 ms; commands re-read mean 57714.9, max 115338
        control: 0 of 40 landings answer differently from a fresh player
after:  rewind: first 2.036 ms, then mean 0.016 ms, max 0.042 ms; commands re-read mean 40.0, max 43
        control: 0 of 40 landings answer differently from a fresh player
```

**Prediction confirmed**: up to ~118k re-read before (115,338), ≤ 50 after (43).

**Corrected 2026-10-26 (batch-33 review).** The control above compares views only, and the bound did not land in a fresh
read's parse-ahead list on four synthetic shapes: a gap wider than 40 ticks (the start was counted from the target, not
from the last packet read — views differed too), a backward tick run with or without a synctick before it, and signons with
no synctick after them. The five gcor POV specimens and z1800 keep the bound too (12-14 commands a rewind, 40.9 on the 2013
specimen; 0 of 40 differ in each). The start is
now counted from the last packet before the stop, and a read command stepping back in tick before the stop restarts from the
file's start (`DemoPlayer.Restart`; held by `InterpolateViewpoint_RewoundToManyTargets_QueuesWhatAFreshReadQueues`, which
compares the queue as well). Re-measured on the same demo (Release, `-Shared`, quiet desktop not checked):

```
rewind: first 3.769 ms, then mean 0.026 ms, max 0.062 ms; commands re-read: first 8, then mean 40.9, max 43
control: 0 of 40 landings answer differently from a fresh player
```

etf2l-12025 has no backward step, so it keeps the bound.

**Superseded the same day by D234 (batch-33 review 2): the bound applies only to a "simple" file**, decided once at load
(`DemoPlayer.IsSimple`); anything else re-reads from the start. The probe now compares the whole rewound state (view,
parse-ahead list, `m_bInterpolateView`) and names the file's shape. Sweep of every POV and SourceTV demo in gcor (the main
checkout's 10) and lcor (`F:\Tf2DemoSalvage-lcor`, 773), `demo-player-rewind <demo> 40`, Release, `-Shared`, four probe
processes at once (so the milliseconds carry CPU contention; the re-read counts do not):

- **First cut** (opening signons required to be ticked past every packet), stopped after 25 demos: every gcor era
  specimen and 4 of 15 lcor demos came out not simple — their opening signons sit inside the packets' span (158 on
  `tf2-2007-build3258-pov-cp_granary`, 6,041 on `tf2-2013-build1729296-stv-cp_foundry`). The proof does not need that
  condition; it was dropped (commit `0f32129e`).
- **Interim, 75 of 783 demos** (all 10 gcor, 65 lcor) on the corrected rule: **75 simple, 0 not simple**; rewind mean
  0.018 ms, largest single rewind 0.506 ms; 31.8 commands re-read a rewind on average; **0 of 3,000 landings** differ from a
  fresh player; no probe exited non-zero. *The sweep was still running when this was written* (one log per demo under the
  session scratchpad, `rewind-summary.ps1` totals them); the non-simple path's cost on a real file is therefore
  unmeasured here — its ceiling is the unbounded figure above, 44.8 ms on etf2l-12025.

### The hybrid

`recorder-speed-gate <lcor>/etf2l-12025-pov-2020-07-21.dem 300`, verbatim (the probe's hybrid is emulated in the probe, not
shipped; its margin is the plan's rule, the fit's farthest flip × 4):

```
etf2l-12025-pov-2020-07-21.dem: recorder 6; fit 300 landings, 21794 recorder probes, 19601 viewed and alive; item hooks wired, ground surfaces wired
  networked: |exact - cheap| mean 23.563, median 0, p99 239.999, max 1023.111 u/s; 1392 probes on the other side of 0.5, the farthest |cheap - 0.5| among them 1022.613 u/s
    flips' needed margin: median 215.502, p90 239.5, p99 287.5; over 10: 1308, over 50: 1213, over 300: 10; exact still and cheap moving: 1339, the reverse: 53
    against the next packet's networked speed: 36 flips side with exact, 1356 with networked
    the frame's drawn speed there: 1392 flips side with exact, 0 with networked, 0 not drawn alive
    worst five (tick: exact, networked, track): 77518: 0, 1023.11, 969.36; 117811: 0, 826.15, 702.22; 117810: 0, 826.15, 810.66; 119405: 0, 721.92, 819.36; 47436: 0, 421.91, 916.85
  track: |exact - cheap| mean 91.024, median 13.741, p99 444.164, max 80003.2 u/s; 1665 probes on the other side of 0.5, the farthest |cheap - 0.5| among them 80002.7 u/s
    flips' needed margin: median 177.623, p90 239.5, p99 916.353; over 10: 1471, over 50: 1322, over 300: 32; exact still and cheap moving: 1525, the reverse: 140
    against the next packet's networked speed: 347 flips side with exact, 1318 with track
    the frame's drawn speed there: 1665 flips side with exact, 0 with track, 0 not drawn alive
    worst five (tick: exact, networked, track): 42978: 0, 0, 80003.2; 42979: 0, 0, 77959.43; 42977: 0, 0, 67463.16; 42980: 0, 0, 65486.14; 42976: 0, 0, 52692.64
  margin 0: 1392 fit probes decided wrong, 0 of 19601 (0.0%) predict
  margin 10: 1308 fit probes decided wrong, 2448 of 19601 (12.5%) predict
  margin 50: 1213 fit probes decided wrong, 2830 of 19601 (14.4%) predict
  margin 100: 1071 fit probes decided wrong, 3399 of 19601 (17.3%) predict
  margin 300: 10 fit probes decided wrong, 18116 of 19601 (92.4%) predict
  margin 1022.613: 0 fit probes decided wrong, 19577 of 19601 (99.9%) predict
  margin 4090.451: 0 fit probes decided wrong, 19601 of 19601 (100.0%) predict
  test half: the hybrid emulated at margin 4090.451 u/s
  test Exact: recorder probes per seek mean 9.865 ms, max 102.685 ms; 26536 probes; 0 of 300 landings chose another sequence than Exact
  test Networked: recorder probes per seek mean 0.414 ms, max 4.809 ms; 44118 probes; 0 of 300 landings chose another sequence than Exact
  test Track: recorder probes per seek mean 0.397 ms, max 5.463 ms; 42324 probes; 0 of 300 landings chose another sequence than Exact
  test Hybrid: recorder probes per seek mean 10.197 ms, max 104.933 ms; 26536 probes; 0 of 300 landings chose another sequence than Exact
  test Hybrid per probe: 0 of 21232 viewed, living probes on another side of 0.5 than Exact
```

And the test half at margin 300 (`… 300 300`): hybrid 8.852 ms mean, 103.231 max, **13 of 21,281 probes on another side**.

**Verdict — the plan's prediction is falsified.** Zero mismatches holds only at a margin (≥ 1,022.6 u/s, 4,090 with the
plan's headroom) where the hybrid predicts on every probe and costs exact's 9.9-10.2 ms; a margin that saves anything
decides wrongly (300 u/s: 92.4% still predict, 10 fit and 13 test probes wrong). **Not shipped**; production is unchanged.

**What the disagreements are** — the finding worth more than the gate: 1,339 of the 1,392 networked flips are prediction
answering 0 u/s where the networked speed says a running speed (median 215.5, p90 239.5); the next packet's networked speed
— the server's own answer — sides with the networked speed in 1,356 of 1,392; and the frame draws prediction's 0 in all
1,392. So the viewer draws the running recorder standing on ~7% of probed ticks (1,392 of 19,601), and the scan's reference
is wrong there (RISKS B604). Until that is fixed, any margin is fitted to a defect.

**The track route is ruled out outright**: median error 13.7 u/s against the networked route's 0, and 80,003 u/s through a
teleport. The networked velocity is the right cheap half if this is revisited.

**Instrument notes.** The per-landing comparison (recorder main sequence) read 0 of 300 for every route, the track one
included, so it is no control; the per-probe side count and the margin-0 sweep (1,392 wrong) are. Wiring the prediction's
item hooks and ground surfaces (`wired`, printed) left every figure identical to print precision with the unwired run: the
control line shows the inputs exist, not that prediction read them, so the 2026-10-26 "lower bound" caveat is not resolved.
The `exact` route is production's `ActivityPlayerAt` recomposed from the timeline's public pieces in `RecorderGate`.

## B604's cause, and the counts before and after its fix

**Environment:** the owner's desktop, 2026-10-26, branch `fix/b604-pov-prediction-speed`, `-c Release`, `run-exclusive.ps1
-Shared`. **Counts only:** the machine was not quiet (CPU 44-91%, other agents), so no millisecond here is a result.

**Instrument:** `recorder-prediction-gaps <demo> [print]`. From every packet, it re-runs the commands the next packet
acknowledges, selected by sequence because this demo's acknowledgement lags its reads, through `PredictFrom`, the
production route, wired. It compares the result with that next packet's networked velocity, which is the server's answer.
Zeroed gaps are tagged, with the agreeing gaps' rates beside them as the baseline. Controls printed: gaps alive at both ends
(95,713 of 118,282 packets, 83,267 compared, 0 declined), dormant living enemies seen (100, so the reading works), and
whether the installed map is the recorded build (**False**: `cp_process_final`'s MapHash differs).

**Before** (`6fee5c39`): within 1 u/s 61,014 of 83,267 (73.3%), mean |Δv| 25.718 u/s; still against the server over
100 u/s: **7,723**. Of those, 98.4% are on the ground (66.2% of agreeing gaps) and 89.2% have the hull clear at the restored
origin (99.3%). A temporary, uncommitted instrument on `TryPlayerMove`'s zeroing sites put them at the stuck re-trace after a
clear sweep (43%), all-solid (25%) and `allFraction == 0` (21%). A clear sweep that ends embedded points at the sweep. With
the entry clamp alone, as a manipulation before any test: 2,369. With startsolid-but-gets-out no longer zeroing the fraction,
on top: 2,347, so that part was not the cause, and it was reverted.

**After** (the fix, `fac73fdd`): within 1 u/s 65,636 (78.9%), mean |Δv| 10.492 u/s; still against running **2,376**. Of
those, 23.3% start inside world geometry at the restored origin (agreeing gaps: 10). Of the stuck ones, 376 clear only when
raised 18 units and 77 when raised 4. 11.7% have a static prop one step on (agreeing: 0.7%). Most likely the 2020 map build,
which is not installed; *interpolated, not fetched*.

**`recorder-speed-gate <demo> 300` with the per-probe dump, paired on the 18,076 probe ticks both runs asked:**

| | before | after |
|---|---:|---:|
| flips against the networked speed | 1,372 | 656 (716 fixed, **0 new**) |
| \|exact − networked\| < 1 u/s | 72.4% | 74.4% |
| right before, moved > 1 u/s after | — | 8 (none changed side) |
| the run's own total flips / viewed living probes | 1,392 / 19,601 (7.1%) | 724 / 33,368 (2.2%) |
| margin with 0 fit probes wrong | 1,022.6 u/s (99.9% predict) | 825.6 u/s (98.7% predict) |

**The control on a demo whose map matches** is `rgl-pug-2026-08-10-pov.dem` (lcor), on `cp_process_f12`, where the probe
prints that the installed map matches the recorded MapHash. Of 94,321 gaps compared, still against running went from
**3,194 to 1,000**; within 1 u/s went from 75.2% to 77.1%; mean |Δv| went from 13.879 to 7.503 u/s. **None** of the 1,000
starts inside world brushes, which supports blaming etf2l-12025's 23% on its map build. What is left there has a different
shape. In 48.1% (1 of 72,686 agreeing), an unswept hull at the restored origin answers a fraction under 1 *without*
`startsolid`, which is how `MapLevel.Trace` reports a static prop or terrain hit, since it drops the flag. In 51.3%, a static
prop holds the hull one step on (agreeing: 0.1%). That points at static prop collision, or at the flag `MapLevel.Trace`
drops, as the next cause. Not hunted.

### Source's clip

**Environment:** the owner's desktop, 2026-10-26, branch `fix/source-brush-clip` from `30790a30`, `-c Release`,
`run-exclusive.ps1` (exclusive; the lead's gate had finished). Counts only. Both probes were run at the brush-clip commit
(`5cd81704`) and at the box-sweep commit on top of it (`6384277d`, unchanged by the test commit after it).

| etf2l-12025 | `30790a30` (above) | `5cd81704` brush clip | `6384277d` + box sweep |
|---|---:|---:|---:|
| `recorder-prediction-gaps`: still against the server over 100 u/s | 2,376 | 2,369 | 2,369 |
| within 1 u/s, of 83,267 | 65,636 (78.9%) | 65,661 (78.9%) | 65,661 (78.9%) |
| mean \|Δv\| xy | 10.492 | 10.492 | 10.492 |
| `recorder-speed-gate … 300`: flips / viewed living probes | 724 / 33,368 (2.2%) | 721 / 34,040 (2.1%) | 721 / 34,040 (2.1%) |
| flips siding with the next packet's networked speed | 675 | 672 | 672 |

**Every change is the brush clip's; the box sweep moved nothing here.** Starting inside a building or an enemy is rare on this
demo ("origin start-in building": 8 of the 2,369 at `6384277d`). The scan grew by 672 viewed probes, and with it the exact
route's test-half probes to 41,804 (22.5 ms a seek, 180.9 ms at most — one run, and another agent's build shared the
machine for part of it, so not a result). The remainder is
unchanged in shape: 23.4% start inside world geometry at the restored origin, the map-build question above.

There are more probes after the fix. The scan goes back to the recorder's last activity change, and wrongly zeroed ticks
no longer break up a run. **This is a cost consequence:** on the same landings, the exact route's test-half probes went from
26,536 to 41,236 (+55%) and the fit's from 21,794 to 35,561. The per-seek milliseconds above (14.05 / 85.85) are therefore
stale and probably low. They need a re-run on a quiet machine. **The cheap route still does not match** (724 flips, 675 of them siding with the next packet over
exact), so the scan was not switched to it. The residual is the map question above.
