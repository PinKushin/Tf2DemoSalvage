# Measurement 2026-10-22: the particle step and build on worker threads

## Observation

`2026-10-22-f12-camera-breakdown.md`: on f12 tick 30000 first person, `camera particles` is 2.4 ms a frame, of which
`bursts` 0.9-1.0 (its `step` 0.35) and `build` 0.65. Finding 93 estimated (not measured) 0.5-0.7 ms saved by running the
step and the build per effect on worker threads, as `r_threaded_particles` does the simulation (particlemgr.cpp:1869-1884).
Commit `eb5f63ea` does that, byte-identical by test, behind `cl_frame_workers` (1 = serial; this machine's default 6).

## Question

Same scene and command as the breakdown, one binary, `+cl_frame_workers 1` against `+cl_frame_workers 6`: how much do
`camera particles/bursts/step`, `camera particles/build`, `camera particles`, `camera` and the whole frame change?

## Prediction

`build` falls from about 0.65 to 0.25-0.45 ms and `bursts/step` from about 0.35 to 0.15-0.3; `camera particles` by
0.3-0.6 ms; the whole frame by about the same, which is inside run-to-run noise. Falsified if `build` falls by under
0.1 ms, or if any threaded part rises above its serial value by more than 15%.

## Method

Debug viewer built from `eb5f63ea`. Four runs interleaved S1, T1, S2, T2 (S = `+cl_frame_workers 1`, T = `6`), each

```
TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 \
  managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe \
  C:/Users/pinku/source/repos/PinKushin/Tf2DemoSalvage/tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem \
  --tick 30000 --first-person --measure 20 +fps_max 0 +cl_frame_workers <1|6>
```

CPU load (`Get-CimInstance Win32_Processor` LoadPercentage, 3 x 2 s) before and after each run. Steady = the per-second
lines after the first five, frame-weighted. The `bursts/step` and `build` parts are wall-clock intervals on the render
thread around regions that now include a parallel `For`; under T they measure the main thread's WAIT for the workers plus
the serial passes, not the work, and are named so.

## Control

- **Same content:** S and T runs report the same census lines (model decals, bursts) at comparable ticks.
- **Same pictures:** before the timing, a fixed-step playback (`TF2VIEW_FIXED_FRAME=0.0125`, shots at 12 ticks from 30025
  to 30300) captured serial, at 8 workers and serial again; every T shot must equal its S shot byte for byte, and the two
  serial runs must equal each other (the instrument's control: if they differ, the picture is not deterministic and the
  comparison proves nothing).
- **Switch works:** the T run's parts must differ from the S run's somewhere in `particles/*`; identical numbers to two
  places would mean the setting never reached `ParticleEffects`.
- S1 and S2 agree within 15% on `camera particles`, or the pair is void.

## Environment

RTX 3060, i5-10600K (12 logical processors, so the default is 6), Debug viewer built from `eb5f63ea`, every run through
`run-exclusive.ps1`. Load (3 x 2 s) before / after: S1 10/3/100 / 7/11/10, T1 5/8/2 / 11/4/7, S2 8/7/3 / 7/9/10,
T2 4/3/5 / 4/7/3. S1's one 100% sample was before it started and its steady numbers match S2's.

## Result

Raw output: `2026-10-22-threaded-particles/S1-T1-S2-T2.txt` (timing) and `shots-runs.txt` (picture control). Steady = 13
per-second lines after the first five, frame-weighted (the arithmetic of the breakdown's `parts.ps1`, run inline). ms a frame.

| run | frame | camera | particles | bursts | bursts/step (wait) | build (wait) | pose | draw |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| S1 (1 worker) | 11.20 | 6.55 | 2.08 | 0.91 | 0.36 | 0.73 | 3.82 | 2.12 |
| T1 (6 workers) | 10.75 | 6.58 | 2.05 | 0.89 | 0.28 | 0.68 | 3.84 | 2.13 |
| S2 (1 worker) | 11.41 | 6.57 | 2.04 | 0.91 | 0.36 | 0.68 | 3.83 | 2.12 |
| T2 (6 workers) | 10.87 | 6.60 | 2.01 | 0.85 | 0.28 | 0.70 | 3.91 | 2.13 |

**The frame column is not explained by any phase.** The steady phase means (sound, camera, advance, hud, draw) sum to
10.96 / 11.01 / 10.92 / 10.99 ms for S1 / T1 / S2 / T2, so the phases are equal within 0.1 ms, and the T sums sit ABOVE the
T frame column. The column (the `, N ms;` field of each per-second line, as `parts.ps1` reads it) is therefore not the sum
of the phases, and its 0.3-0.7 ms difference is not taken as a saving. GC, from the first `gc:` line of each run: paused
0.58 / 0.62 / 0.59 / 0.63 ms a frame, allocated 2.44 / 2.48 / 2.44 / 2.47 MB a frame, so threaded allocates about
0.03-0.04 MB a frame more and pauses about 0.04 ms more.

**Controls.**
- Same content: the census lines of all four runs report `model decals 75` and burst counts in the same range at the
  same ticks (met).
- **Same pictures: FAILED, and so it proves nothing either way.** The two SERIAL runs differ from each other: the PNG at
  every one of the four ticks compared (30025, 30100, 30200, 30300) has a different hash in serial 1 and serial 2, and their
  census lines already differ at tick 30213 (bursts 44 against 45). `TF2VIEW_FIXED_FRAME` did not make playback repeat:
  the 40-second runs reached ticks 32535, 32365 and 32612, so the demo still advanced on the wall clock. The picture of a
  played frame is not deterministic here, so no serial-against-threaded comparison of pictures can be made with it.
- Switch works: `bursts/step` is 0.36 in both S runs and 0.28 in both T runs (met).
- S1 and S2 agree on `camera particles` within 2% (met).

## Verdict

**Prediction falsified.** Stepping the played bursts on six workers cuts the main thread's wait in `bursts/step` from 0.36
to 0.28 ms; the build does not move outside noise (0.68-0.73 serial, 0.68-0.70 threaded), so the gather it threads is a
small part of `build` or its merge copy eats what it saves; `camera particles` falls by about 0.03 ms and `camera` not
at all. The whole frame's phases are equal S against T; the frame column's lower T reading is not a saving (Result). Finding 93's
0.5-0.7 ms estimate for this row was about seven times too high. The byte-identity of the threaded output rests on the
synthetic equivalence test only; the output-level f12 comparison is still open, because the fixed-step capture is not
deterministic.
