---
name: decode-must-be-total
description: Anything that does not decode to 100% with no errors is wrong; the engine reads these files without complaint and the formats are documented.
metadata:
  type: feedback
---

**Anything that does not decode to 100%, with no errors, is wrong.** That covers maps, materials,
meshes, and everything else this project reads.

The owner's reasoning, and it is not aspirational: **Source runs these files without any errors, and
the Hammer/BSP side is completely documented.** So a face this project cannot read, a material it
cannot resolve, a model it skips, is a defect on our side rather than a quirk of the data.

**Why this matters more than it sounds:** the tempting move when 6% of faces read oddly is to clamp,
skip, or fall back — each of which produces a plausible picture and hides the defect. Every
significant bug this session was of that shape:

- 219 displacements had lightmap coordinates outside their own lightmap; they were CLAMPED, so each
  drew one flat shade and read as diffuse dark patches. The real cause was using the wrong mechanism
  entirely — a displacement's luxel coordinates are assigned from its corner ordering, never
  projected through `lightmapVecs`.
- `tools/toolsblack` was dropped by a category rule; it is an ordinary drawn surface, 80 faces and
  4.8 million square units.
- Props with unresolved materials were skipped, leaving holes nobody investigates.

**The same rule stated for the demo pipeline**, the owner, 2026-08-25:

> "build should basically never throw any exceptions, we just read bytes, turn them into quake
> script, and compile that script back to a bite identical demo"

That is `DemoTimeline.Build` and it is the project's whole loop. **A throw on a real demo is our
defect**, so a `try/catch` around a decode is a BACKSTOP and never a design path — writing one is
fine, treating its firing as normal is not. Two consequences worth keeping apart when writing tests:
a guard that should never fire still has to work, so pinning it is legitimate; but pin it with
deliberately synthetic garbage and say so, or the test reads as a claim that throwing is expected.

**How to apply:** treat a non-zero count of unread, skipped or clamped anything as an open defect and
name it in the log. Where something genuinely cannot be drawn yet, draw it in the engine's own
missing-material chequer rather than hiding it — magenta gets reported, a hole does not. And when a
map does turn out to contain something broken, handle it, because TF2 did. Related:
[[research-before-code]], [[measure-the-output-not-the-capability]],
[[fallbacks-do-not-make-guesses-safe]], [[author-the-specimen-the-corpus-lacks]].
