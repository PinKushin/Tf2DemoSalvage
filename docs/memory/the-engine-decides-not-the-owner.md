---
name: the-engine-decides-not-the-owner
description: An item whose engine behaviour is read is not "held for the owner" — do what the engine does (D259); ask only about above-Valve features or an ambiguous engine.
metadata:
  type: feedback
---

When an item's engine behaviour has been read, the engine is the answer: do what it does. Do not hold it "for the owner"
because the change is visible or large.

**Why:** Owner, 2026-11-15, on B738 (freeze frame) and B615 (jiggle bones on real time) being held for him: *"those
decisions are engine decision i think not mine"*. D89/D129/D131 already settle parity; a read engine leaves nothing to
decide. Recorded as D259.

**How to apply:** Before writing "held for the owner" / "needs owner", ask whether the engine answers it. It stays the
owner's only when the engine cannot answer: a feature above Valve (`docs/ABOVE-VALVE.md`, D246 — parity never removes
one; keep it working beside the parity behaviour) or genuinely ambiguous engine code. See [[valve-parity-is-the-first-principle]],
[[a-picked-option-is-not-a-voiced-decision]].
