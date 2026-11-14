# B613: what the f12 timeline decode allocates, and what removing it saves

## Observation

B613 (`docs/RISKS.md`): loading `demostf-cp_process_f12-2026-08-08-2207.dem` takes about 300 s, of which `DemoTimeline.Build`
is 150 to 250 s (`2026-11-13-b613-load-phases.md`). A CPU trace of that build (`dotnet-trace report … topN`, the lead's
scratch trace `b613/f12-timeline.nettrace`) puts `Thread.PollGC` at 29% exclusive and `GC.RunFinalizers` at 11%, with
`EntityState.SoundscapeIndex`, `RecordProp`, `BoneControllers`, `EntityState.Integer(string)`, `PoseParameters`,
`MyWeapons`, `AnimationLayers` and `ItemOf` the next named functions. About 40% of the decode is GC, so allocation is
the lever. `SoundscapeIndex()` reads `Integer($"{LocalDataTable}.m_audio.soundscapeIndex")`: a string built and hashed
per call, per entity update.

## Question

How many bytes does one `DemoTimeline.Build` of the f12 demo allocate, which methods allocate them (by type), and how
many of those bytes, and how many seconds, does removing the largest allocators save, with behaviour unchanged?

## Prediction

Fixed before the run:

- Baseline allocation is above 20 GB for the one build (the GC share of the trace needs a very high allocation rate).
- The largest single allocator is `string` built inside `EntityState`'s by-name accessors (interpolated property
  names), followed by fresh arrays returned per accessor call (`BoneControllers`, `PoseParameters`, `AnimationLayers`,
  `CarriedItems`).
- Fixing those cuts the build's allocation by at least half, and the build's seconds by at least a quarter.

Falsified if the baseline is under 5 GB, if strings are not among the top three allocated types, or if the fixes save
under a quarter of the bytes.

## Method

The deterministic instrument is the `timeline-cost` probe's `allocated` column (`GC.GetTotalAllocatedBytes(precise:
true)` around `DemoTimeline.Build`, carried by the probe itself). Release build, built in the same command by
`dotnet run`, under the machine-wide lock:

```
pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 -TimeoutMinutes 120 dotnet run \
  --project tools/Tf2DemoSalvage.Probe -c Release -- timeline-cost demostf-cp_process_f12-2026-08-08-2207
