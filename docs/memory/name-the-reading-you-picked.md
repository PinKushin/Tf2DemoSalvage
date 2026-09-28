---
name: name-the-reading-you-picked
description: An underspecified request gets resolved into a design decision, and the resolution then reads as the requirement.
metadata:
  type: feedback
---

**When a request admits more than one implementation and the choice is load-bearing, say which
reading was taken and why, in the same commit.**

"A top down map view" was implemented as an orthographic projection — an ordinary reading, but a
projection had been chosen where only a viewpoint was asked for, unrecorded. Nine days later that
choice had produced a second projection, a wrong depth bias (B135), a height cut that isn't a height
(B136), a reflection gap with no eye vector (B126), and two reverted reconciliation attempts —
because the first reading read as a requirement, not a decision.

Owner: *"the ortho cam is probably mostly my fault, i didnt really know the design completely at
first, and didnt ecpress that the first cam should be like valves cam."*

**How to apply:**
- A well-reasoned implementation and a considered CHOICE between implementations look identical in
  history unless alternatives are named.
- One line is enough: "implemented as X rather than Y, because…" — a decision recorded as a decision
  can be revisited; one recorded as a requirement cannot.
- Tell that a reading is load-bearing: it introduces a second KIND of something the codebase already
  has one of (a second projection, coordinate convention, lighting path).
- Don't backdate blame when the record is written late — correct the framing to match what actually
  happened.

Related: [[build-time-shortcuts-assume-the-camera]], [[nothing-is-closed]],
[[a-filed-design-choice-may-not-be-one]].
