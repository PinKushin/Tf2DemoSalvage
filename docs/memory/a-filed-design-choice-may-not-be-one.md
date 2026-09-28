---
name: a-filed-design-choice-may-not-be-one
description: "B131 offered two shapes for a fix; Valve's source picked one and the codebase already had it, so the \"choice\" was four lines of plumbing."
metadata: 
  node_type: memory
  type: project
  originSessionId: 1530d8fa-540e-408a-bb73-09b13bdff510
  modified: 2026-09-09T03:40:43.106Z
---

**B131 was filed as an architectural choice** — carry lightmap coordinates into the entity vertex
format, or draw brushwork with the world shader and a per-instance transform — "not attempted,
deliberately not guessed at". Closed 2026-08-21 by reading two files: `utils/vrad/vrad.cpp:703`
lights **every** model's faces, offsetting by `origin`; `C_BaseEntity::DrawBrushModel` shows an
unmoved brush entity is drawn by `view->DrawWorld` itself. The engine does the second shape, and this
project already had it — `WorldVertex` always carried `LightU`/`LightV`/`LightStep`.

**How to apply:**
- Re-read an old risk entry against the code before trusting its framing — this one described a
  vertex format that had since gained the fields.
- A dilemma in a risk entry usually means the source hasn't been read ([[nothing-is-closed]]).
- vrad lights brush entities where the mapper left them, once — an opening door carries its closed
  lighting; no relighting step.
- **The half that hides:** a supplied ambient cube OVERWRITES the lightmap sample, so correct
  coordinates plus a cube still draws flat. `ModelInstance.Light` is nullable for that reason (null =
  "lightmapped", not "unlit") — assert both kinds in one test ([[output-level-assertion-or-it-is-not-done]]).

Related: [[read-the-map-before-the-renderer]], [[a-test-can-outlive-its-design]],
[[wire-faithful-is-not-state-faithful]].

---

## `an-unrecoverable-input-is-not-an-open-choice` — draw it the way the engine draws it

**When the engine's answer comes from something a demo can't record, reproduce the MECHANISM,
don't turn it into a menu.** Owner, 2026-09-04: *"you should of done it valves way, but too late for
that."*

`CreateTFRagdoll` decides death-animation vs. ragdoll physics with a `RandomFloat` on the recording
client's own stream (`c_tf_player.cpp:829`), recorded nowhere. Reading "unrecoverable input" as
"undecided behaviour" and offering three options (two not Valve's way) violates the standing rule:
never ask which of Valve's way and another way to take.

**Valve's way was the branch itself** — draw a random number (25%/75%), just seeded per corpse since
this project can seek and the client couldn't. Distinguish from a real divergence
([[parity-is-the-search-not-the-defence]], which is about deliberately doing something else): here
the logic is decided, only the input is missing.

A filed finding can carry the same mistake — `PARITY-AUDIT.md` #4 called this branch "a divergence to
be ASKED about"; following the document over the rule was wrong.
