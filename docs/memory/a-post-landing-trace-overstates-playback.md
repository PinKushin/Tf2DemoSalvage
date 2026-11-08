---
name: a-post-landing-trace-overstates-playback
description: An allocation trace of the 0.9 s after a seek's landing, scaled to 20 s, overstates a 20 s --measure window by about 2.5 times; divide predictions from it.
metadata:
  type: project
---

**The allocation a `dotnet-trace` shows just after a seek's landing is not the playback's average.** On z1800 at
`--tick 20000`, the trace's played window (64.7 to 65.6 s, scaled to 20 s by the `alloc-trace` probe) predicted the
removed garbage at 0.83 GB per 20 s for the IVP ring iterators and 0.93 GB for the scratch reuse. The `--measure 20` A/B
measured about 0.34 and 0.3 to 0.4 GB (0.10 to 0.13 MB a frame over about 3,100 frames). Both overshot by the same factor,
about 2.5 (`docs/measurements/2026-11-08-ivp-scratch-reuse.md`, whose gen0 prediction was falsified because of it).

**Why (interpolated, not measured):** the corpses are most active right after the landing, and they settle over the 20 s
window, so a corpse-physics rate read in the first second is the window's peak, not its mean.

**How to apply:** when a prediction for a `--measure` run comes from a post-landing trace window, divide the trace's
per-20 s figure by about 2.5. Better, trace the same 20 s the measurement covers. See also
[[measure-the-output-not-the-capability]] and [[a-win-above-0-01-ms-counts-the-target-is-1000-fps]].
