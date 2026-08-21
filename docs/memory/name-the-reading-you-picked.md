---
name: name-the-reading-you-picked
description: An underspecified request gets resolved into a design decision, and the resolution then reads as the requirement.
metadata:
  type: feedback
---

**When a request admits more than one implementation and the choice is load-bearing, say which
reading was taken and why, in the same commit.**

**Why:** "a top down map view" was implemented as an orthographic projection on 2026-08-12. That is
an ordinary reading and nobody was wrong. But a projection had been chosen where only a *viewpoint*
was asked for, and nothing recorded that the request had admitted more than one answer.

Nine days later that choice had produced a second projection, a decal bias retuned to suit it that
was wrong the moment a real camera existed (B135), a height cut that is not a height (B136), a
reflection gap with no eye vector (B126), and **two** separate attempts — 2026-08-14 and 2026-08-21
— to reconcile the two projections, both reverted. Nobody asked why there were two, because the
first one read as a requirement rather than as a decision.

The owner's own account, which is the fair one: *"the ortho cam is probably mostly my fault, i didnt
really know the design completely at first, and didnt ecpress that the first cam should be like
valves cam, just said i wanted a top down map view."*

**How to apply:**

- `TopDownCamera` was written up well, with reasons attached — that is precisely what disguised it.
  A considered implementation and a considered *choice between implementations* look identical in
  the history unless the alternatives are named.
- One line is enough: "implemented as an orthographic projection rather than a high perspective
  camera, because …". **A design decision recorded as a design decision can be revisited; one
  recorded as a requirement cannot.**
- The tell that a reading is load-bearing: it introduces a second *kind* of something the codebase
  already has one of — a second projection, a second coordinate convention, a second lighting path.
  Those are the ones to name.
- Do not backdate blame when the record is written late. The first account of this was "an assistant
  substituted its own design", and the owner corrected it to an underspecified request. The
  correction is the accurate one and the entry was rebuilt on it — see [[a-filed-design-choice-may-not-be-one]]
  for the sibling case where a *risk* entry's framing was the thing that was wrong.

Related: [[build-time-shortcuts-assume-the-camera]], [[read-the-spec-before-measuring-our-data]].
