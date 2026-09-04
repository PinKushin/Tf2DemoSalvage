---
name: look-for-the-instrument-before-building-one
description: Three measurements in one session already existed and were unread; grep the logs before adding a counter.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-04T01:54:18.280Z
---

**Three times in one session (2026-09-03) a measurement was about to be built that already existed.**

- B254's *"every prop is posed"* is answered by `posed N of M selected` in the moment cost log,
  which reports 9 of 567.
- B258's *"sample is 2.0 ms"* is re-taken by `--measure`, one flag, and comes back 0.3.
- B262's *"count second-cull rejections"* is answered by `opaque draw order: 152 of 152 models
  kept` — a line that had been printed on every run and read by nobody.

For the third, a counter and a second log line were actually written before the existing line was
noticed. Two routes to one number, free to disagree, which is what [[B243]] is about — the fix was
to delete the new one.

**Why:** this project instruments heavily, so the prior probability that a number already exists is
high. Building a second one costs more than the code: it makes the two answers independent, and
when they differ nothing says which is right.

**How to apply:**

- **Grep the logs for the quantity before writing a counter.** `grep -i <thing>` over a viewer log
  is seconds; the alternative is a divergent instrument.
- **Read the whole line, not the part you came for.** `opaque draw order` was printing "152 of 152"
  next to bucket counts that were being read for something else.
- **A stale entry that asks for a measurement often predates the instrument that answers it.** Take
  the measurement first; it may close the entry outright.

See [[an-instrument-unread-is-not-an-instrument]] and
[[a-measurement-recorded-as-a-conclusion-expires]].
