# B730: the frame cost of the channel steal's per-start OpenAL reads (2026-11-13) — INCONCLUSIVE

**Question.** `AudioOutput.PickSlot` reads each busy slot's state and sample offset on every dynamic start, about 128
OpenAL calls a start (B730, filed as unmeasured). Does the `sound` phase move?

**Prediction.** Under the phase's 0.1 ms print resolution: a few starts a frame at roughly 0.3 µs a call is tens of µs.

**Command**, both builds Debug and built in the same command, the machine-wide lock held, interleaved pre, post, pre,
post (`scratchpad/ab-b730.sh`, run in pmux):

```
TF2VIEW_AUTOPLAY=1 pwsh run-exclusive.ps1 tf2demoview.exe demostf-cp_process_f12-2026-08-08-2207.dem --tick 30000 --first-person --measure 20 +fps_max 0
```

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

**Verdict: inconclusive.**
- The quiet seconds differ by about 0.1 ms in post's disfavour, which is the print resolution. Post had fewer quiet
  seconds to compare.
- The B724 measurement, on an idle machine, put the whole phase at 0.4–0.5 ms.
- **To settle it,** rerun on an idle machine, or time `PickSlot` directly with a counter carried from the call. The phase
  line cannot resolve a tens-of-µs change.
- Until then B730's *Not changed* note stands: the per-start reads are unmeasured.
