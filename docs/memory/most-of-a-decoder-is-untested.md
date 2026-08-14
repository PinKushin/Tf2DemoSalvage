---
name: most-of-a-decoder-is-untested
description: Real files take one path through a format decoder; sabotage each branch to find which, and say which are unproven.
metadata:
  type: feedback
---

A decoder written from a specification handles every case the specification allows. **The real
files take one path.** A green suite therefore verifies that one path and says nothing about the
rest, while reading exactly like proof that all of it works.

**Why:** measured on the studio animation decoder, 2026-08-13. It supports six encodings from
`studio.h`. Four sabotage checks, in order:

- wrong `Quaternion48` z scale — **green**, that path is never taken
- wrong run-length index past `valid` — **green**, at frame zero the other branch always runs, so
  the edit was unreachable
- flipped sign in `AngleQuaternion` — **green**, the Euler path is never taken
- wrong `Quaternion64` z scale — **three failures**, exactly the posed-model tests, six controls
  still green

All nine TF2 player models pose exactly one bone at frame zero — the root — carrying
`STUDIO_ANIM_RAWROT2`. Everything else inherits. So one sixth of the decoder is proven and five
sixths are unproven code that will meet its first real input in production.

**How to apply:** after writing a format decoder, sabotage each branch and record which ones the
corpus can actually kill. Two of the three green results above were *unreachable-condition*
failures, not weak assertions — strengthening the assertion would have done nothing, and the
instinct to do that is the wrong move ([[differential-beats-fixtures]], and the four routes to an
insensitive test). Then **write the coverage limit into the class comment**, because the next
reader's default assumption is that a passing suite covers the file.

Related: [[mutation-score-is-not-the-goal]] — the point is knowing which mutants are reachable,
not killing them; [[real-data-hides-bugs-small-inputs-expose]] is the same asymmetry from the
input side; [[logs-are-the-debugger]] is how the one live path got identified (logging the posed
bone count and its values, rather than guessing which branch ran).
