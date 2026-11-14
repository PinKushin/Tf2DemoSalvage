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

Run once before the fixes and once after each fix (or batch of fixes). The `total` seconds column is recorded too, and
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

## Result

## Verdict
