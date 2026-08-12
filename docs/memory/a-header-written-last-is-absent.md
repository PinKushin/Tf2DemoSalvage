---
name: a-header-written-last-is-absent
description: Fields the writer fills in at the END of recording are zero in 43% of real demos, and zero parses cleanly.
metadata:
  type: project
---

`PlaybackTicks`, `PlaybackFrames` and `PlaybackTimeSeconds` are written into the demo header by
**seeking back to offset zero when recording stops**. Recording begins with zeroes there. A
recording that ends because the server died, the map changed or the process was killed never
reaches that write, so the file claims to be empty while holding a full match.

Measured 2026-08-12 on 370 ESEA archive demos: **159 — 43% — are truncated**, and every one lies
this way. One 110,238-frame recording of `cp_process_final` declared 0 frames, 0 ticks and 0.00
seconds.

**Why this is worse than the truncated tail it accompanies.** A missing tail eventually announces
itself: something runs out of bytes. A zero-length header parses cleanly, every field is in range,
and the file simply reads as an empty demo. It surfaced as a viewer bug — no timeline, dead play
button — not as a parser error.

**How to recover it:** the tick count exists twice by unrelated routes, once as a number the engine
wrote and once as a consequence of the commands. Walk the command stream and take the maximum
tick — `DemoSurvey.Measure` does this, and only when the header states nothing, because a complete
demo is authoritative and re-deriving it would mean reading 39 MB to confirm a number already in
hand. See [[two-recordings-of-one-value]].

**The general rule for this format:** any field a writer fills in at the end is a field that is
absent from a large fraction of real files. Treat "the header says zero" as "the header says
nothing", not as a measurement. Related: [[read-the-encoder-not-the-decoder]], because it is the
writer's control flow that explains the value, and nothing in the reader hints at it.
