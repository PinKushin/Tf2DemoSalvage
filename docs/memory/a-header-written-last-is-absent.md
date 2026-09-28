---
name: a-header-written-last-is-absent
description: "Fields the writer fills in at the END of recording are zero in 43% of real demos, and zero parses cleanly."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:51:12.510Z
---

`PlaybackTicks`, `PlaybackFrames`, `PlaybackTimeSeconds` are written into the demo header by seeking
back to offset zero when recording stops. A recording that ends abnormally (server died, map
changed, process killed) never reaches that write, so the file claims to be empty while holding a
full match.

Measured 2026-08-12 over 370 ESEA archive demos: **159 (43%) truncated this way** — one 110,238-frame
`cp_process_final` recording declared 0 frames/ticks/seconds.

**Correlated with SOURCE, not how recording ended.** Reproduced 2026-08-21 over 380 demos in
`D:\tf2-demo-archive`:

| Source | zero-tick headers |
|---|---:|
| ESEA (S29/30/31) | **152 of 152 — 100%** |
| ETF2L (S29/30/32) | 5 of 218 — 2% |
| owner's own recordings | 0 of 10 |

100% predicts a processing step in how ESEA stores/re-serves demos, not "server died" (which would
scatter). The earlier 43% is the same phenomenon over a 41%-ESEA sample.

**Negatives never occur:** 0 of 433 demos (full sweep + all 53 corpus) have a negative tick/frame/
signon count — the parser's negative-tolerance is justified only by the salvage rule (never refuse
to open), not by real files; `DemoSurvey` treating non-positive as "unstated" makes that safe. The
zero case is also unreachable from the committed corpus (all 53 gcor/lcor demos are clean; none come
from ESEA) — untestable without an authored fixture ([[fixtures-are-the-weak-point]]).

**Worse than a truncated tail:** a missing tail eventually runs out of bytes; a zero header parses
cleanly and reads as an empty demo — surfaced as a viewer bug (no timeline, dead play button), not a
parser error.

**Recovery:** the tick count exists twice by unrelated routes — the engine-written number, and the
max tick walked from the command stream. `DemoSurvey.Measure` takes the latter only when the header
states nothing (a complete demo is authoritative; re-deriving would mean reading 39MB needlessly).

**General rule for this format:** any field a writer fills in at the end is absent from a large
fraction of real files — treat "header says zero" as "header says nothing", not a measurement.
Related: [[read-the-encoder-not-the-decoder]].
