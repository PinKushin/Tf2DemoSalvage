---
name: decode-must-be-total
description: Anything that does not decode to 100% with no errors is wrong; the engine reads these files without complaint and the formats are documented.
metadata:
  type: feedback
---

**Anything that does not decode to 100% with no errors is wrong** — maps, materials, meshes,
everything this project reads. Owner's reasoning, not aspirational: Source runs these files without
error, and the Hammer/BSP side is fully documented, so a face we can't read is our defect.

**Why this matters:** the tempting move at 6% odd faces is to clamp/skip/fallback, each producing a
plausible picture that hides the defect. All were this shape: 219 displacements with lightmap
coordinates outside their own lightmap were CLAMPED (flat dark patches) — the real cause was using
the wrong mechanism (luxel coordinates come from corner ordering, never projected through
`lightmapVecs`); `tools/toolsblack` was dropped by a category rule though it's an ordinary drawn
surface; props with unresolved materials were skipped, leaving unnoticed holes.

**Same rule for the demo pipeline**, owner: *"build should basically never throw any exceptions, we
just read bytes, turn them into quake script, and compile that script back to a bite identical
demo."* A throw on a real demo is our defect — a `try/catch` is a BACKSTOP, never a design path. Pin
a guard's non-firing with deliberately synthetic garbage and say so, or the test reads as claiming
throwing is expected.

**How to apply:** treat any non-zero count of unread/skipped/clamped as an open defect, name it in
the log. Draw what can't be drawn yet in the engine's own missing-material chequer (magenta reports,
a hole doesn't). Related: [[research-before-code]], [[measure-the-output-not-the-capability]],
[[fallbacks-do-not-make-guesses-safe]], [[author-the-specimen-the-corpus-lacks]].
