---
name: a-new-entity-must-not-borrow-an-index
description: Anything this project draws that is not the networked entity needs its own index — per-entity caches are keyed by index and slots are reused, so borrowing one inherits another model's pose and crashes.
metadata:
  type: feedback
---

**When you add something drawn that is not exactly the demo's entity, give it an index of its own.**
`EntityModelSet` keys the pose, the skinning buffers and the visible set by entity index, and a demo
reuses indices briskly — slot 752 is a prop, then a corpse, then something else. Borrowing the slot
inherits whatever was cached under it, and the first frame where the two models have different bone
counts is an `ArgumentOutOfRangeException` inside `Skinning`.

**This project already had the pattern and the new code did not follow it.** `ViewmodelScene` puts
the arms and weapon at 4096..4098 with a comment saying why; corpses (B318) now take 2048..4095. The
engine does the same thing for the same reason — a ragdoll becomes a CLIENT-side entity through
`InitAsClientRagdoll`, and Source gives those indices at or above `MAX_EDICTS`, so they cannot
collide with anything the server sends.

**Offsetting the slot is not enough; the index must be unique per OBJECT.** Adding 2048 to a corpse's
entity index still gives the second occupant of a reused slot the first one's caches. Key on
something unique for the life of the timeline — the position in the list — or the same crash comes
back, rarer and harder to reproduce.

**Follow the index through everything that keys on it.** The corpse fade asks whether it was visible
last frame, and the renderer's visible set holds what it DREW. Left asking under the old slot it
would have reported every corpse unseen and expired them all on the wrong timer: no crash, no failing
test, just a mechanism that runs and is never right.

**No test caught this and the shape of that is worth knowing.** Twelve assemblies and the UI suite
were green across two full gate runs, and the crash was on the first frame with a corpse in view.
Nothing builds a scene where one index carries two models over time, and the corpus suites do not
render. **`--measure` found it in seconds** — a twenty-second playback run made for a performance
check. Run the viewer over a real demo after adding anything drawn, not only the suites.

Related: [[output-level-assertion-or-it-is-not-done]], [[a-moves-regressions-are-wiring]],
[[wire-faithful-is-not-state-faithful]], [[three-test-levels-and-the-third-is-missing]].
