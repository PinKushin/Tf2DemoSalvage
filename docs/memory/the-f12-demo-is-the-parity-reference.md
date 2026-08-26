---
name: the-f12-demo-is-the-parity-reference
description: "Use the cp_process_f12 demos for parity and before/after checks — the owner knows them best, so their eye is a working instrument on those and not on an arbitrary demo."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-26T03:38:10.416Z
---

**For any check that compares against previous behaviour, use a `cp_process_f12` demo.** The owner,
2026-08-25:

> "we use the f_12 demo for parity to the old code checks, because that is the demo i know the best
> outside of my era specimins"

`tools/corpus/local/demostf-cp_process_f12-2026-08-07.dem` and `-2026-08-08-2207.dem`. The matching
`cp_process_f12.bsp` is already in the TF2 install, put there deliberately so the real client can
play it.

**Why it matters, and it is not a preference about demos.** On a UI question the owner's eye is the
instrument — anything about a picture that cannot be verified by looking is a question for them, not
a claim. An instrument only works on a subject it knows: they can say "that door is wrong" about
`cp_process_f12` because they have watched it many times. On a demo picked at random they can only
say "something looks off", which is where an evening goes.

**Measured, the same day.** I picked `etf2l-12030-stv-2020-07-23.dem` for a before/after check
because it was the first STV in the folder. Five defects were reported, read as regressions from a
large refactor, and six hypotheses were investigated and falsified. None of it was the refactor. The
demo was simply one the owner had never examined — and one the live TF2 client refuses outright
(B201, schema drift). **Choosing the subject badly cost more than the whole investigation.**

**How to apply:**

- **Parity, before/after, "did I break it" — use f12.** A comparison is only as good as the
  observer's familiarity with the subject.
- **Era questions — use the gcor specimens**, which are the owner's own period recordings and the
  only demos dated exactly.
- **A demo neither of you knows is for finding NEW defects**, never for judging a change. It cannot
  distinguish "this broke" from "this was always like that".
- **Before reporting a regression from an unfamiliar demo, run the OLD build on the same file.** One
  launch would have ended that evening at the start.

Related: [[ask-which-input-differs-before-bisecting]], [[a-picture-is-assertable]],
[[record-both-points-of-view]], [[pov-demos-are-pvs-limited]].
