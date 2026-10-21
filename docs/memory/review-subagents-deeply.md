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

## `dont-guide-the-reviewer` — a reviewer prompt gives scope and context, never the lead's hypotheses

Owner, 2026-10-20, seeing a second-round Fable prompt that listed what the lead suspected (child delay, lifetime default, units at a call site): *"yo shouldnt be guiding ffable at all"*.

**Why:** the point of the cross-model read is independence. A prompt naming the lead's suspicions anchors the reviewer on them, so it finds less that the lead missed.

**How to apply:** the prompt states branch, base, diff command, rules to read (CLAUDE.md, the finding), what the change is FOR in a sentence or two, what not to run, the report format. A second round says what was fixed since the first (facts), not where the lead thinks the fixes are weak. Check own suspicions yourself or raise them afterward.
