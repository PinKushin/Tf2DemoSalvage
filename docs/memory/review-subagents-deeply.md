---
name: review-subagents-deeply
description: Subagents are weaker and single-pass - brief them with full context and review their branches line by line against the engine before merging
metadata:
  type: feedback
---

Owner, sending HUD work to parallel sonnet subagents overnight: *"make sure to review them well and
give them all the context they need, they won't reason or go multi turn like you will, they are not
as agenic."* He wants deep-review-level care without changing effort settings, because the main
session reviews subagent work and lighter workflows don't.

**Why:** a sonnet subagent does one pass. It won't notice a test that can't fail, a seam dead in
production (a green suite that missed a wiring gap), or a ported base class missing an override.

**How to apply:**
- A brief carries everything: branch base, existing files and what they do, engine files to read,
  project rules, what not to run, exactly what to report.
- Tell it to do the work itself — a nested hand-off risks the outer worktree being deleted on return.
- Review every branch before merging: read the diff against engine source, run its tests, sabotage
  one behaviour yourself, check the wiring reaches production (probe on a real demo), fix what it
  missed. Then gate.

Related: [[one-subagent-and-prefer-cheap-models]], [[no-task-cards]],
[[output-level-assertion-or-it-is-not-done]].
