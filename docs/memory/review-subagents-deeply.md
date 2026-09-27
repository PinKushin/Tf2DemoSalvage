---
name: review-subagents-deeply
description: Subagents are weaker and single-pass - brief them with full context and review their branches line by line against the engine before merging
metadata:
  type: feedback
---

The owner, 2026-09-26, sending HUD work to parallel sonnet subagents overnight: *"make sure to review them well and give
them all the context they need, they won't reason or go multi turn like you will, they are not as agenic"*. He asked for
"ultracode"-level care without changing the effort setting, because the main session reviews subagent work and ultracode
workflows do not.

**Why:** a sonnet subagent does one pass. It will not notice that a test cannot fail, that a seam is dead in production
(the round-timer events never reached the panel because the feed filtered them — a green suite missed it), or that it
ported the base class and missed an override.

**How to apply:**
- A brief carries everything: the branch base, the files that already exist and what they do, the engine files to read,
  the project rules, what not to run, and exactly what to report.
- Tell it to do the work itself — one agent handed off to a nested agent and its worktree was deleted when it returned.
- Review every branch before merging: read the diff against the engine source, run its tests, sabotage one behaviour
  yourself, check the wiring reaches production (probe on a real demo), and fix what it missed. Then gate.
- Related: [[one-subagent-and-prefer-cheap-models]], [[no-task-cards]], [[output-level-assertion-or-it-is-not-done]].
