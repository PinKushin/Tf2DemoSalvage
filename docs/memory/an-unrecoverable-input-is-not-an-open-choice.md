---
name: an-unrecoverable-input-is-not-an-open-choice
description: A value the demo cannot carry does not make the engine's logic a decision to put to the owner.
metadata:
  type: feedback
---

**When the engine's answer comes from something a demo cannot record, reproduce the MECHANISM and
draw the input the way the engine draws it.** Do not convert it into a menu.

The owner, 2026-09-04: *"you should of done it valves way, but too late for that."*

`CreateTFRagdoll` decides death-animation against ragdoll physics with a `RandomFloat` on the
recording client's own stream (`c_tf_player.cpp:829`), recorded nowhere. I read "the value is
unrecoverable" as "the behaviour is undecided" and offered three options, two of which were not
Valve's way. **The standing decision forbids exactly that**: never ask which of Valve's way and
another way to take.

**Valve's way was the branch itself.** The engine draws a random number, so we draw one — 25% death
animation, 75% physics. That is not an approximation of the engine, it IS the engine, and it
reproduces the distribution a viewer saw. The only forced adaptation is seeding the draw per corpse,
because this project can seek and the client could not.

**Distinguish this from a real divergence.** [[parity-is-the-search-not-the-defence]] is about
deliberately doing something ELSE, which does need asking. An unrecoverable input is not that: the
logic is decided, only the input is missing.

**And a filed finding can carry the same mistake.** `PARITY-AUDIT.md` #4 said the branch was "a
divergence to be ASKED about" — I followed the document rather than the rule, and the document was
wrong. A note in the repo is not automatically the standard.
