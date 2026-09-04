---
name: a-measurement-recorded-as-a-conclusion-expires
description: A risk entry that ranks work by a number goes stale silently; put the command that produced the number beside it.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T01:26:14.986Z
---

**Three OPEN entries in `docs/RISKS.md` were stale in one session (2026-09-03), all the same way.**
B157 described a substitution that had already been built. B254 said *"every prop the tick carries
is posed"* when nine of 567 are. B258 quoted `sample 2.0 ms` against a measured 0.3.

**None was wrong when written.** What they share is that a MEASUREMENT was written down as a
CONCLUSION. "Sample is 2.0 ms" is a fact about one build on one day. "Sample is the same size as
pose, so it is the next thing to fix" is a RANKING, and it expires the moment either number moves —
silently, because nothing re-runs it.

**Why it costs more than a wrong note.** These entries are the work queue. A stale one sends the
next session to re-derive a fixed problem, and it does so with the authority of a written record and
a citation. Two of tonight's hours went into confirming that two entries were describing a state the
code had left.

**How to apply:**

- **Put the command beside the number.** One line, runnable. `TF2VIEW_AUTOPLAY=1 tf2demoview <demo>
  --tick 14000 --first-person --measure 12 +fps_max 0` is cheaper to re-run than to argue with.
- **Re-measure before believing a ranking**, especially one that says "this is where the frame is".
- **Separate the reading of the engine from the ranking of the work.** B258's reading of
  `ProcessInterpolatedList` is still correct and still worth having; only its "therefore this is
  next" died. Kept apart, half the entry survives.
- **A counter that reports one of two exits reads as a failure of the whole.** B254's `0.3 hidden by
  pvs` looked like an idle cull; the frustum half simply returns first without counting. See
  [[a-ledger-must-cover-every-exit]].

See [[read-the-trx-total-not-the-console]] and [[an-instrument-unread-is-not-an-instrument]].
