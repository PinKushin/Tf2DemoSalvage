---
name: a-filed-design-choice-may-not-be-one
description: B131 offered two shapes for a fix; Valve's source picked one and the codebase already had it, so the "choice" was four lines of plumbing.
metadata:
  type: project
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
  [[read-the-spec-before-measuring-our-data]] and [[nothing-is-closed]].
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
