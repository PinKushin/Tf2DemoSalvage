---
name: never-stop-a-working-subagent
description: A subagent that has started working keeps running; a new concurrency limit applies to the NEXT launch only.
metadata:
  node_type: memory
  type: feedback
  originSessionId: 124d1a9c-39d8-407f-871a-adb7c8b92a98
  modified: 2026-10-04T14:37:11.011Z
---

2026-10-04: owner said "we are dropping to one subagent at a time from here out"; three running agents were stopped,
throwing away their in-flight work. Owner: *"never fucking stop a running subagent that already has its fucking context
and started its first turn... you just fucking burned over 500k tokens for nothing, THAT IS WHY I SAID 'FROM HERE ON
OUT'"*.

**How to apply:**
- A limit change ("from here on out") applies to future launches; let running agents finish.
- Stopping is acceptable only right after spawning, before real work (a few hundred tokens).
- If one was stopped by mistake, resume it with SendMessage (context is kept) rather than relaunching.

Related: [[one-subagent-and-prefer-cheap-models]].
