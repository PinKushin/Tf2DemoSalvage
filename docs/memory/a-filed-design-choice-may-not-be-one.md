---
name: a-filed-design-choice-may-not-be-one
description: "B131 offered two shapes for a fix; Valve's source picked one and the codebase already had it, so the \"choice\" was four lines of plumbing."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:40:43.106Z
---

**B131 was filed as a genuine architectural choice** — carry lightmap coordinates into the entity
vertex format, or draw brushwork with the world shader and a per-instance transform — with the note
"not attempted, and deliberately not guessed at". Closed 2026-08-21 by reading two files.

`utils/vrad/vrad.cpp:703` lights **every** model's faces, not model zero alone, offsetting each by
its `origin` keyvalue "into their in-use position". `C_BaseEntity::DrawBrushModel` says an unmoved
brush entity is drawn by `view->DrawWorld` itself. So the engine does the second shape, and this
project already had it: `WorldVertex` has always carried `LightU`/`LightV`/`LightStep` for every
vertex and one shader has always served both paths. The stated cost of the first shape — "every
model vertex then carries fields only brushwork uses" — was already paid years earlier.

**How to apply:**

- **Re-read an old risk entry against the code before working from its framing.** The premises a
  risk was filed under age; this one described a vertex format that had since gained the fields, and
  the entry still read as authoritative.
- **A dilemma in a risk entry is a signal the source has not been read yet.** Two plausible shapes
  usually means nobody has looked at what the engine does. See
  [[nothing-is-closed]].
- **vrad lights brush entities where the mapper left them, once.** An opening door carries its
  closed-position lighting. No relighting step; the transform moves the geometry and the light rides
  on the vertices.
- **The half that hides: a supplied ambient cube OVERWRITES the lightmap sample.** Correct
  coordinates plus a cube still draws flat, so the fix is two edits and only one of them looks like
  the fix. `ModelInstance.Light` is nullable for that reason — null means "lightmapped", not
  "unlit". Assert both kinds in one test; either alone passes against a constant.
  See [[output-level-assertion-or-it-is-not-done]].

Related: [[read-the-map-before-the-renderer]], [[a-test-can-outlive-its-design]],
[[wire-faithful-is-not-state-faithful]].

---

## `an-unrecoverable-input-is-not-an-open-choice` — draw it the way the engine draws it

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
wrong. A note in the repo is not automatically the standard, which is the same lesson as the
section above.
