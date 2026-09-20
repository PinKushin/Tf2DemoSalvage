---
name: one-subagent-and-prefer-cheap-models
description: "Subagent model selection: Sonnet for code/analysis, Haiku only for mechanical log-passing monitor tasks with a fully self-contained prompt."
metadata:
  type: feedback
---

## D168: Haiku refused for code and analysis subagents

Haiku has produced wrong answers on decompiler output reading and SDK analysis. Whether that was prompt or model is unknown, but the failure only appeared with Haiku. Sonnet is the floor for any subagent that reads code, makes a judgment, or synthesises findings.

**Pass `model: "haiku"` only when ALL of these hold:**

1. The task is purely mechanical — read a remote log, extract a number, pass it along.
2. No code inspection, no SDK reading, no synthesis, no judgment call.
3. The prompt is fully self-contained and tells the agent exactly what to find and return.

**Use `model: "sonnet"` (the default) for everything else**, including any subagent that touches code, docs, or makes a decision.

**Why:** Haiku failures on this project were on decompiler/SDK reads. For pure log-monitoring (mutation box check, "did the run finish, what was the score") the prompt is fully mechanical and Haiku is fine. (Owner stated 2026-09-19.)
