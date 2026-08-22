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

**It is correlated with the SOURCE, not with how the recording ended.** Reported by the
tf2-comp-archive agent via `PinKushin/TF2DEMOSALVAGE-LOG.md` and then **reproduced here first-hand,
2026-08-21, over the 380 demos in `D:\tf2-demo-archive`:**

| Source | zero-tick headers |
|---|---:|
| ESEA Season 29 | 52 of 52 |
| ESEA Season 30 | 58 of 58 |
| ESEA Season 31 | 42 of 42 |
| **ESEA total** | **152 of 152 — 100%** |
| ETF2L Season 29 | 4 of 82 |
| ETF2L Season 30 | 0 of 71 |
| ETF2L Season 32 | 1 of 65 |
| **ETF2L total** | **5 of 218 — 2%** |
| the owner's own recordings | 0 of 10 |

**That is a different cause from the one above.** "The server died or the map changed" predicts a
rate scattered across sources; **152 of 152 predicts a processing step** — something in how ESEA
stores or re-serves demos drops the fields. The earlier 43% figure is the same phenomenon measured
over a set that happened to be 41% ESEA, so the two reconcile.

**Negatives, by contrast, do not occur.** The same sweep, plus all 53 corpus demos: **0 of 433 have a
negative tick count, frame count or signon length.** So the parser's permissiveness about negatives
is not justified by real files containing them — it is justified by the salvage rule alone (never
refuse to open), and `DemoSurvey` treating a non-positive count as "unstated" is what makes that
safe. Do not write "real demos have negative ticks" anywhere; it was believed here on 2026-08-21 and
does not survive 433 files.

**And the zero case is unreachable from the committed corpus.** All 53 gcor and lcor demos declare
real values — the era specimens are the owner's own clean recordings and the local ones come from
demos.tf, ETF2L, RGL and serveme, none from ESEA. So the case is common in the wild and **untestable
with a corpus demo**; it has to be authored, which is the case [[put-the-real-file-in-the-fixture]]
now names explicitly.

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
