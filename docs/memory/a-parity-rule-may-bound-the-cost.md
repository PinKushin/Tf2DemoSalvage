---
name: a-parity-rule-may-bound-the-cost
description: before presenting Valve parity as a cost, look for what the engine rule bounds — the corpse fade caps a seek's replay at ~15 s
metadata:
  type: feedback
---

On 2026-09-19 I offered three ways for faded corpses to leave the physics world, framing Valve's camera-dependent fade as a
cost against D181's background record. The owner: *"i dont mind you offering the alternatives, the problem was simply you
framed valve parity wrong and where thinking about the ragdoll fade wrong, the 15 sec fade out is literally a good thig for
us, it means we DONT HAVE TO SIM EVERY RAGDOLL FROM THE BEGINNING, 15s of physics and ragdoll calculations shouldnt be an
issue"*.

**Why:** a corpse exists only until its fade, so any moment depends only on deaths inside the fade window — the engine's rule
is what makes a seek cheap, not what makes it hard. I treated the rule as an obstacle to a design (the background record)
instead of asking what it bounds.

**How to apply:** when a parity rule seems to fight a design, first ask what the rule LIMITS (lifetimes, windows, counts) — the
limit often makes the design unnecessary. Offering alternatives is fine; framing parity as the expensive one without that
check is the error. [[valve-parity-is-the-first-principle]] [[ask-what-the-request-is-for]]
