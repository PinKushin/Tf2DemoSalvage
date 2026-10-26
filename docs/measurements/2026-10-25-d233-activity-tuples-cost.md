# Measurement 2026-10-25: what the engine's weighted pick costs (D233)

**Written after the run** (no prediction was fixed beforehand; the owner asked for the cost to be reported, not a
hypothesis tested). No viewer was timed: the render-thread work was being timed by another agent.

## Question

What do per-activity candidate lists cost in retained memory over a loaded model set, what does a pick cost against the
heaviest-only lookup it sits beside, and does the per-frame held path allocate?

## Method

`dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- activity-tuples D:/measure-f12/f12.dem 30000`, twice.
The probe takes the skinned models of the props at the tick plus the nine class models, through the production loader.

- **Retained bytes**: `GC.GetTotalMemory(true)` around building every model's map on a fresh merged table, minus the same
  around the fresh tables alone (the control), against a heaviest-only `Dictionary<string, int>` of the same names (what the
  B584 map held).
- **Per call**: Stopwatch over 2,000,000 calls each of `ForActivity` and `SelectWeightedSequence` for
  `ACT_MP_STAND_PRIMARY`, round-robin over the nine class models, Release.
- **Held path allocation**: `SelectWeightedSequenceConformanceTests.UpdateClientSideAnimations_HeldPath_AllocatesNothing`
  (`GC.GetAllocatedBytesForCurrentThread` around one whole `UpdateClientSideAnimations` pass on a held player).

## Result

Both runs identical in counts and bytes; timings within 0.3 ns.

| | value |
|---|---:|
| skinned models | 64 |
| sequences | 3,869 |
| sequences with an activity (candidates) | 1,369 |
| activities | 1,339 |
| activities with more than one candidate | 19 |
| retained, tuple maps | 195,928 B |
| retained, heaviest-only map of the same names | 55,784 B |
| `ForActivity` | 48.3-48.4 ns/call |
| `SelectWeightedSequence` | 50.1-50.4 ns/call |
| allocated over 4,000,000 calls | 40 B (the harness, not per call) |
| held path, one pass, one player | 0 B (was 88 B: the `declared` closure, now one delegate per model) |

**Reading.** The tuple lists cost about 140 KB more than the heaviest-only map across 64 models. A pick costs the same as the
lookup it replaces, and runs only when an activity changes; a held frame does the same lookups as before plus one dictionary
store. Only 19 activities on these models have more than one candidate, so the visible change is confined to those. A
third run listed them: the main-sequence ones are scout `ACT_MP_STAND_MELEE` x2 and soldier `ACT_MP_AIRWALK_PRIMARY` x5;
the rest are gestures (heavy and engineer `ACT_MP_GESTURE_FLINCH_CHEST` x4 and x3, attack, reload and PDA variants, a spy
voice gesture), engineer `ACT_MP_TAUNT` x7, sniper `ACT_KART_POSITIVE` x2 and `ACT_MP_CROUCH_DEPLOYED_IDLE_ITEM2` x2, and
soldier `ACT_MP_FALLING_STOMP_PRIMARY` x2. That run: 196,000 B retained (72 B more, run-to-run), 50.5 / 50.0 ns.

## Addendum, same day: what a seek's activity-start scan costs (`feat/seek-recovers-activity-start`)

**Written after the run**, no prediction fixed. `dotnet run --project tools/Tf2DemoSalvage.Probe -c Release -- seek-scan
D:/measure-f12/f12.dem 40`, through `run-exclusive.ps1 -Shared` (it waited for another agent's timed viewer runs, so no
viewer was running beside it). The probe builds the production `MomentScene` over the production `EntityModelSet`, sets
`EarlierPlayer = DemoTimeline.PlayerAt`, and counts the calls passing through the `PlayerPropAt` the scene installed. 40
landings spread over the demo, each more than `ContinuityTicks` from the last, so every drawn player's start is recovered;
then two held frames.

| | before sharing the motion pair | after (`430e3ca6`) |
|---|---:|---:|
| drawn players per landing | 6-12, mean 9.8 | same |
| seek frame, first (pays the JIT) | 39.3 ms | 41.2 ms |
| seek frame, mean of the other 39 | 13.4 ms | 9.1 ms |
| seek frame, max | 97.6 ms | 52.2 ms |
| earlier-tick probes | 68,378 (174.9 a player) | same |
| the probes replayed through the builder / `PlayerAt` alone | 369.9 / 360.1 ms | 205.4 / 195.7 ms |
| held frame after the seek | 0.011 ms, 1,112 B over 40 frames | same |
| the held frame after that | — | 0 B over 40 frames |

**Reading.** The scan is `PlayerAt`-bound (95%), so sharing the speed and heading's pair of track reads (five reads to two)
cut it by 46%. Per drawn player a seek costs about 0.93 ms mean; **for 24 players that is about 22 ms mean and up to about
105 ms** — *interpolated*, linearly from this demo's at most 12 drawn players, not measured. The 1,112 B is the first held
frame after a seek (once per landing, 28 B a frame on average); the next held frame allocates nothing. A frame that only
changes activity scans one or two ticks; the long scans are players standing still, or absent, back to the 2048-tick cap.

**Third run, after the review's fixes** (the build sets the clock before the sequence pass; a forward gap past four ticks
scans only the gap; a skip's `SimulatesOnly` frames scan nothing and leave the rolls for the landing to recover):

| | value |
|---|---:|
| a real forward skip, f12 26,579 → 79,735: 536 skip frames (every 99 ticks, B504) | 39.3 ms, 0 probes |
| its landing frame, 11 players | 6.54 ms, 1,217 probes |
| 40 landings as above: mean of 39 / max | 9.26 / 57.2 ms |
| speed through `PlayerAt` against the pre-`430e3ca6` formula, rebuilt from the public track, bit for bit | 37,657 compared, 0 differ |
| held frame after a landing / the one after that | 1,112 B over 40 / 0 B over 40 |

**Reading.** A skip's frames would otherwise have scanned 99 ticks a player each — about 536 × 11 × 99 probes on this
skip. Not measured: how many moments the post-seek model-decal replay builds (`FramePresenter.ReplayModelDecals`); those
are ordinary frames, so each one more than four ticks from the last pays a landing's cost.
