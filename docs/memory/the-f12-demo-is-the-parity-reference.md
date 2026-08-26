---
name: the-f12-demo-is-the-parity-reference
description: "Hold the demo constant across a comparison and announce any change; f12 is today's reference because the owner knows it, and familiarity is earned rather than fixed."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-08-26T03:39:35.785Z
---

## The rule is HOLD THE SUBJECT, not "always f12"

The owner, clarifying:

> "its not a hard rule forever, if you play another demo enough then i can use it for checks too, but
> the problem comes when you change demos in the middle, expecially when its basically the same map
> so i dont realize immedietly that im watching a different demo"

**The defect is the SWAP, not the choice.** A comparison has a subject, and changing it mid-way
destroys the comparison whether or not the replacement is a good demo. Two consequences:

- **Announce the demo by name every time it changes**, in the message — not buried in a tool call
  nobody reads.
- **A similar-looking map is the DANGEROUS case, not the safe one.** `cp_process_final` and
  `cp_process_f12` look near-identical, and nothing on screen says which is loaded, so a silent
  substitution is invisible exactly when it matters most. An obvious swap gets noticed; this one
  does not.

**Familiarity is earned, not fixed.** Any demo watched enough becomes usable as a reference. f12 is
today's answer because it is the one with the hours in it — so a second reference is added by using
one repeatedly and deliberately, never by picking a fresh file per check.

## Why f12 is the one today

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