```

Because a run can wait an hour for the lock while the tree changes, each run executes a snapshot of that Release build
(`dotnet build tools/Tf2DemoSalvage.Probe -c Release`, then `bin/Release/net10.0` copied to the git-ignored
`tools/Tf2DemoSalvage.Probe/bin/snap-<label>/`), run under the lock as `snap-<label>/Tf2DemoSalvage.Probe.exe
timeline-cost demostf-cp_process_f12-2026-08-08-2207`. The binary is the one `dotnet run -c Release` would have built.

Run once before the fixes and once after each fix (or batch of fixes). Then, because single runs minutes apart see
different machine load, the baseline and final snapshots alternated twice back to back under one lock hold
(`before`, `final`, `before`, `final`) for the seconds comparison. The `total` seconds column is recorded too, and
treated as noisy: other agents share this machine.

To find the allocators, the same probe (Release, already built by the run above) under `dotnet-trace collect --profile
gc-verbose`, whose `GCAllocationTick` events (one per ~100 KB allocated) carry the allocated type and a stack; the trace
is grouped by type and by allocating method with `dotnet-trace report <file> topN` and the PerfView-style tick counts.

## Control

- The `allocated` column is asked of a demo whose answer must be non-zero; a zero or an absurdly small figure means the
  counter is not around the build.
- The probe prints `frames`, `tracks` and `players`; those must be identical before and after every fix, or the fix
  changed behaviour (the test suites are the main behavioural check: Core, Scene and Corpus at
  `TF2DEMOSALVAGE_GCOR_ONLY=1`).
- The allocation-tick trace's type totals must add up to roughly the probe's `allocated` figure (ticks × ~100 KB), or
  the trace is sampling something else.

## Environment

The owner's 12-core machine, shared with other agents (load not controlled). Worktree
`.claude/worktrees/agent-a38c7c0e3f3cf49ea`, branch `perf/f12-decode`, baseline at `3860debd7` plus this file. lcor is a
junction to the main checkout's `tools/corpus/local`.

The snapshots, each a Release build of the working tree at the time, cumulative:

| snapshot | adds |
|---|---|
| `before` | nothing: `3860debd7` plus this file |
| `keys` | `ElementKeys`: `m_hMyWeapons`, `player_array`, `m_hMyWearables`, `m_iAmmo`, `m_flItemChargeMeter` and the player resource's sixteen per-slot arrays built once instead of formatted per read |
| `round2` | `ScenePlayerDetail.SameValues` clones one side instead of two; the frame's player list sized to the last frame's; `EntityDecoder.ReadProperties` reuses one list |
| `round3` | `SceneObjectives.Key` and the eight `m_audio.localSound[n]` keys built once (the final state) |

Other agents ran builds, tests and UI suites in the same hour (the lock reported holders before every run); load was not
sampled.

## Result

**Allocation, the deterministic column** (`allocated`, carried from `GC.GetTotalAllocatedBytes(precise: true)` around the
build). `frames 96589`, `tracks 4755`, `players 13` in every row, before and after.

| snapshot | allocated | runs |
|---|---|---|
| `before` | 30,182 / 30,175 / 30,163 MB | three |
| `keys` | 16,006 MB | one |
| `round2` | 10,345 MB | one |
| `round3` | 8,026 / 8,026 / 8,026 MB | three |

The baseline moves by about 20 MB between runs (some allocation depends on timing); the final state did not move at all
in three runs.

Raw probe lines (`timeline-cost demostf-cp_process_f12-2026-08-08-2207`, columns as the probe's header: MB, total,
commands, schema, messages, entities, sampling, (viewmodels), building, rest, frames, tracks, players, us/frame-track,
gc-paused, allocated):

```
before     58.2 152.87s 0.05s 0.01s 0.68s 60.58s 33.52s 0.42s 56.97s 1.06s 96589 4755 13 0.13 3.51s 30182MB
keys       58.2 176.68s 0.03s 0.01s 0.70s 74.77s 40.87s 0.53s 58.98s 1.31s 96589 4755 13 0.16 2.92s 16006MB
round2     58.2 124.72s 0.03s 0.01s 0.52s 54.07s 29.55s 0.34s 39.76s 0.77s 96589 4755 13 0.12 1.94s 10345MB
round3     58.2 145.68s 0.03s 0.01s 0.58s 61.19s 37.28s 0.45s 45.70s 0.89s 96589 4755 13 0.13 2.03s  8026MB
-- alternated, one lock hold, 02:06 to 02:17 --
before     58.2 139.17s 0.03s 0.01s 0.57s 56.97s 27.31s 0.36s 53.42s 0.87s 96589 4755 13 0.12 3.20s 30175MB
round3     58.2 122.09s 0.03s 0.01s 0.51s 55.93s 26.84s 0.38s 37.99s 0.78s 96589 4755 13 0.12 1.82s  8026MB
before     58.2 188.45s 0.03s 0.01s 0.72s 72.21s 41.67s 0.50s 72.60s 1.21s 96589 4755 13 0.16 3.88s 30163MB
round3     58.2 195.02s 0.03s 0.01s 0.79s 79.63s 51.15s 0.59s 62.24s 1.17s 96589 4755 13 0.17 3.22s  8026MB
```

(The `keys` and `round3` single rows were the untraced runs taken just before each `gc-verbose` trace.)

**The allocators** (`alloc-trace <trace> 3 all`, the probe's own grouping of `GCAllocationTick` by type and first
non-plumbing frame; figures are its MB per 20 s, so multiply by trace seconds / 20 for the build's total). Control: the
trace's total of ticks was 30,234 MB before (probe: 30,182), 16,069 MB at `keys` (16,006) and 8,086 MB at `round3`
(8,026), so the trace saw the build's allocation and nothing else.

Before (145.9 s traced, ×7.3), the top eight types:

| type | MB/20 s | ≈ GB | where |
|---|---|---|---|
| `String` | 2,271 | 16.6 | `MyWeapons` from `MedigunOf` 439 and from `CarriedItems` 436, `TeamPlayers` 280, `ScoreboardPlayers` 255, `AmmoCounts` 252, `ItemChargeMeters` 132; the rest below the report's cut |
| `DecodedProperty[]` | 564 | 4.1 | `EntityDecoder.ReadProperties`, a list grown from empty per entity |
| `ScenePlayerDetail` | 223 | 1.6 | `SameValues` 141 (two `with` clones per comparison), `Run` 80 (the detail itself) |
| `ScenePlayer[]` | 193 | 1.4 | `Run`, the per-frame player list's growth and `TrimExcess` copy |
| `SceneItem` | 103 | 0.8 | `ItemOf` from `CarriedItems` |
| `Int32[]` | 85 | 0.6 | `InterpolatedHistory.Add` 31, `AmmoCounts` 25 |
| `Compact[]` | 84 | 0.6 | `ReadProperties`, the decoded entity's own storage |
| `Single[]` | 55 | 0.4 | `InterpolatedHistory.Add` 20, `ItemChargeMeters` 12 |

At `keys` (163.5 s traced, ×8.2): strings fell to 293 MB/20 s, led by `Positions` (`SoundscapePosition`'s key, 101)
and `SceneObjectives.Key` (about 120 across `Read`, `Ints` and `Previous`); `DecodedProperty[]` 504, `ScenePlayerDetail`
201 (`SameValues` 129), `ScenePlayer[]` 175.

At `round3` (115.5 s traced, ×5.8), what is left of 8,086 MB: `ScenePlayerDetail` 194 (`Run` 101, `SameValues` 93, now
one clone), `SceneItem` 130, `Int32[]` 107, `Compact[]` 105, `ScenePlayer[]` 77, `Single[]` 69, `SceneGesture[]` 47
(`GesturesFor`), the eight-slot `Positions` array 46. `String` and `DecodedProperty[]` are no longer in the top eight.

Each fix's share, from the probe where it was measured alone and from the trace's sites where it was measured in a batch
(*interpolated* for the batch, since only the batch total is a probe figure):

| fix | saved |
|---|---|
| element keys built once (`ElementKeys`) | 14,176 MB, probe (30,182 to 16,006) |
| `ReadProperties` reuses its list | ≈ 4.1 GB, trace (504 MB/20 s at `keys`, gone at `round3`) |
| frame player list sized to the last frame | ≈ 1.0 GB, trace (175 to 77 MB/20 s) |
| `SameValues` one clone | ≈ 0.5 GB, trace (129 to 93 MB/20 s) |
| the three above together | 5,661 MB, probe (16,006 to 10,345) |
| `SceneObjectives` and `localSound` keys built once | 2,319 MB, probe (10,345 to 8,026) |
| **total** | **22,156 MB, probe (30,182 to 8,026), 73%** |

**Seconds.** Run alone, the totals wander from 122 to 195 s for one binary; in the alternated pair, the first pass went
139.2 to 122.1 s and the second 188.5 to 195.0 s. GC pause over the build fell in every pairing (3.20 to 1.82 s and 3.88
to 3.22 s alternated; 3.51 s before against 1.94-2.03 s after alone). The `building` column (the per-frame player build,
where most of the removed allocation was) fell in every pairing: 53.4 to 38.0 s and 72.6 to 62.2 s alternated.

## Verdict

**The f12 timeline build allocates 8,026 MB instead of 30,182 MB, 73% less, deterministic to the megabyte; its seconds did
not measurably change against this machine's load.** The prediction is **partly confirmed, partly falsified**: the
baseline was above 20 GB (30.2), strings were the largest type (16.6 GB) and came from the by-name accessors, and the fixes
saved more than half the bytes, all as predicted; but the strings came from per-element keys formatted with a number
(`m_hMyWeapons.{n:D3}` and its kind), not from the `$"{Table}.name"` interpolations, which are compile-time constants and
allocate nothing, so `SoundscapeIndex`'s 5% of CPU is the string-keyed lookup's hashing, not garbage. And the seconds
saving of a quarter is **inconclusive**: one alternated pair saved 12%, the other lost 3%, and the run-to-run spread (122 to
195 s for one binary) is wider than any saving the GC column allows. GC pause is now about 2 s of a two-minute build, so
allocation is no longer the lever for B613's length; the CPU work in `RecordProp`, the per-frame player build and the
string-keyed lookups is.
