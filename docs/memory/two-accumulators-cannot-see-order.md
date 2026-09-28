---
name: two-accumulators-cannot-see-order
description: Two lists filled by two different callbacks cannot measure which callback ran first; the observer has to read the observed.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-10T22:52:47.282Z
---

**A test recording two things into two independent lists and asserting on both looks thorough and is
blind to ordering.** Both assertions can be exact and true, and swapping the order of two calls under
test leaves both true — the accumulators don't observe each other, so ORDER is not a variable this
experiment measures at all. Sabotage caught it; no stronger assertion could (wrong instrument, not a
weak assertion).

**Fix: make the observer read the observed** — have one side read a shared variable the other side
just set, so fire-first and set-first produce genuinely different observations.

**How to apply:** when a claim contains "before"/"after"/"already"/"not yet", ask which side must READ
the other's state. If the answer is "neither, they both just append", restructure the test rather than
strengthen it. Related: [[instrument-bugs-outnumber-decoder-bugs]],
[[most-of-a-decoder-is-untested]]#a-duplicated-guard-cannot-be-tested.
