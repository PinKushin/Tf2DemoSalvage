# B730: the frame cost of the channel steal's per-start OpenAL reads (2026-11-13) — INCONCLUSIVE

**Question.** `AudioOutput.PickSlot` reads each busy slot's state and sample offset on every dynamic start, about 128
OpenAL calls a start (B730, filed as unmeasured). Does the `sound` phase move?

**Prediction.** Under the phase's 0.1 ms print resolution: a few starts a frame at roughly 0.3 µs a call is tens of µs.

**Command.** Each of the four runs, interleaved pre, post, pre, post, built and ran its own tree in one command, from a
bash loop in pmux:

```
MSBUILDDISABLENODEREUSE=1 dotnet build <tree>/managed/Tf2DemoSalvage.Viewer3D &&
TF2VIEW_AUTOPLAY=1 pwsh C:/Users/pinku/source/repos/PinKushin/run-exclusive.ps1 -TimeoutMinutes 180 \
  <tree>/managed/Tf2DemoSalvage.Viewer3D/bin/Debug/net10.0-windows/tf2demoview.exe \
  tools/corpus/local/demostf-cp_process_f12-2026-08-08-2207.dem --tick 30000 --first-person --measure 20 +fps_max 0
```

The `frame rate` lines were kept and read by eye. "Quiet" means the frame column was 10 ms or less.

**Environment.** post = main `945b5f342` (B730 merged); pre = `821a69f63`, the merge before it. **The machine was NOT
idle**:
- three implementer agents were building and running tests;
- two gate phase-1 runs held the shared lock beside these runs.

The lock serialises the desktop, not the CPU.

**Raw output.** The `sound` phase of each per-second `--measure` line, as printed:

| run | quiet seconds (frame ≤ 10 ms) | all seconds |
|---|---|---|
| pre (20:29) | 0.3, 0.3, 0.3, 0.4, 0.5, 0.5 | 0.3–6.8 |
| post (20:39) | 0.4, 0.5, 0.6 | 0.4–5.8 |
| pre (20:47) | 0.3, 0.5, 0.5, 0.5 | 0.3–4.3 |
| post (20:55) | 0.4 | 0.4–8.4 |

- Frame rates swung between 13 and 166 fps within each run.
- The `sound` phase rose and fell with the `camera` phase. That is contention, not the steal.

**Verdict: inconclusive, and by design rather than by bad luck.** Two things were known before the run that made it
unable to answer:
- The predicted effect (tens of µs) is below the phase line's 0.1 ms print resolution.
- The machine was knowingly busy.

The busy machine only widened the noise. The instrument was already too coarse. The camera swings and the 13-166 fps
range cited above came from the same lines' other columns; they were not kept here.
- The quiet seconds differ by about 0.1 ms in post's disfavour, which is the print resolution. Post had fewer quiet
  seconds to compare.
- The B724 measurement, on an idle machine, put the whole phase at 0.4–0.5 ms.
- **To settle it,** rerun on an idle machine, or time `PickSlot` directly with a counter carried from the call. The phase
  line cannot resolve a tens-of-µs change.
- Until then B730's *Not changed* note stands: the per-start reads are unmeasured.
