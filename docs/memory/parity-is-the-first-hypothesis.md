---
name: parity-is-the-first-hypothesis
description: When anything is wrong in Tf2DemoSalvage, a divergence from the engine is the first hypothesis - and a cost symptom is parity evidence before it is an optimisation problem.
metadata:
  type: feedback
---

The owner, 2026-09-07: *"basically any time theres anything wrong, look at valve parity first"* —
and on a hang: *"if this is partiy it shouldnt be doing this"*.

**Why:** [[valve-parity-is-the-first-principle]] governs design and the standing note about
performance governs trades. Neither covers DIAGNOSIS, and that is where the defects are. Four for
four in one session: a contact solve invented because its engine function was unread; static props
missing from the physics world; a ragdoll colliding with playerclip that `MASK_SOLID` excludes; and
a hundred-pass loop transcribed at the wrong scope. None was a coding mistake in the ordinary sense.

**The speed half is the one that keeps getting missed.** The engine runs a server full of ragdolls
at 66 ticks a second, so when our transcription cannot keep up the likely cause is a DIFFERENT
algorithm, not the same one slowly. Treating cost as cost sent an afternoon the wrong way; see
[[an-optimisation-is-not-a-skippable-departure]] for the trade version of this.

**How to apply:** before proposing any cause for a defect, name the engine function or SDK file the
code corresponds to and check it. When the symptom is cost, ask specifically whether one iteration
of our loop covers the same thing as one iteration of the engine's — the failure that day was a
bound read correctly (`iVar15 < 100`, per mindist pair) and applied to the wrong set (every contact
of a ragdoll). `~/.claude/hooks/block-fix-without-parity.ps1` refuses a fix commit that cites no
engine; `not-parity:` is the audited escape. Recorded as D148.
