---
name: one-subagent-and-prefer-cheap-models
description: "Subagent model selection: opus for full implementation work (D196), sonnet for a bounded known task, haiku only for the sabotage verifier."
metadata:
  type: feedback
---

## D196: Opus for implementation work

Full implementation work goes to `model: "opus"`: porting a Valve class, a feature across several files, anything
with judgment calls about what the engine does. The owner, 2026-09-28: the subagent rule "was made with the idea
that subagents were only doing very very basic work, like creating a single known method, or a single known branch
to look at. not doing full implementation work like this".

**Why:** sonnet first passes on the HUD kept under-porting ("narrowed", "stated unavailable", "deferred"). Every one
turned out portable, at two or three review rounds and 500–700k tokens per agent. Measured in tokens per GOOD
outcome, the better model wins.

**How to apply:**

- `opus`: implementation work.
- `sonnet`: a bounded, known task — one method, one branch, one lookup with a stated answer shape.
- `haiku`: only the sabotage verifier (D177).
- Always name the model; the hook refuses an omitted one.
- Review stays mandatory (D145) whichever model ran.

## Early in the weekly limit: one subagent at a time, main loop by default (2026-09-28)

Not a permanent rule; it depends on where the week's token budget stands. Early in the week, do the work directly
and run at most one subagent, only for a bounded task that is cheaper on sonnet. Late in the week, with tokens left
over, several at once is fine.

**Check the budget, do not guess:** `mcp__ccd_session_mgmt__get_usage` (deferred; load via ToolSearch) reports the
5-hour and weekly limits with percent used and reset time. The weekly limit resets Sunday 10:00 local.

**Why:** each new agent pays input tokens to rebuild context and re-reads files it may not need; the main loop
already holds those reads.

## D168: Haiku refused for code and analysis subagents

Haiku produced wrong answers on decompiler output and SDK analysis. Pass `model: "haiku"` only for a purely
mechanical task with a fully self-contained prompt, such as the sabotage verifier or a remote-log number read.
