---
name: correct-counts-are-not-a-chain-of-custody
description: Six healthy counts reported success for a draw call that was never issued; only a screenshot could see the gap.
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 4774a88b-811c-40bb-9c79-9b22dc0a4474
  modified: 2026-09-06T00:24:01.055Z
---

A feature can be absent from the frame with **every instrument reporting success**, because each one
measures a stage that genuinely worked. Detail sprites (B360): the game-lump directory found `dprp`,
the reader returned 28,699 objects, the builder made 20,117 quads, the material resolved at index
206, `MapWorld` logged `398595 of 398595 prop triangles drawn`, and the renderer's blend census
listed material 206 as translucent. The hillside was bare.

The gap was between the last two: `DrawOpaqueBatches` skips translucent materials and the sorted
translucent list was built from the world's batches only, so every translucent PROP batch — five of
eleven on harvest — was issued by nothing (B362).

**Why:** each count answered "did this stage produce output", and none answered "was a draw call
issued". A chain of correct counts is not a chain of custody; the last link is the one nobody
instruments, and it is the only one that decides whether anything appears.

**How to apply:** when output is missing and every counter is green, stop adding counters — the next
one will be green too. Ask which pass actually ISSUES the work and whether the new thing is in that
pass's input list. Then confirm with a picture, pointed at coordinates taken from the data itself
rather than guessed: see [[point-the-camera-from-the-data]]. Related:
[[output-level-assertion-or-it-is-not-done]], [[instrument-bugs-outnumber-decoder-bugs]].
