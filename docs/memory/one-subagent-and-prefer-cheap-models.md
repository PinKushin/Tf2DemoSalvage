---
name: one-subagent-and-prefer-cheap-models
description: "Subagent model selection: opus for full implementation work (D196), sonnet for a bounded known task, haiku only for the sabotage verifier."
metadata:
  type: feedback
---

## D196: Opus for implementation work

Full implementation work goes to `model: "opus"` — porting a Valve class, a multi-file feature,
anything with judgment about engine behaviour. Owner: the subagent rule "was made with the idea that
subagents were only doing very very basic work... not doing full implementation work like this."

**Why:** sonnet first passes consistently under-ported ("narrowed", "deferred") things that turned
out fully portable at two-three review rounds and 500-700k tokens per agent. Measured in tokens per
GOOD outcome, the better model wins.

**How to apply:**
- `opus`: implementation work.
- `sonnet`: a bounded, known task — one method, one branch, one lookup with a stated answer shape.
- `haiku`: only the sabotage verifier (D177).
- Always name the model; review stays mandatory (D145) whichever ran.

## Early in the weekly limit: one subagent at a time, main loop by default

Depends on token budget — early in the week, do work directly and run at most one subagent for a
bounded sonnet task; late in the week with tokens to spare, several at once is fine. Check the budget
(`get_usage`), don't guess.

**Why:** each new agent pays input tokens rebuilding context the main loop already holds.

## D168: Haiku refused for code and analysis subagents

Haiku produced wrong answers on decompiler output and SDK analysis. Only pass `haiku` for a purely
mechanical, fully self-contained task (sabotage verifier, a remote-log number read).

## Confirmed 2026-09-28: the split works, keep it

Owner: *"yes the current split is fine … this workflow has actually seemed to be faster than what we had
before, it took us a little over a week to do physics, but the hud system … took us like 2 days"*.
Split: main loop reads engine (Ghidra/SDK), writes brief; opus subagent implements; main loop reviews,
runs gates 2/3, merges, fixes CI. **Engine read before brief, always** — B426 brief skipped it, lighting
origin left open.

**Subagents skip TDD even when the brief says TESTS FIRST** (B429, B436, 2026-09-29): they report "code first, sabotaged
after". Brief must require a separate red commit or a logged red run BEFORE the implementation edit.
